using MxControls.Features;
using MxControls.Hid;
using MxControls.Hidpp;

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

    public HidInterface Interface { get; }
    public HidppChannel Channel { get; }
    public IReadOnlyList<FeatureEntry> Features { get; private set; } = [];
    public ReprogControls Controls { get; }
    public PointerSettings Pointer { get; }
    public WheelSettings Wheels { get; }
    public HapticSettings Haptics { get; }
    public DeviceInfo Info { get; }

    private MxDevice(HidInterface device)
    {
        Interface = device;
        Channel = new HidppChannel(device);
        Controls = new ReprogControls(this);
        Pointer = new PointerSettings(this);
        Wheels = new WheelSettings(this);
        Haptics = new HapticSettings(this);
        Info = new DeviceInfo(this);
    }

    public static IEnumerable<HidInterface> FindBluetooth() =>
        HidInterface.Enumerate(LogitechVendorId)
            .Where(d => d.UsagePage == BluetoothHidppUsagePage && d.Usage == BluetoothHidppLongUsage);

    public static async Task<MxDevice> OpenAsync(HidInterface? device = null, CancellationToken cancel = default)
    {
        device ??= FindBluetooth().FirstOrDefault()
            ?? throw new InvalidOperationException("No Logitech mouse is connected over Bluetooth.");
        var mx = new MxDevice(device);
        try
        {
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

    public Task<byte[]> CallAsync(ushort featureId, byte function, params byte[] args) =>
        _indexes.TryGetValue(featureId, out var index)
            ? Channel.CallAsync(index, function, args)
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
