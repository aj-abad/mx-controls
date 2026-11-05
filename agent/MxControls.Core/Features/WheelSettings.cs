using MxControls.Hidpp;

namespace MxControls.Features;

/// <param name="Ratchet">True for ratchet, false for free spin.</param>
/// <param name="AutoDisengage">Scroll speed that drops the ratchet into free spin; 0xFF keeps it ratcheted.</param>
/// <param name="Torque">Ratchet force, when the wheel supports tuning it.</param>
public readonly record struct SmartShiftState(bool Ratchet, byte AutoDisengage, byte Torque);

public readonly record struct HiResWheelState(int Multiplier, bool CanInvert, bool HasRatchetSwitch, bool HighResolution, bool Inverted, bool Diverted);

public readonly record struct ThumbWheelState(int NativeResolution, int DivertedResolution, bool Diverted, bool Inverted);

/// <param name="Rotation">Counts turned since the last event, in diverted resolution.</param>
/// <param name="Phase">0 idle, 1 the first event of a turn, 2 during one, 3 the last.</param>
public readonly record struct ThumbWheelTurn(int Rotation, int Phase);

/// <summary>SMART_SHIFT_ENHANCED (0x2111), HIRES_WHEEL (0x2121) and THUMB_WHEEL (0x2150).</summary>
public sealed class WheelSettings(MxDevice device)
{
    public async Task<(bool TunableTorque, byte DefaultAutoDisengage, byte DefaultTorque)> GetSmartShiftCapabilitiesAsync()
    {
        var r = await device.CallAsync(FeatureIds.SmartShiftEnhanced, 0);
        return ((r[0] & 0x01) != 0, r[1], r[2]);
    }

    public async Task<SmartShiftState> GetSmartShiftAsync()
    {
        var r = await device.CallAsync(FeatureIds.SmartShiftEnhanced, 1);
        return new SmartShiftState(r[0] == 2, r[1], r[2]);
    }

    public Task SetSmartShiftAsync(SmartShiftState state) =>
        device.CallAsync(FeatureIds.SmartShiftEnhanced, 2, (byte)(state.Ratchet ? 2 : 1), state.AutoDisengage, state.Torque);

    public async Task<HiResWheelState> GetHiResWheelAsync()
    {
        var caps = await device.CallAsync(FeatureIds.HiResWheel, 0);
        var mode = await device.CallAsync(FeatureIds.HiResWheel, 1);
        return new HiResWheelState(caps[0], (caps[1] & 0x08) != 0, (caps[1] & 0x04) != 0,
            HighResolution: (mode[0] & 0x02) != 0, Inverted: (mode[0] & 0x04) != 0, Diverted: (mode[0] & 0x01) != 0);
    }

    /// <summary>
    /// Reverses the main wheel in the mouse itself, so other mice and trackpads keep their direction. Leaves the
    /// wheel's other modes as they are. Returns whether the mouse had to change.
    /// </summary>
    public async Task<bool> SetHiResInvertedAsync(bool inverted)
    {
        const byte InvertBit = 0x04;
        byte mode = (await device.CallAsync(FeatureIds.HiResWheel, 1))[0];
        if (((mode & InvertBit) != 0) == inverted)
            return false;
        await device.CallAsync(FeatureIds.HiResWheel, 2, (byte)(inverted ? mode | InvertBit : mode & ~InvertBit));
        return true;
    }

    public async Task<ThumbWheelState> GetThumbWheelAsync()
    {
        var info = await device.CallAsync(FeatureIds.ThumbWheel, 0);
        var status = await device.CallAsync(FeatureIds.ThumbWheel, 1);
        return new ThumbWheelState((info[0] << 8) | info[1], (info[2] << 8) | info[3], status[0] == 1, status[1] != 0);
    }

    /// <summary>Diverted, the wheel reports turns as events instead of scrolling; inverted flips both.</summary>
    public Task SetThumbWheelAsync(bool divert, bool invert) =>
        device.CallAsync(FeatureIds.ThumbWheel, 2, (byte)(divert ? 1 : 0), (byte)(invert ? 1 : 0));

    /// <summary>A turn of the diverted thumb wheel, or null for other events.</summary>
    public ThumbWheelTurn? ParseThumbWheel(HidppEvent e)
    {
        if (!device.TryGetIndex(FeatureIds.ThumbWheel, out var index) || e.FeatureIndex != index || e.Function != 0)
            return null;
        return new ThumbWheelTurn((short)((e.Payload[0] << 8) | e.Payload[1]), e.Payload[4]);
    }
}
