using MxControls.Actions;

namespace MxControls.Agent;

/// <summary>
/// Moves recorded key combinations into saved macros. Before macros, a custom shortcut was a <c>press</c> action
/// holding its own keys; the app's built-in actions, such as Copy, are <c>press</c> actions too and stay as they are.
/// </summary>
public static class MacroMigration
{
    private static readonly bool Mac = OperatingSystem.IsMacOS();
    private static readonly string Mod = Mac ? "MetaLeft" : "ControlLeft";

    /// <summary>The keys of the app's built-in actions on this platform, as `PRESETS` in `actions.ts` has them.</summary>
    private static readonly HashSet<string> BuiltIn = new string[][]
    {
        [Mod, "KeyT"], [Mod, "KeyW"], [Mod, "ShiftLeft", "KeyT"], ["ControlLeft", "Tab"], ["ControlLeft", "ShiftLeft", "Tab"],
        [Mod, "KeyC"], [Mod, "KeyX"], [Mod, "KeyV"], [Mod, "KeyZ"], Mac ? [Mod, "ShiftLeft", "KeyZ"] : [Mod, "KeyY"], [Mod, "KeyA"],
        Mac ? ["ControlLeft", "ArrowLeft"] : ["ControlLeft", "MetaLeft", "ArrowLeft"],
        Mac ? ["ControlLeft", "ArrowRight"] : ["ControlLeft", "MetaLeft", "ArrowRight"],
        ["MediaPlayPause"], ["MediaTrackNext"], ["MediaTrackPrevious"], ["AudioVolumeUp"], ["AudioVolumeDown"], ["AudioVolumeMute"],
    }.Select(Chord).ToHashSet();

    private static string Chord(IEnumerable<string> keys) => string.Join("+", keys);

    /// <summary>
    /// Settings with every custom <c>press</c> action, in every profile, thumb wheel and gesture preset, running a
    /// macro instead. The same keys share one macro, named after them. Returns the same instance when there's
    /// nothing to move.
    /// </summary>
    public static AgentSettings Migrated(AgentSettings settings)
    {
        var macros = (settings.Macros ?? []).ToList();
        bool moved = false;

        ButtonAction? Move(ButtonAction? action)
        {
            if (action is not { Type: ActionTypes.Press, Keys: { Count: > 0 } keys } || BuiltIn.Contains(Chord(keys)))
                return action;
            moved = true;
            var macro = macros.FirstOrDefault(m => m.Steps is [{ Type: MacroStepTypes.Keys, Keys: { } same }] && same.SequenceEqual(keys));
            if (macro is null)
            {
                macro = new Macro(Profile.NewId(), Label(keys), [new MacroStep(MacroStepTypes.Keys, keys)]);
                macros.Add(macro);
            }
            return new ButtonAction(ActionTypes.Macro, Repeat: action.Repeat, Macro: macro.Id);
        }

        var profiles = settings.AllProfiles.Select(profile => profile with
        {
            Bindings = profile.AllBindings.Select(b => b with
            {
                Action = Move(b.Action),
                DoubleClick = Move(b.DoubleClick),
                TripleClick = Move(b.TripleClick),
                Gesture = b.Gesture is { } g
                    ? g with
                    {
                        Click = Move(g.Click)!, DoubleClick = Move(g.DoubleClick), TripleClick = Move(g.TripleClick),
                        Up = g.Up.Map(a => Move(a)!), Down = g.Down.Map(a => Move(a)!),
                        Left = g.Left.Map(a => Move(a)!), Right = g.Right.Map(a => Move(a)!),
                    }
                    : null,
            }).ToArray(),
            ThumbWheel = profile.ThumbWheel is { } wheel ? wheel with { Left = Move(wheel.Left), Right = Move(wheel.Right) } : null,
        }).ToArray();
        var presets = settings.GesturePresets?.Select(p => p with
        {
            Up = p.Up.Map(a => Move(a)!), Down = p.Down.Map(a => Move(a)!), Left = p.Left.Map(a => Move(a)!), Right = p.Right.Map(a => Move(a)!),
        }).ToArray();

        return moved ? settings with { Profiles = profiles, GesturePresets = presets, Macros = macros } : settings;
    }

    private static readonly Dictionary<string, string> Names = new()
    {
        ["ControlLeft"] = "Ctrl", ["ControlRight"] = "Ctrl", ["ShiftLeft"] = "Shift", ["ShiftRight"] = "Shift",
        ["AltLeft"] = Mac ? "Option" : "Alt", ["AltRight"] = Mac ? "Option" : "Alt",
        ["MetaLeft"] = Mac ? "Cmd" : "Win", ["MetaRight"] = Mac ? "Cmd" : "Win",
        ["ArrowLeft"] = "←", ["ArrowRight"] = "→", ["ArrowUp"] = "↑", ["ArrowDown"] = "↓",
    };

    /// <summary>A name for recorded keys, such as "Ctrl + Shift + V", until the user gives it a better one.</summary>
    public static string Label(IEnumerable<string> keys) => string.Join(" + ", keys.Select(key =>
        Names.TryGetValue(key, out var name) ? name
        : key.StartsWith("Key") ? key[3..]
        : key.StartsWith("Digit") ? key[5..]
        : key));
}
