using MxControls.Hidpp;

namespace MxControls.Features;

public sealed record DpiRange(int Min, int Max, int Step);

/// <summary>ADJUSTABLE_DPI (0x2201) for sensor 0.</summary>
public sealed class PointerSettings(MxDevice device)
{
    public async Task<(int Current, int Default)> GetDpiAsync()
    {
        var r = await device.CallAsync(FeatureIds.AdjustableDpi, 2, 0);
        return ((r[1] << 8) | r[2], (r[3] << 8) | r[4]);
    }

    /// <summary>Reads the DPI list; a word of 0xE0nn means "every nn between the neighbouring values".</summary>
    public async Task<DpiRange?> GetDpiRangeAsync()
    {
        var r = await device.CallAsync(FeatureIds.AdjustableDpi, 1, 0);
        var words = Enumerable.Range(0, 7).Select(i => (r[1 + i * 2] << 8) | r[2 + i * 2]).TakeWhile(w => w != 0).ToArray();
        return words is [var min, var step, var max] && (step & 0xE000) == 0xE000
            ? new DpiRange(min, max, step & 0x1FFF)
            : null;
    }

    public Task SetDpiAsync(int dpi) =>
        device.CallAsync(FeatureIds.AdjustableDpi, 3, 0, (byte)(dpi >> 8), (byte)dpi);
}
