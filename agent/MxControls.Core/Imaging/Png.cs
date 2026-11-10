using System.Buffers.Binary;
using System.IO.Compression;

namespace MxControls.Imaging;

/// <summary>A minimal PNG writer for program icons, so the agent needs no imaging library.</summary>
public static class Png
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Encodes rows of straight (not premultiplied) BGRA pixels, top row first, as an RGBA PNG.</summary>
    public static byte[] FromBgra(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (bgra.Length != width * height * 4)
            throw new ArgumentException("The pixels don't fill the image.", nameof(bgra));
        var raw = new byte[height * (width * 4 + 1)];
        for (int y = 0; y < height; y++)
        {
            int row = y * (width * 4 + 1);
            // Filter type 0: each row is stored as it is.
            raw[row] = 0;
            for (int x = 0; x < width; x++)
            {
                int from = (y * width + x) * 4, to = row + 1 + x * 4;
                raw[to] = bgra[from + 2];
                raw[to + 1] = bgra[from + 1];
                raw[to + 2] = bgra[from];
                raw[to + 3] = bgra[from + 3];
            }
        }

        using var output = new MemoryStream();
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8; // bits per channel
        header[9] = 6; // RGBA
        Chunk(output, "IHDR", header);
        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(raw);
            Chunk(output, "IDAT", compressed.ToArray());
        }
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);
        Span<byte> name = [(byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3]];
        output.Write(name);
        output.Write(data);
        uint crc = Crc(Crc(0xFFFFFFFF, name), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(word, crc);
        output.Write(word);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
