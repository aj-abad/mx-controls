namespace MxControls.Input;

/// <summary>
/// Spots global shortcuts in the keys a platform watch reports. A shortcut is modifiers plus one key, with exactly
/// those modifiers held. Only that key is hidden from apps, and its repeats and release with it; modifiers always
/// pass, since apps have already seen them go down.
/// </summary>
public sealed class ShortcutMatcher
{
    private static readonly HashSet<string> Modifiers =
        ["ShiftLeft", "ShiftRight", "ControlLeft", "ControlRight", "AltLeft", "AltRight", "MetaLeft", "MetaRight"];

    private sealed record Entry(int Id, string Key, HashSet<string> Modifiers);

    private volatile IReadOnlyList<Entry> _entries = [];
    private volatile bool _suspended;

    // Only the platform's watch thread touches these.
    private readonly HashSet<string> _heldModifiers = [];
    private readonly HashSet<string> _hidden = [];

    /// <summary>A shortcut was pressed. Runs on the watch thread, so it must return quickly.</summary>
    public event Action<int>? Fired;

    /// <summary>While true, nothing fires or is hidden, so a shortcut being recorded reaches the recorder.</summary>
    public bool Suspended
    {
        get => _suspended;
        set => _suspended = value;
    }

    /// <summary>Why these keys can't be a global shortcut, or null if they can.</summary>
    public static string? Problem(IReadOnlyList<string> keys)
    {
        if (keys.Count == 0 || Modifiers.Contains(keys[^1]) || keys.SkipLast(1).Any(k => !Modifiers.Contains(k)))
            return "A shortcut needs one key besides its modifiers, such as Ctrl + Alt + 1.";
        // Caps Lock reports its light rather than the key, so it can't be told apart from its release.
        if (keys[^1] == "CapsLock")
            return "Caps Lock can't be part of a shortcut.";
        // The key stops reaching apps, so a bare letter could no longer be typed.
        if (keys.Count == 1 && !IsFunctionKey(keys[0]))
            return "A shortcut needs a modifier such as Ctrl or Alt, unless it's a function key.";
        return null;
    }

    public static bool Same(IReadOnlyList<string> a, IReadOnlyList<string> b) => a.ToHashSet().SetEquals(b);

    private static bool IsFunctionKey(string key) => key.Length > 1 && key[0] == 'F' && key.Skip(1).All(char.IsAsciiDigit);

    public void Set(IEnumerable<(int Id, IReadOnlyList<string> Keys)> shortcuts) =>
        _entries = shortcuts.Where(s => Problem(s.Keys) is null).Select(s => new Entry(s.Id, s.Keys[^1], s.Keys.SkipLast(1).ToHashSet())).ToArray();

    /// <summary>Takes one key going down or up, auto-repeat included, and says whether to hide it from apps.</summary>
    public bool OnKey(string? code, bool down)
    {
        if (code is null)
            return false;
        if (Modifiers.Contains(code))
        {
            if (down)
                _heldModifiers.Add(code);
            else
                _heldModifiers.Remove(code);
            return false;
        }
        if (!down)
            return _hidden.Remove(code);
        // A repeat of a key already hidden; it fires once per press.
        if (_hidden.Contains(code))
            return true;
        if (_suspended)
            return false;
        foreach (var entry in _entries)
        {
            if (entry.Key == code && entry.Modifiers.SetEquals(_heldModifiers))
            {
                _hidden.Add(code);
                Fired?.Invoke(entry.Id);
                return true;
            }
        }
        return false;
    }
}
