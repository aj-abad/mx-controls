using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MxControls.Imaging;

namespace MxControls.Platform.Windows;

/// <summary>An executable's own icon as a PNG, read the way Explorer shows it.</summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class WindowsIcons
{
    public const int Size = 48;

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon;
        public int HotspotX, HotspotY;
        public nint Mask, Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHDefExtractIconW(string file, int index, uint flags, nint* large, nint* small, uint size);

    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetIconInfo(nint icon, IconInfo* info);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyIcon(nint icon);
    [LibraryImport("gdi32.dll")] private static partial int GetDIBits(nint dc, nint bitmap, uint start, uint lines, void* bits, BitmapInfoHeader* info, uint usage);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteObject(nint item);
    [LibraryImport("user32.dll")] private static partial nint GetDC(nint hwnd);
    [LibraryImport("user32.dll")] private static partial int ReleaseDC(nint hwnd, nint dc);

    /// <summary>The icon of a program file, or null when it has none Windows can read.</summary>
    public static byte[]? Extract(string path)
    {
        nint icon = 0;
        if (SHDefExtractIconW(path, 0, 0, &icon, null, Size) != 0 || icon == 0)
            return null;
        try
        {
            return ToPng(icon);
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    private static byte[]? ToPng(nint icon)
    {
        IconInfo info;
        if (!GetIconInfo(icon, &info))
            return null;
        nint screen = GetDC(0);
        try
        {
            if (info.Color == 0)
                return null;
            var header = new BitmapInfoHeader { Size = (uint)sizeof(BitmapInfoHeader), Width = Size, Height = -Size, Planes = 1, BitCount = 32 };
            var pixels = new byte[Size * Size * 4];
            fixed (byte* bits = pixels)
            {
                if (GetDIBits(screen, info.Color, 0, Size, bits, &header, 0) == 0)
                    return null;
            }
            // Icons from before alpha channels have none, and say what's see-through in their mask instead.
            bool hasAlpha = false;
            for (int i = 3; i < pixels.Length && !hasAlpha; i += 4)
                hasAlpha = pixels[i] != 0;
            if (!hasAlpha && info.Mask != 0)
            {
                var mask = new byte[Size * Size * 4];
                var maskHeader = header;
                fixed (byte* bits = mask)
                    GetDIBits(screen, info.Mask, 0, Size, bits, &maskHeader, 0);
                for (int i = 0; i < pixels.Length; i += 4)
                    pixels[i + 3] = mask[i] == 0 ? (byte)255 : (byte)0;
            }
            return Png.FromBgra(pixels, Size, Size);
        }
        finally
        {
            ReleaseDC(0, screen);
            if (info.Color != 0)
                DeleteObject(info.Color);
            if (info.Mask != 0)
                DeleteObject(info.Mask);
        }
    }
}
