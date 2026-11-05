namespace MxControls.Input;

/// <summary>
/// Turns key names typed on the command line into W3C <c>KeyboardEvent.code</c> names: "Shift" becomes
/// "ShiftLeft", "A" becomes "KeyA", "5" becomes "Digit5". Codes pass through unchanged.
/// </summary>
public static class KeyNames
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Shift"] = "ShiftLeft", ["RShift"] = "ShiftRight", ["Ctrl"] = "ControlLeft", ["RCtrl"] = "ControlRight",
        ["Alt"] = "AltLeft", ["RAlt"] = "AltRight", ["Win"] = "MetaLeft", ["Cmd"] = "MetaLeft", ["Meta"] = "MetaLeft",
        ["Esc"] = "Escape", ["Left"] = "ArrowLeft", ["Up"] = "ArrowUp", ["Right"] = "ArrowRight", ["Down"] = "ArrowDown",
        ["VolumeUp"] = "AudioVolumeUp", ["VolumeDown"] = "AudioVolumeDown", ["Mute"] = "AudioVolumeMute",
        ["PlayPause"] = "MediaPlayPause", ["NextTrack"] = "MediaTrackNext", ["PreviousTrack"] = "MediaTrackPrevious",
    };

    public static string Normalize(string name)
    {
        if (Aliases.TryGetValue(name, out var code))
            return code;
        if (name.Length == 1 && char.IsAsciiLetter(name[0]))
            return $"Key{char.ToUpperInvariant(name[0])}";
        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
            return $"Digit{name}";
        return name;
    }
}
