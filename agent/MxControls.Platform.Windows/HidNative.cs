using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MxControls.Platform.Windows;

internal static partial class HidNative
{
    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint FileShareReadWrite = 0x3;
    public const uint OpenExisting = 3;
    public const uint FileFlagOverlapped = 0x40000000;
    public const uint InterfacesPresent = 0;
    public const int HidpStatusSuccess = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    public struct Attributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    /// <summary>HIDP_CAPS. Only the leading fields are read; the rest is reserved space.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    public struct Caps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
    }

    [LibraryImport("hid.dll")]
    public static partial void HidD_GetHidGuid(out Guid guid);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool HidD_GetAttributes(SafeFileHandle device, ref Attributes attributes);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr data);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool HidD_FreePreparsedData(IntPtr data);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetCaps(IntPtr data, out Caps caps);

    [LibraryImport("hid.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool HidD_GetProductString(SafeFileHandle device, Span<char> buffer, uint length);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_List_SizeW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int CM_Get_Device_Interface_List_Size(out uint length, ref Guid interfaceClass, string? deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_ListW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int CM_Get_Device_Interface_List(ref Guid interfaceClass, string? deviceId, Span<char> buffer, uint length, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
}
