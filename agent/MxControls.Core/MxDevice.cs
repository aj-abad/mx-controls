using MxControls.Features;
using MxControls.Hidpp;
using MxControls.Platform;

namespace MxControls;

public sealed record FeatureEntry(byte Index, ushort Id, byte Version, bool Hidden, bool Engineering)
{
    public string Name => FeatureIds.NameOf(Id);
}

/// <summary>An MX mouse connected directly over Bluetooth, with its feature table resolved.</summary>
public sealed class MxDevice : IAsyncDisposable
{
    public const ushort LogitechVendorId = 0x046D;
    // Bluetooth Logitech devices expose HID++ long reports on this vendor collection.
    private const ushort BluetoothHidppUsagePage = 0xFF43;
    private const ushort BluetoothHidppLongUsage = 0x0202;

    private readonly Dictionary<ushort, byte> _indexes = [];

    public HidDeviceInfo Interface { get; }
    public HidppChannel Channel { get; }
    public IReadOnlyList<FeatureEntry> Features { get; private set; } = [];
    public ReprogControls Controls { get; }
    public PointerSettings Pointer { get; }
    public WheelSettings Wheels { get; }
    public HapticSettings Haptics { get; }
    public HostSettings Hosts { get; }
    public DeviceInfo Info { get; }

    private MxDevice(HidDeviceInfo device, Stream stream)
    {
        Interface = device;
        Channel = new HidppChannel(stream);
        Controls = new ReprogControls(this);
        Pointer = new PointerSettings(this);
        Wheels = new WheelSettings(this);
        Haptics = new HapticSettings(this);
        Hosts = new HostSettings(this);
        Info = new DeviceInfo(this);
    }

    public static IEnumerable<HidDeviceInfo> FindBluetooth(IHidBackend hid) =>
        hid.Enumerate(LogitechVendorId)
            .Where(d => d.UsagePage == BluetoothHidppUsagePage && d.Usage == BluetoothHidppLongUsage);

    /// <param name="first">
    /// Runs before the feature table is read, which takes dozens of calls, for what can't wait for them. It finds the
    /// features it needs with <see cref="FindFeatureAsync"/>.
    /// </param>
    public static async Task<MxDevice> OpenAsync(IHidBackend hid, HidDeviceInfo? device = null, CancellationToken cancel = default, Func<MxDevice, Task>? first = null)
    {
        device ??= FindBluetooth(hid).FirstOrDefault()
            ?? throw new InvalidOperationException("No Logitech mouse is connected over Bluetooth.");
        var mx = new MxDevice(device, hid.Open(device));
        try
        {
            if (first is not null)
                await first(mx);
            await mx.ReadFeatureTableAsync(cancel);
            return mx;
        }
        catch
        {
            await mx.DisposeAsync();
            throw;
        }
    }

    public bool Has(ushort featureId) => _indexes.ContainsKey(featureId);

    /// <summary>Asks ROOT where one feature lives, a single call where the whole table takes dozens. False if the mouse has none.</summary>
    public async Task<bool> FindFeatureAsync(ushort featureId)
    {
        if (Has(featureId))
            return true;
        var reply = await Channel.CallAsync(0, 0, new[] { (byte)(featureId >> 8), (byte)(featureId & 0xFF) });
        if (reply[0] == 0)
            return false;
        _indexes[featureId] = reply[0];
        return true;
    }

    /// <summary>A call got no reply and was sent again: the feature and function, for logging.</summary>
    public event Action<string>? CallRetried;

    /// <summary>
    /// Calls a feature function. One that gets no reply is sent once more, which is safe because the calls set or
    /// read whole values; a late reply to the first send answers the second.
    /// </summary>
    public async Task<byte[]> CallAsync(ushort featureId, byte function, params byte[] args)
    {
        if (!_indexes.TryGetValue(featureId, out var index))
            throw new NotSupportedException($"{Interface.Product} has no {FeatureIds.NameOf(featureId)} feature");
        try
        {
            return await Channel.CallAsync(index, function, args);
        }
        catch (TimeoutException)
        {
            CallRetried?.Invoke($"{FeatureIds.NameOf(featureId)} function {function}");
        }
        try
        {
            return await Channel.CallAsync(index, function, args);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"The mouse didn't answer {FeatureIds.NameOf(featureId)} function {function}, twice.");
        }
    }

    public Task SendAsync(ushort featureId, byte function, params byte[] args) =>
        _indexes.TryGetValue(featureId, out var index)
            ? Channel.SendAsync(index, function, args)
            : throw new NotSupportedException($"{Interface.Product} has no {FeatureIds.NameOf(featureId)} feature");

    public bool TryGetIndex(ushort featureId, out byte index) => _indexes.TryGetValue(featureId, out index);

    private async Task ReadFeatureTableAsync(CancellationToken cancel)
    {
        // ROOT is always index 0; ask it where FEATURE_SET lives, then walk the table.
        var root = await Channel.CallAsync(0, 0, new byte[] { FeatureIds.FeatureSet >> 8, FeatureIds.FeatureSet & 0xFF }, cancel);
        byte featureSet = root[0];
        int count = (await Channel.CallAsync(featureSet, 0, ReadOnlyMemory<byte>.Empty, cancel))[0];

        var features = new List<FeatureEntry>();
        for (int i = 0; i <= count; i++)
        {
            var r = await Channel.CallAsync(featureSet, 1, new[] { (byte)i }, cancel);
            var entry = new FeatureEntry((byte)i, (ushort)((r[0] << 8) | r[1]), r[3],
                Hidden: (r[2] & 0x40) != 0, Engineering: (r[2] & 0x20) != 0);
            features.Add(entry);
            _indexes[entry.Id] = entry.Index;
        }
        Features = features;
    }

    public ValueTask DisposeAsync() => Channel.DisposeAsync();
}
