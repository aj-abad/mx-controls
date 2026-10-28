namespace MxControls.Input;

/// <summary>Parses key names such as "A", "F13", "Shift" or "0x7C" into Windows virtual-key codes.</summary>
public static class KeyNames
{
    private static readonly Dictionary<string, ushort> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Shift"] = 0xA0, ["RShift"] = 0xA1, ["Ctrl"] = 0xA2, ["RCtrl"] = 0xA3, ["Alt"] = 0xA4, ["RAlt"] = 0xA5,
        ["Win"] = 0x5B, ["Space"] = 0x20, ["Enter"] = 0x0D, ["Tab"] = 0x09, ["Esc"] = 0x1B, ["Backspace"] = 0x08,
        ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28, ["CapsLock"] = 0x14, ["PrintScreen"] = 0x2C,
        ["VolumeUp"] = 0xAF, ["VolumeDown"] = 0xAE, ["Mute"] = 0xAD, ["PlayPause"] = 0xB3,
        ["NextTrack"] = 0xB0, ["PreviousTrack"] = 0xB1,
    };

    public static bool TryParse(string name, out ushort virtualKey)
    {
        virtualKey = 0;
        if (Named.TryGetValue(name, out virtualKey))
            return true;
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0]))
        {
            virtualKey = char.ToUpperInvariant(name[0]);
            return true;
        }
        if (name.Length is 2 or 3 && (name[0] is 'F' or 'f') && int.TryParse(name[1..], out int f) && f is >= 1 and <= 24)
        {
            virtualKey = (ushort)(0x70 + f - 1);
            return true;
        }
        return name.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && ushort.TryParse(name[2..], System.Globalization.NumberStyles.HexNumber, null, out virtualKey)
            && virtualKey is > 0 and < 0xFF;
    }
}
