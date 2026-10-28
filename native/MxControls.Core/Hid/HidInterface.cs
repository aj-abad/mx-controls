using Microsoft.Win32.SafeHandles;

namespace MxControls.Hid;

/// <summary>One HID top-level collection, as Windows exposes it.</summary>
public sealed record HidInterface(
    string Path, ushort VendorId, ushort ProductId, ushort UsagePage, ushort Usage,
    int InputReportLength, int OutputReportLength, string Product)
{
    public static IReadOnlyList<HidInterface> Enumerate(ushort? vendorId = null)
    {
        HidNative.HidD_GetHidGuid(out var guid);
        if (HidNative.CM_Get_Device_Interface_List_Size(out var length, ref guid, null, HidNative.InterfacesPresent) != 0)
            return [];
        var buffer = new char[length];
        if (HidNative.CM_Get_Device_Interface_List(ref guid, null, buffer, length, HidNative.InterfacesPresent) != 0)
            return [];

        var found = new List<HidInterface>();
        foreach (var path in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryDescribe(path) is { } device && (vendorId is null || device.VendorId == vendorId))
                found.Add(device);
        }
        return found;
    }

    /// <summary>Opens the collection for overlapped reads and writes.</summary>
    public FileStream Open()
    {
        var handle = HidNative.CreateFile(Path, HidNative.GenericRead | HidNative.GenericWrite,
            HidNative.FileShareReadWrite, IntPtr.Zero, HidNative.OpenExisting, HidNative.FileFlagOverlapped, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new IOException($"Could not open {Product} ({UsagePage:X4}:{Usage:X4})",
                System.Runtime.InteropServices.Marshal.GetHRForLastWin32Error());
        return new FileStream(handle, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);
    }

    private static HidInterface? TryDescribe(string path)
    {
        // Zero access is enough for attributes and caps, even on collections Windows keeps for itself.
        using SafeFileHandle handle = HidNative.CreateFile(path, 0, HidNative.FileShareReadWrite,
            IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            return null;

        var attributes = new HidNative.Attributes { Size = System.Runtime.InteropServices.Marshal.SizeOf<HidNative.Attributes>() };
        if (!HidNative.HidD_GetAttributes(handle, ref attributes))
            return null;
        if (!HidNative.HidD_GetPreparsedData(handle, out var preparsed))
            return null;
        HidNative.Caps caps;
        try
        {
            if (HidNative.HidP_GetCaps(preparsed, out caps) != HidNative.HidpStatusSuccess)
                return null;
        }
        finally
        {
            HidNative.HidD_FreePreparsedData(preparsed);
        }

        Span<char> name = stackalloc char[128];
        var product = HidNative.HidD_GetProductString(handle, name, (uint)(name.Length * sizeof(char)))
            ? new string(name).TrimEnd('\0')
            : "";
        return new HidInterface(path, attributes.VendorId, attributes.ProductId, caps.UsagePage, caps.Usage,
            caps.InputReportByteLength, caps.OutputReportByteLength, product);
    }
}
