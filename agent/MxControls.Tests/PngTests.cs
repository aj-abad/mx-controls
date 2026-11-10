using System.Buffers.Binary;
using System.IO.Compression;
using MxControls.Imaging;

namespace MxControls.Tests;

public class PngTests
{
    [Fact]
    public void PixelsComeBackAsRgbaRowsWithValidChunks()
    {
        // Two pixels: opaque blue, then half-transparent red, in BGRA.
        byte[] bgra = [255, 0, 0, 255, 0, 0, 255, 128];
        var png = Png.FromBgra(bgra, 2, 1);

        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
        var chunks = new Dictionary<string, byte[]>();
        for (int at = 8; at < png.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            chunks[type] = png[(at + 8)..(at + 8 + length)];
            at += 12 + length;
        }
        Assert.Equal(["IHDR", "IDAT", "IEND"], chunks.Keys);
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(chunks["IHDR"]));
        Assert.Equal(6, chunks["IHDR"][9]);

        using var zlib = new ZLibStream(new MemoryStream(chunks["IDAT"]), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        Assert.Equal([0, 0, 0, 255, 255, 255, 0, 0, 128], raw.ToArray());
    }

    [Fact]
    public void PixelsThatDontFillTheImageAreRefused() =>
        Assert.Throws<ArgumentException>(() => Png.FromBgra(new byte[4], 2, 1));
}
