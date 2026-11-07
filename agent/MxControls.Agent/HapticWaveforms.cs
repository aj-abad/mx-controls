using MxControls.Features;

namespace MxControls.Agent;

public static class HapticWaveforms
{
    public static IReadOnlyList<string> Names { get; } = Enum.GetNames<HapticWaveform>();

    /// <summary>The waveform's exact name, ignoring case. Enum.TryParse would also take any number as a waveform.</summary>
    public static string? Find(string name) =>
        Names.FirstOrDefault(known => known.Equals(name, StringComparison.OrdinalIgnoreCase));
}
