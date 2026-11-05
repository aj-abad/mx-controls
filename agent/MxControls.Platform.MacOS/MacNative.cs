using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MxControls.Platform.MacOS;

/// <summary>Core Foundation, shared by the rest of the backend. Objects from Create and Copy calls are released by hand.</summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class Cf
{
    private const string Library = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint Utf8 = 0x08000100;
    private const int NumberLong = 4;
    private const int NumberDouble = 13;

    private static readonly nint Handle = NativeLibrary.Load(Library);
    public static readonly nint DefaultMode = Constant(Handle, "kCFRunLoopDefaultMode");
    public static readonly nint CommonModes = Constant(Handle, "kCFRunLoopCommonModes");
    public static readonly nint True = Constant(Handle, "kCFBooleanTrue");
    public static readonly nint DictionaryKeyCallbacks = NativeLibrary.GetExport(Handle, "kCFTypeDictionaryKeyCallBacks");
    public static readonly nint DictionaryValueCallbacks = NativeLibrary.GetExport(Handle, "kCFTypeDictionaryValueCallBacks");

    [LibraryImport(Library)] public static partial void CFRelease(nint value);
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)] private static partial nint CFStringCreateWithCString(nint allocator, string text, uint encoding);
    [LibraryImport(Library)] private static partial nint CFStringGetLength(nint text);
    [LibraryImport(Library)] private static partial byte CFStringGetCString(nint text, byte* buffer, nint size, uint encoding);
    [LibraryImport(Library)] private static partial nuint CFGetTypeID(nint value);
    [LibraryImport(Library)] private static partial nuint CFStringGetTypeID();
    [LibraryImport(Library)] private static partial nuint CFNumberGetTypeID();
    [LibraryImport(Library)] private static partial byte CFNumberGetValue(nint number, int type, void* value);
    [LibraryImport(Library)] private static partial nint CFNumberCreate(nint allocator, int type, void* value);
    [LibraryImport(Library)] public static partial nint CFArrayGetCount(nint array);
    [LibraryImport(Library)] public static partial nint CFArrayGetValueAtIndex(nint array, nint index);
    [LibraryImport(Library)] public static partial nint CFSetGetCount(nint set);
    [LibraryImport(Library)] public static partial void CFSetGetValues(nint set, nint* values);
    [LibraryImport(Library)] public static partial nint CFDictionaryGetValue(nint dictionary, nint key);
    [LibraryImport(Library)] public static partial nint CFDictionaryCreate(nint allocator, nint* keys, nint* values, nint count, nint keyCallbacks, nint valueCallbacks);
    [LibraryImport(Library)] public static partial nint CFRunLoopGetCurrent();
    [LibraryImport(Library)] public static partial void CFRunLoopStop(nint loop);
    [LibraryImport(Library)] public static partial int CFRunLoopRunInMode(nint mode, double seconds, byte returnAfterSourceHandled);
    [LibraryImport(Library)] public static partial void CFRunLoopAddSource(nint loop, nint source, nint mode);
    [LibraryImport(Library)] public static partial void CFRunLoopRemoveSource(nint loop, nint source, nint mode);
    [LibraryImport(Library)] public static partial nint CFMachPortCreateRunLoopSource(nint allocator, nint port, nint order);

    /// <summary>The value of an exported constant, such as a CFStringRef key.</summary>
    public static nint Constant(nint library, string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name));

    /// <summary>A new CFString, for the caller to release.</summary>
    public static nint String(string text) => CFStringCreateWithCString(0, text, Utf8);

    /// <summary>A new CFNumber, for the caller to release.</summary>
    public static nint Number(long value) => CFNumberCreate(0, NumberLong, &value);

    public static string? ToText(nint value)
    {
        if (value == 0 || CFGetTypeID(value) != CFStringGetTypeID())
            return null;
        int size = (int)CFStringGetLength(value) * 4 + 1;
        var buffer = new byte[size];
        fixed (byte* start = buffer)
        {
            if (CFStringGetCString(value, start, size, Utf8) == 0)
                return null;
            return Marshal.PtrToStringUTF8((nint)start);
        }
    }

    public static long? ToLong(nint value)
    {
        if (value == 0 || CFGetTypeID(value) != CFNumberGetTypeID())
            return null;
        long number;
        return CFNumberGetValue(value, NumberLong, &number) != 0 ? number : null;
    }

    public static double? ToDouble(nint value)
    {
        if (value == 0 || CFGetTypeID(value) != CFNumberGetTypeID())
            return null;
        double number;
        return CFNumberGetValue(value, NumberDouble, &number) != 0 ? number : null;
    }
}

/// <summary>The path of a running process, by libproc.</summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class Proc
{
    private const string Library = "/usr/lib/libSystem.B.dylib";
    private const int MaxPath = 4096;

    [LibraryImport(Library)] private static partial int proc_pidpath(int pid, byte* buffer, uint size);
    [LibraryImport(Library)] private static partial int proc_listallpids(int* buffer, int size);

    public static string? PathOf(int pid)
    {
        byte* buffer = stackalloc byte[MaxPath];
        int length = proc_pidpath(pid, buffer, MaxPath);
        return length > 0 ? Marshal.PtrToStringUTF8((nint)buffer, length) : null;
    }

    public static int[] All()
    {
        int count = proc_listallpids(null, 0);
        if (count <= 0)
            return [];
        // Room for processes started between the two calls.
        var pids = new int[count + 64];
        fixed (int* start = pids)
            count = proc_listallpids(start, pids.Length * sizeof(int));
        return count > 0 ? pids[..count] : [];
    }
}
