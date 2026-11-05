using MxControls.Hidpp;

namespace MxControls.Features;

public enum HapticWaveform : byte
{
    SharpStateChange = 0x00,
    DampStateChange = 0x01,
    SharpCollision = 0x02,
    DampCollision = 0x03,
    SubtleCollision = 0x04,
    HappyAlert = 0x05,
    AngryAlert = 0x06,
    Completed = 0x07,
    Square = 0x08,
    Wave = 0x09,
    Firework = 0x0A,
    Mad = 0x0B,
    Knock = 0x0C,
    Jingle = 0x0D,
    Ringing = 0x0E,
    WhisperCollision = 0x1B,
}

public readonly record struct HapticLevel(bool Enabled, int Level, bool FourLevelsOnly);

public readonly record struct ForceButton(int Number, bool Changeable, int Current, int Default, int Min, int Max);

/// <summary>
/// HAPTIC (0x19B0) and FORCE_SENSING_BUTTON (0x19C0). Function layouts follow Solaar, which supports
/// both on the MX Master 4; Logitech does not publish them.
/// </summary>
public sealed class HapticSettings(MxDevice device)
{
    public async Task<IReadOnlyList<HapticWaveform>> GetWaveformsAsync()
    {
        var r = await device.CallAsync(FeatureIds.Haptic, 0);
        uint mask = (uint)((r[4] << 24) | (r[5] << 16) | (r[6] << 8) | r[7]);
        return Enum.GetValues<HapticWaveform>().Where(w => (mask & (1u << (int)w)) != 0).ToArray();
    }

    public async Task<HapticLevel> GetLevelAsync()
    {
        var r = await device.CallAsync(FeatureIds.Haptic, 1);
        return new HapticLevel((r[0] & 0x01) != 0, r[1], (r[2] & 0x01) != 0);
    }

    /// <summary>Level 0 turns haptics off; Solaar keeps 50 as the stored level in that case.</summary>
    public Task SetLevelAsync(int level) =>
        level == 0
            ? device.CallAsync(FeatureIds.Haptic, 2, 0, 50)
            : device.CallAsync(FeatureIds.Haptic, 2, 1, (byte)Math.Clamp(level, 1, 100));

    public Task PlayAsync(HapticWaveform waveform) => device.CallAsync(FeatureIds.Haptic, 4, (byte)waveform);

    /// <summary>
    /// Plays a waveform without waiting for the reply, for feedback that must land at once. A round trip holds
    /// the channel for a radio interval or two, which other requests would queue behind.
    /// </summary>
    public Task TapAsync(HapticWaveform waveform) => device.SendAsync(FeatureIds.Haptic, 4, (byte)waveform);

    public async Task<IReadOnlyList<ForceButton>> GetForceButtonsAsync()
    {
        int count = (await device.CallAsync(FeatureIds.ForceSensingButton, 0))[0];
        var buttons = new List<ForceButton>(count);
        for (int n = 0; n < count; n++)
        {
            var info = await device.CallAsync(FeatureIds.ForceSensingButton, 1, (byte)n);
            var current = await device.CallAsync(FeatureIds.ForceSensingButton, 2, (byte)n);
            buttons.Add(new ForceButton(n, (info[1] & 0x01) != 0, (current[0] << 8) | current[1],
                Default: (info[2] << 8) | info[3], Min: (info[6] << 8) | info[7], Max: (info[4] << 8) | info[5]));
        }
        return buttons;
    }

    public Task SetForceAsync(int button, int force) =>
        device.CallAsync(FeatureIds.ForceSensingButton, 3, (byte)button, (byte)(force >> 8), (byte)force);
}
