using MxControls.Hidpp;

namespace MxControls.Features;

[Flags]
public enum ControlFlags
{
    None = 0,
    Mouse = 1 << 0,
    FKey = 1 << 1,
    HotKey = 1 << 2,
    FnToggle = 1 << 3,
    Reprogrammable = 1 << 4,
    Divertable = 1 << 5,
    PersistentlyDivertable = 1 << 6,
    Virtual = 1 << 7,
    RawXY = 1 << 8,
    ForceRawXY = 1 << 9,
    AnalyticsKeyEvents = 1 << 10,
    RawWheel = 1 << 11,
}

/// <summary>How a control currently reports, from getCidReporting.</summary>
public readonly record struct ControlReporting(bool Diverted, bool Persistent, bool RawXY, ushort RemappedTo);

public sealed record Control(ushort Cid, ushort TaskId, ControlFlags Flags, byte Group, byte GroupMask, ControlReporting Reporting)
{
    public string Name => ReprogControls.NameOf(Cid);
    public bool CanDivert => Flags.HasFlag(ControlFlags.Divertable);
}

/// <summary>
/// REPROG_CONTROLS_V4 (0x1B04). Diverted controls stop acting natively and report which of them are held;
/// none of the MX Master 4's controls are persistently divertable, so diversion is lost on reconnect.
/// </summary>
public sealed class ReprogControls(MxDevice device)
{
    private const byte DivertedButtonsEvent = 0;
    private const byte DivertedRawXYEvent = 1;

    public static readonly IReadOnlyDictionary<string, ushort> KnownButtons = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
    {
        ["middle"] = 0x0052, ["back"] = 0x0053, ["forward"] = 0x0056,
        ["gesture"] = 0x00C3, ["smartshift"] = 0x00C4, ["haptic"] = 0x01A0,
    };

    private static readonly Dictionary<ushort, string> Names = new()
    {
        [0x0050] = "Left", [0x0051] = "Right", [0x0052] = "Middle", [0x0053] = "Back", [0x0056] = "Forward",
        [0x00C3] = "Gesture", [0x00C4] = "Smart Shift", [0x00D7] = "Virtual Gesture", [0x01A0] = "Haptic",
    };

    public static string NameOf(ushort cid) => Names.TryGetValue(cid, out var name) ? name : $"CID {cid:X4}";

    public async Task<IReadOnlyList<Control>> GetControlsAsync()
    {
        int count = (await device.CallAsync(FeatureIds.ReprogControlsV4, 0))[0];
        var controls = new List<Control>(count);
        for (int i = 0; i < count; i++)
        {
            var r = await device.CallAsync(FeatureIds.ReprogControlsV4, 1, (byte)i);
            ushort cid = (ushort)((r[0] << 8) | r[1]);
            var flags = (ControlFlags)(r[4] | (r[8] << 8));
            controls.Add(new Control(cid, (ushort)((r[2] << 8) | r[3]), flags, r[6], r[7], await GetReportingAsync(cid)));
        }
        return controls;
    }

    public async Task<ControlReporting> GetReportingAsync(ushort cid)
    {
        var r = await device.CallAsync(FeatureIds.ReprogControlsV4, 2, (byte)(cid >> 8), (byte)cid);
        return new ControlReporting((r[2] & 0x01) != 0, (r[2] & 0x04) != 0, (r[2] & 0x10) != 0, (ushort)((r[3] << 8) | r[4]));
    }

    /// <summary>Sets temporary diversion and raw XY reporting; remap 0 leaves the mapping unchanged.</summary>
    public Task SetDivertAsync(ushort cid, bool divert, bool rawXY = false)
    {
        byte flags = (byte)((divert ? 0x01 : 0) | 0x02 | (rawXY ? 0x10 : 0) | 0x20);
        return device.CallAsync(FeatureIds.ReprogControlsV4, 3, (byte)(cid >> 8), (byte)cid, flags, 0, 0);
    }

    /// <summary>The diverted controls held right now, from a divertedButtons event; null for other events.</summary>
    public IReadOnlySet<ushort>? ParseHeld(HidppEvent e)
    {
        if (!device.TryGetIndex(FeatureIds.ReprogControlsV4, out var index) || e.FeatureIndex != index || e.Function != DivertedButtonsEvent)
            return null;
        var held = new HashSet<ushort>();
        for (int i = 0; i < 8; i += 2)
        {
            ushort cid = (ushort)((e.Payload[i] << 8) | e.Payload[i + 1]);
            if (cid != 0)
                held.Add(cid);
        }
        return held;
    }

    /// <summary>Pointer movement while a raw-XY diverted control is held, used for gestures.</summary>
    public (short Dx, short Dy)? ParseRawXY(HidppEvent e)
    {
        if (!device.TryGetIndex(FeatureIds.ReprogControlsV4, out var index) || e.FeatureIndex != index || e.Function != DivertedRawXYEvent)
            return null;
        return ((short)((e.Payload[0] << 8) | e.Payload[1]), (short)((e.Payload[2] << 8) | e.Payload[3]));
    }
}
