using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Channels;

namespace MxControls.Platform.MacOS;

/// <summary>
/// HID through IOKit's IOHIDManager. macOS has one device object for all of a mouse's collections, so each usage in
/// its DeviceUsagePairs is listed apart, as Windows lists them. The registry entry ID in the path finds it again.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed unsafe partial class MacHid : IHidBackend
{
    private const string Library = "/System/Library/Frameworks/IOKit.framework/IOKit";

    [LibraryImport(Library)] private static partial nint IOHIDManagerCreate(nint allocator, uint options);
    [LibraryImport(Library)] private static partial void IOHIDManagerSetDeviceMatching(nint manager, nint matching);
    [LibraryImport(Library)] private static partial nint IOHIDManagerCopyDevices(nint manager);
    [LibraryImport(Library)] internal static partial nint IOHIDDeviceGetProperty(nint device, nint key);
    [LibraryImport(Library)] private static partial uint IOHIDDeviceGetService(nint device);
    [LibraryImport(Library)] private static partial int IORegistryEntryGetRegistryEntryID(uint entry, ulong* id);
    [LibraryImport(Library)] private static partial nint IORegistryEntryIDMatching(ulong id);
    [LibraryImport(Library)] private static partial uint IOServiceGetMatchingService(uint mainPort, nint matching);
    [LibraryImport(Library)] private static partial int IOObjectRelease(uint value);
    [LibraryImport(Library)] private static partial nint IOHIDDeviceCreate(nint allocator, uint service);

    public IReadOnlyList<HidDeviceInfo> Enumerate(ushort vendorId)
    {
        nint manager = IOHIDManagerCreate(0, 0);
        if (manager == 0)
            return [];
        try
        {
            // Matching everything rather than the vendor: the list is short, and it needs no dictionary to build.
            IOHIDManagerSetDeviceMatching(manager, 0);
            nint set = IOHIDManagerCopyDevices(manager);
            if (set == 0)
                return [];
            try
            {
                int count = (int)Cf.CFSetGetCount(set);
                var devices = new nint[count];
                fixed (nint* start = devices)
                    Cf.CFSetGetValues(set, start);
                var found = new List<HidDeviceInfo>();
                foreach (var device in devices)
                    Describe(device, vendorId, found);
                return found;
            }
            finally
            {
                Cf.CFRelease(set);
            }
        }
        finally
        {
            Cf.CFRelease(manager);
        }
    }

    public IDisposable WatchArrivals(ushort vendorId, Action arrived) => new ArrivalWatcher(vendorId, arrived);

    public Stream Open(HidDeviceInfo device)
    {
        var id = ulong.Parse(device.Path.AsSpan(0, device.Path.IndexOf(':')));
        uint service = IOServiceGetMatchingService(0, IORegistryEntryIDMatching(id));
        if (service == 0)
            throw new IOException($"{device.Product} is no longer connected.");
        nint handle;
        try
        {
            handle = IOHIDDeviceCreate(0, service);
        }
        finally
        {
            IOObjectRelease(service);
        }
        if (handle == 0)
            throw new IOException($"Could not open {device.Product}.");
        return new MacHidStream(handle, device.Product);
    }

    private static void Describe(nint device, ushort vendorId, List<HidDeviceInfo> found)
    {
        if (Property(device, "VendorID") is not { } vendor || vendor != vendorId)
            return;
        ulong id;
        if (IORegistryEntryGetRegistryEntryID(IOHIDDeviceGetService(device), &id) != 0)
            return;
        ushort product = (ushort)(Property(device, "ProductID") ?? 0);
        string name = Text(device, "Product") ?? "";

        nint key = Cf.String("DeviceUsagePairs");
        nint pairs = IOHIDDeviceGetProperty(device, key);
        Cf.CFRelease(key);
        if (pairs == 0)
            return;
        nint pageKey = Cf.String("DeviceUsagePage"), usageKey = Cf.String("DeviceUsage");
        for (nint i = 0; i < Cf.CFArrayGetCount(pairs); i++)
        {
            nint pair = Cf.CFArrayGetValueAtIndex(pairs, i);
            if (Cf.ToLong(Cf.CFDictionaryGetValue(pair, pageKey)) is { } page && Cf.ToLong(Cf.CFDictionaryGetValue(pair, usageKey)) is { } usage)
                found.Add(new HidDeviceInfo($"{id}:{page:X4}:{usage:X4}", vendorId, product, (ushort)page, (ushort)usage, name));
        }
        Cf.CFRelease(pageKey);
        Cf.CFRelease(usageKey);
    }

    internal static long? Property(nint device, string name)
    {
        nint key = Cf.String(name);
        var value = Cf.ToLong(IOHIDDeviceGetProperty(device, key));
        Cf.CFRelease(key);
        return value;
    }

    private static string? Text(nint device, string name)
    {
        nint key = Cf.String(name);
        var value = Cf.ToText(IOHIDDeviceGetProperty(device, key));
        Cf.CFRelease(key);
        return value;
    }
}

/// <summary>
/// One open device. Input reports arrive through a callback on a run loop thread of the stream's own. Both directions
/// carry the report ID as the first byte, as they do on Windows.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe partial class MacHidStream : Stream
{
    private const string Library = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const int ReportTypeOutput = 1;
    private const int NotPermitted = unchecked((int)0xE00002E2);
    private const int DefaultReportSize = 64;
    private const uint RequestListenEvent = 1;

    // Opening a device doesn't always bring up the system's dialog, so it's asked for once.
    private static int s_requestedAccess;

    [LibraryImport(Library)] private static partial byte IOHIDRequestAccess(uint type);
    [LibraryImport(Library)] private static partial int IOHIDDeviceOpen(nint device, uint options);
    [LibraryImport(Library)] private static partial int IOHIDDeviceClose(nint device, uint options);
    [LibraryImport(Library)] private static partial void IOHIDDeviceScheduleWithRunLoop(nint device, nint loop, nint mode);
    [LibraryImport(Library)] private static partial void IOHIDDeviceUnscheduleFromRunLoop(nint device, nint loop, nint mode);
    [LibraryImport(Library)] private static partial void IOHIDDeviceRegisterInputReportCallback(nint device, byte* report, nint length,
        delegate* unmanaged<nint, int, nint, int, uint, byte*, nint, void> callback, nint context);
    [LibraryImport(Library)] private static partial void IOHIDDeviceRegisterRemovalCallback(nint device,
        delegate* unmanaged<nint, int, nint, void> callback, nint context);
    [LibraryImport(Library)] private static partial int IOHIDDeviceSetReport(nint device, int type, nint reportId, byte* report, nint length);

    private readonly nint _device;
    private readonly string _product;
    private readonly Channel<byte[]> _reports = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly object _writeLock = new();
    private readonly Thread _thread;
    private readonly byte* _buffer;
    private GCHandle _self;
    private nint _loop;
    private volatile bool _stopping;
    private bool _closed;

    public MacHidStream(nint device, string product)
    {
        _device = device;
        _product = product;
        int result = IOHIDDeviceOpen(device, 0);
        if (result != 0)
        {
            Cf.CFRelease(device);
            if (result == NotPermitted && Interlocked.Exchange(ref s_requestedAccess, 1) == 0)
                IOHIDRequestAccess(RequestListenEvent);
            throw new IOException(result == NotPermitted
                ? $"macOS won't let MX Studio open {product}. Allow it under System Settings > Privacy & Security > Input Monitoring."
                : $"Could not open {product} (IOKit error 0x{result:X8}).", result);
        }

        int size = (int)(MacHid.Property(device, "MaxInputReportSize") ?? DefaultReportSize);
        _buffer = (byte*)NativeMemory.Alloc((nuint)size);
        _self = GCHandle.Alloc(this);
        nint context = GCHandle.ToIntPtr(_self);
        IOHIDDeviceRegisterInputReportCallback(device, _buffer, size, &OnReport, context);
        IOHIDDeviceRegisterRemovalCallback(device, &OnRemoved, context);

        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "HID reports" };
        _thread.Start();
        ready.Wait();
    }

    private void Run(ManualResetEventSlim ready)
    {
        _loop = Cf.CFRunLoopGetCurrent();
        IOHIDDeviceScheduleWithRunLoop(_device, _loop, Cf.DefaultMode);
        ready.Set();
        // In short turns: a stop that comes before the loop is running would otherwise be lost.
        while (!_stopping)
            Cf.CFRunLoopRunInMode(Cf.DefaultMode, 1, 0);
        IOHIDDeviceUnscheduleFromRunLoop(_device, _loop, Cf.DefaultMode);
    }

    [UnmanagedCallersOnly]
    private static void OnReport(nint context, int result, nint sender, int type, uint reportId, byte* report, nint length)
    {
        try
        {
            if (result == 0 && GCHandle.FromIntPtr(context).Target is MacHidStream stream)
                stream._reports.Writer.TryWrite(new ReadOnlySpan<byte>(report, (int)length).ToArray());
        }
        catch
        {
            // An exception can't cross into IOKit.
        }
    }

    [UnmanagedCallersOnly]
    private static void OnRemoved(nint context, int result, nint sender)
    {
        try
        {
            if (GCHandle.FromIntPtr(context).Target is MacHidStream stream)
                stream._reports.Writer.TryComplete(new IOException($"{stream._product} disconnected."));
        }
        catch
        {
            // An exception can't cross into IOKit.
        }
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel = default) =>
        ReportReader.ReadAsync(_reports.Reader, buffer, cancel);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancel) =>
        ReadAsync(buffer.AsMemory(offset, count), cancel).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    // IOHIDDeviceSetReport waits for the device, so it stays off the caller's thread.
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel = default)
    {
        var report = buffer.ToArray();
        return new ValueTask(Task.Run(() => Send(report), cancel));
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancel) =>
        WriteAsync(buffer.AsMemory(offset, count), cancel).AsTask();

    public override void Write(byte[] buffer, int offset, int count) => Send(buffer.AsSpan(offset, count).ToArray());

    private void Send(byte[] report)
    {
        lock (_writeLock)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            fixed (byte* start = report)
            {
                int result = IOHIDDeviceSetReport(_device, ReportTypeOutput, report[0], start, report.Length);
                if (result != 0)
                    throw new IOException($"Could not write to {_product} (IOKit error 0x{result:X8}).", result);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        lock (_writeLock)
        {
            if (_closed)
                return;
            _closed = true;
        }
        _stopping = true;
        if (_loop != 0)
            Cf.CFRunLoopStop(_loop);
        _thread.Join(TimeSpan.FromSeconds(2));
        IOHIDDeviceClose(_device, 0);
        Cf.CFRelease(_device);
        NativeMemory.Free(_buffer);
        _self.Free();
        _reports.Writer.TryComplete();
        base.Dispose(disposing);
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>IOHIDManager's matching callback on a run loop thread of its own. It also fires once for each device already there.</summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe partial class ArrivalWatcher : IDisposable
{
    private const string Library = "/System/Library/Frameworks/IOKit.framework/IOKit";

    [LibraryImport(Library)] private static partial nint IOHIDManagerCreate(nint allocator, uint options);
    [LibraryImport(Library)] private static partial void IOHIDManagerSetDeviceMatching(nint manager, nint matching);
    [LibraryImport(Library)] private static partial void IOHIDManagerRegisterDeviceMatchingCallback(nint manager,
        delegate* unmanaged<nint, int, nint, nint, void> callback, nint context);
    [LibraryImport(Library)] private static partial void IOHIDManagerScheduleWithRunLoop(nint manager, nint loop, nint mode);
    [LibraryImport(Library)] private static partial void IOHIDManagerUnscheduleFromRunLoop(nint manager, nint loop, nint mode);
    [LibraryImport(Library)] private static partial int IOHIDManagerOpen(nint manager, uint options);
    [LibraryImport(Library)] private static partial int IOHIDManagerClose(nint manager, uint options);

    private readonly ushort _vendorId;
    private readonly Action _arrived;
    private readonly Thread _thread;
    private GCHandle _self;
    private nint _loop;
    private volatile bool _stopping;

    public ArrivalWatcher(ushort vendorId, Action arrived)
    {
        _vendorId = vendorId;
        _arrived = arrived;
        _self = GCHandle.Alloc(this);
        _thread = new Thread(Run) { IsBackground = true, Name = "HID arrivals" };
        _thread.Start();
    }

    private void Run()
    {
        nint manager = IOHIDManagerCreate(0, 0);
        if (manager == 0)
            return;
        nint key = Cf.String("VendorID"), value = Cf.Number(_vendorId);
        nint matching = Cf.CFDictionaryCreate(0, &key, &value, 1, Cf.DictionaryKeyCallbacks, Cf.DictionaryValueCallbacks);
        IOHIDManagerSetDeviceMatching(manager, matching);
        Cf.CFRelease(matching);
        Cf.CFRelease(key);
        Cf.CFRelease(value);

        _loop = Cf.CFRunLoopGetCurrent();
        IOHIDManagerRegisterDeviceMatchingCallback(manager, &OnArrived, GCHandle.ToIntPtr(_self));
        IOHIDManagerScheduleWithRunLoop(manager, _loop, Cf.DefaultMode);
        IOHIDManagerOpen(manager, 0);
        while (!_stopping)
            Cf.CFRunLoopRunInMode(Cf.DefaultMode, 1, 0);
        IOHIDManagerUnscheduleFromRunLoop(manager, _loop, Cf.DefaultMode);
        IOHIDManagerClose(manager, 0);
        Cf.CFRelease(manager);
    }

    [UnmanagedCallersOnly]
    private static void OnArrived(nint context, int result, nint sender, nint device)
    {
        try
        {
            (GCHandle.FromIntPtr(context).Target as ArrivalWatcher)?._arrived();
        }
        catch
        {
            // An exception can't cross into IOKit.
        }
    }

    public void Dispose()
    {
        _stopping = true;
        if (_loop != 0)
            Cf.CFRunLoopStop(_loop);
        _thread.Join(TimeSpan.FromSeconds(2));
        _self.Free();
    }
}

// Apart from the stream, whose unsafe context can't hold an await.
internal static class ReportReader
{
    public static async ValueTask<int> ReadAsync(ChannelReader<byte[]> reports, Memory<byte> buffer, CancellationToken cancel)
    {
        var report = await reports.ReadAsync(cancel);
        int length = Math.Min(report.Length, buffer.Length);
        report.AsMemory(0, length).CopyTo(buffer);
        return length;
    }
}
