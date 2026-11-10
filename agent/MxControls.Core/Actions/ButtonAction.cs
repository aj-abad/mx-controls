using System.Text.Json.Serialization;
using MxControls.Platform;

namespace MxControls.Actions;

/// <summary>
/// What a button, or one direction of a gesture, does. <see cref="Type"/> is one of <see cref="ActionTypes"/>:
/// <c>hold</c> keeps <see cref="Keys"/> down for as long as the button is held, <c>press</c> taps them,
/// <c>system</c> runs <see cref="System"/>, <c>click</c> holds the mouse button <see cref="Mouse"/>,
/// <c>macro</c> runs the <see cref="Actions.Macro"/> called <see cref="Macro"/>, <c>layer</c> turns on the profile's
/// second layer as <see cref="Layer"/> says, <c>profile</c> switches to the profile <see cref="Profile"/>,
/// <c>open</c> opens <see cref="Open"/>, <c>appleShortcut</c> runs the macOS Shortcut <see cref="Shortcut"/>, and
/// <c>none</c> does nothing.
/// </summary>
/// <param name="Keys"><c>KeyboardEvent.code</c> names in press order, modifiers first.</param>
/// <param name="Repeat">
/// For <c>press</c>, <c>system</c> and <c>macro</c>: repeat while held, after <see cref="ButtonEngine.RepeatDelay"/>.
/// In a gesture direction, repeat instead for every <see cref="GestureTracker.DefaultRepeatStepMm"/> the movement
/// keeps going.
/// </param>
/// <param name="Mouse">For <c>click</c>: one of <see cref="MouseButtons"/>.</param>
/// <param name="Macro">For <c>macro</c>: the macro's ID.</param>
/// <param name="Layer">For <c>layer</c>: one of <see cref="LayerModes"/>.</param>
/// <param name="Profile">For <c>profile</c>: the ID of a profile with no programs of its own.</param>
/// <param name="Open">
/// For <c>open</c>: a web address, a file or folder path, or <c>app:</c> and an app's ID from the list of apps.
/// </param>
/// <param name="Shortcut">For <c>appleShortcut</c>: the Shortcut's identifier, which survives renaming it.</param>
/// <param name="Name">For an app or a Shortcut: its name when it was chosen, to show without looking it up.</param>
public sealed record ButtonAction(
    string Type, IReadOnlyList<string>? Keys = null, string? System = null, bool Repeat = false, string? Mouse = null,
    string? Macro = null, string? Layer = null, string? Profile = null, string? Open = null, string? Shortcut = null,
    string? Name = null)
{
    public static readonly ButtonAction Nothing = new(ActionTypes.None);

    [JsonIgnore]
    public bool IsSet => Type != ActionTypes.None;

    /// <summary>Whether it keeps the second layer on for as long as the button is held.</summary>
    [JsonIgnore]
    public bool HoldsLayer => Type == ActionTypes.Layer && Layer == LayerModes.Momentary;

    public string? Problem(IPlatform platform) => Type switch
    {
        ActionTypes.None => null,
        ActionTypes.Macro => string.IsNullOrEmpty(Macro) ? "A macro action needs a macro." : null,
        ActionTypes.Layer or ActionTypes.Profile when Repeat => "Switching layers or profiles can't repeat.",
        ActionTypes.Open or ActionTypes.AppleShortcut when Repeat => "Opening something or running a Shortcut can't repeat.",
        ActionTypes.Open when platform.Apps is null => "This platform can't open apps or files.",
        ActionTypes.Open => OpenTarget.Problem(Open),
        ActionTypes.AppleShortcut when platform.Shortcuts is null => "Shortcuts run only on a Mac.",
        ActionTypes.AppleShortcut => string.IsNullOrWhiteSpace(Shortcut) ? "A Shortcuts action needs a Shortcut." : null,
        ActionTypes.Layer => Layer is LayerModes.Momentary or LayerModes.Toggle ? null : $"Unknown layer mode '{Layer}'.",
        ActionTypes.Profile => string.IsNullOrEmpty(Profile) ? "A profile action needs a profile." : null,
        ActionTypes.Hold or ActionTypes.Press when Keys is not { Count: > 0 } => "A key action needs at least one key.",
        ActionTypes.Hold or ActionTypes.Press => Keys!.FirstOrDefault(k => !platform.Input.Supports(k)) is { } key
            ? $"This platform can't send the key '{key}'."
            : null,
        ActionTypes.System => System is not null && platform.SupportsSystem(System) ? null : $"Unknown system action '{System}'.",
        ActionTypes.Click => Mouse is not null && platform.Input.SupportsMouse(Mouse) ? null : $"Unknown mouse button '{Mouse}'.",
        _ => $"Unknown action type '{Type}'.",
    };
}

public static class ActionTypes
{
    public const string None = "none";
    public const string Hold = "hold";
    public const string Press = "press";
    public const string System = "system";
    public const string Click = "click";
    public const string Macro = "macro";
    public const string Layer = "layer";
    public const string Profile = "profile";
    public const string Open = "open";
    public const string AppleShortcut = "appleShortcut";
}

/// <summary>
/// What an <c>open</c> action's target is: an app, by <see cref="AppPrefix"/> and the ID the platform listed it
/// under; a web address or other link, by its scheme; or else a file or folder path.
/// </summary>
public static class OpenTarget
{
    public const string AppPrefix = "app:";
    public const int MaxLength = 2048;

    public static bool IsApp(string target) => target.StartsWith(AppPrefix, StringComparison.Ordinal);

    /// <summary>A link with a scheme of two letters or more, so a Windows drive such as <c>C:</c> stays a path.</summary>
    public static bool IsLink(string target)
    {
        int colon = target.IndexOf(':');
        return colon >= 2 && char.IsAsciiLetter(target[0])
            && target[..colon].All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.')
            && !IsApp(target);
    }

    public static string? Problem(string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || target == AppPrefix)
            return "An open action needs something to open.";
        if (target.Length > MaxLength)
            return $"What an open action opens is at most {MaxLength} characters.";
        if (IsLink(target) && target.EndsWith("://", StringComparison.Ordinal))
            return "An open action needs a whole web address.";
        return null;
    }
}

/// <summary>
/// Names for <see cref="ButtonAction.Layer"/>: <c>momentary</c> keeps the second layer on while the button is held,
/// and <c>toggle</c> turns it on or off with each press.
/// </summary>
public static class LayerModes
{
    public const string Momentary = "momentary";
    public const string Toggle = "toggle";
}

/// <summary>Names for <see cref="ButtonAction.Mouse"/>. Back and forward are the side buttons apps use for history.</summary>
public static class MouseButtons
{
    public const string Middle = "middle";
    public const string Back = "back";
    public const string Forward = "forward";
}

/// <summary>
/// Names for <see cref="ButtonAction.System"/>: things each system does its own way, which a platform runs if it
/// has them. Task View is Mission Control on a Mac.
/// </summary>
public static class SystemActions
{
    public const string TaskView = "taskView";
    public const string AppExpose = "appExpose";
    public const string ShowDesktop = "showDesktop";
    public const string Launchpad = "launchpad";
    public const string SwitchApps = "switchApps";
    public const string ZoomIn = "zoomIn";
    public const string ZoomOut = "zoomOut";
    public const string Search = "search";
    public const string Screenshot = "screenshot";
    public const string Emoji = "emoji";
    public const string Dictation = "dictation";
    public const string Notifications = "notifications";
    public const string LockScreen = "lockScreen";
    public const string FileManager = "fileManager";
    public const string Settings = "settings";
    public const string Calculator = "calculator";
    public const string MinimizeWindow = "minimizeWindow";
    public const string MaximizeWindow = "maximizeWindow";
    public const string FullScreen = "fullScreen";
    public const string CloseWindow = "closeWindow";
    public const string QuitApp = "quitApp";
    public const string MicMute = "micMute";
    public const string BrightnessUp = "brightnessUp";
    public const string BrightnessDown = "brightnessDown";
    public const string LookUp = "lookUp";

    public static readonly string[] All =
    [
        TaskView, AppExpose, ShowDesktop, Launchpad, SwitchApps, ZoomIn, ZoomOut, Search, Screenshot, Emoji, Dictation,
        Notifications, LockScreen, FileManager, Settings, Calculator, MinimizeWindow, MaximizeWindow, FullScreen,
        CloseWindow, QuitApp, MicMute, BrightnessUp, BrightnessDown, LookUp,
    ];
}

/// <summary>
/// A gesture: an action for a click without movement, and a step for each direction, which can lead further.
/// Double and triple clicks are null when unset.
/// </summary>
/// <param name="SpacingMm">Distance between steps in a run, <see cref="MinSpacingMm"/> to <see cref="MaxSpacingMm"/>.</param>
public sealed record GestureActions(
    ButtonAction Click, GestureStep Up, GestureStep Down, GestureStep Left, GestureStep Right,
    ButtonAction? DoubleClick = null, ButtonAction? TripleClick = null, double SpacingMm = GestureActions.DefaultSpacingMm)
{
    public const double DefaultSpacingMm = 6;
    public const double MinSpacingMm = 4;
    public const double MaxSpacingMm = 15;
    public const int MaxRun = 3;
    public const int MaxTurns = 2;

    public GestureStep this[GestureDirection direction] => direction switch
    {
        GestureDirection.Up => Up,
        GestureDirection.Down => Down,
        GestureDirection.Left => Left,
        _ => Right,
    };

    /// <summary>
    /// What's wrong with the directions and spacing, leaving the clicks out; the rules are the same for a gesture
    /// preset, which has no clicks.
    /// </summary>
    public string? MovesProblem(IPlatform platform)
    {
        if (SpacingMm is < MinSpacingMm or > MaxSpacingMm)
            return $"The spacing between steps is {MinSpacingMm} to {MaxSpacingMm} mm.";
        foreach (var direction in GestureStep.Directions)
        {
            if (StepProblem(this[direction], direction, run: 1, turns: 0, platform) is { } problem)
                return problem;
        }
        return null;
    }

    private static string? StepProblem(GestureStep step, GestureDirection came, int run, int turns, IPlatform platform)
    {
        if (step.Action.Problem(platform) is { } problem)
            return problem;
        if (step.Action.HoldsLayer)
            return "A gesture step can toggle the second layer but not hold it.";
        // Only the first four directions run as they're reached and last while held; other steps tap when the button
        // comes up, except one that repeats, which runs as it's reached and again as the movement keeps going.
        if ((run > 1 || turns > 0 || !step.IsLeaf) && step.Type == ActionTypes.Hold)
            return "Only a first direction with nothing beyond it can hold keys.";
        if (step[GestureStep.Opposite(came)] is not null)
            return "A step can't lead back the way it came.";
        bool extends = step[came] is not null;
        if (extends && step.Repeat)
            return "A step that repeats is the last in its direction, since going on repeats it.";
        if (extends && GestureStep.Across(came).Any(d => step[d] is not null))
            return "A step can go further or turn, but not both.";
        foreach (var next in GestureStep.Directions)
        {
            if (step[next] is not { } beyond)
                continue;
            int nextRun = next == came ? run + 1 : 1, nextTurns = next == came ? turns : turns + 1;
            if (nextRun > MaxRun)
                return $"A run is at most {MaxRun} steps.";
            if (nextTurns > MaxTurns)
                return $"A path turns at most {MaxTurns} times.";
            if (StepProblem(beyond, next, nextRun, nextTurns, platform) is { } deeper)
                return deeper;
        }
        return null;
    }
}

/// <summary>
/// A step of a gesture: its action, in the same fields as <see cref="ButtonAction"/> so gestures from before paths
/// read unchanged, and the steps beyond it: the same direction to extend, or the two at right angles to branch.
/// </summary>
public sealed record GestureStep(
    string Type, IReadOnlyList<string>? Keys = null, string? System = null, bool Repeat = false, string? Mouse = null,
    string? Macro = null, GestureStep? Up = null, GestureStep? Down = null, GestureStep? Left = null, GestureStep? Right = null,
    string? Layer = null, string? Profile = null, string? Open = null, string? Shortcut = null, string? Name = null)
{
    public static readonly GestureStep Nothing = new(ActionTypes.None);

    public static readonly GestureDirection[] Directions =
        [GestureDirection.Up, GestureDirection.Down, GestureDirection.Left, GestureDirection.Right];

    [JsonIgnore]
    public ButtonAction Action => new(Type, Keys, System, Repeat, Mouse, Macro, Layer, Profile, Open, Shortcut, Name);

    /// <summary>Every step: this one, then the ones beyond it.</summary>
    [JsonIgnore]
    public IEnumerable<GestureStep> All => [this, .. Directions.SelectMany(d => this[d]?.All ?? [])];

    /// <summary>This step and every step beyond it with their actions changed.</summary>
    public GestureStep Map(Func<ButtonAction, ButtonAction> change)
    {
        var action = change(Action);
        return new(action.Type, action.Keys, action.System, action.Repeat, action.Mouse, action.Macro,
            Up?.Map(change), Down?.Map(change), Left?.Map(change), Right?.Map(change), action.Layer, action.Profile,
            action.Open, action.Shortcut, action.Name);
    }

    [JsonIgnore]
    public bool IsLeaf => Up is null && Down is null && Left is null && Right is null;

    public GestureStep? this[GestureDirection direction] => direction switch
    {
        GestureDirection.Up => Up,
        GestureDirection.Down => Down,
        GestureDirection.Left => Left,
        _ => Right,
    };

    /// <summary>Whether the steps beyond this one turn off the way it was reached.</summary>
    public bool Branches(GestureDirection came) => Across(came).Any(d => this[d] is not null);

    public static GestureStep Of(ButtonAction action) =>
        new(action.Type, action.Keys, action.System, action.Repeat, action.Mouse, action.Macro, Layer: action.Layer, Profile: action.Profile,
            Open: action.Open, Shortcut: action.Shortcut, Name: action.Name);

    public static GestureDirection Opposite(GestureDirection direction) => direction switch
    {
        GestureDirection.Up => GestureDirection.Down,
        GestureDirection.Down => GestureDirection.Up,
        GestureDirection.Left => GestureDirection.Right,
        _ => GestureDirection.Left,
    };

    public static GestureDirection[] Across(GestureDirection direction) =>
        direction is GestureDirection.Up or GestureDirection.Down
            ? [GestureDirection.Left, GestureDirection.Right]
            : [GestureDirection.Up, GestureDirection.Down];
}

/// <summary>
/// Saved moves that buttons follow: editing one changes every button that uses it, in every profile. It has no
/// clicks; those stay on each button. Built-in presets keep their IDs, so an edited one is stored under the same ID.
/// </summary>
/// <param name="Icon">A Phosphor icon name, as profiles have, or null.</param>
public sealed record GesturePreset(
    string Id, string Name, GestureStep Up, GestureStep Down, GestureStep Left, GestureStep Right,
    double SpacingMm = GestureActions.DefaultSpacingMm, string? Icon = null)
{
    public GestureActions With(ButtonAction click, ButtonAction? doubleClick, ButtonAction? tripleClick) =>
        new(click, Up, Down, Left, Right, doubleClick, tripleClick, SpacingMm);

    public string? Problem(IPlatform platform) =>
        string.IsNullOrWhiteSpace(Name) ? "A gesture preset needs a name." : With(ButtonAction.Nothing, null, null).MovesProblem(platform);
}

/// <summary>
/// What one, two and three quick clicks run. A count without an action is null; <see cref="Most"/> is the
/// highest count with one, so a click only waits for another when a higher count is set.
/// </summary>
public sealed record ClickActions(ButtonAction Single, ButtonAction? Double, ButtonAction? Triple)
{
    public int Most => Triple is { IsSet: true } ? 3 : Double is { IsSet: true } ? 2 : 1;

    public ButtonAction this[int count] => count switch
    {
        3 => Triple ?? ButtonAction.Nothing,
        2 => Double ?? ButtonAction.Nothing,
        _ => Single,
    };

    public string? Problem(IPlatform platform)
    {
        if (Triple is { IsSet: true } && Double is not { IsSet: true })
            return "A triple click needs a double click too.";
        if (Most > 1 && new[] { Single, Double, Triple }.Any(a => a is { Type: ActionTypes.Hold } or { Repeat: true } or { HoldsLayer: true }))
            return "With a double click set, each click runs once, so none of them can hold keys or a layer, or repeat.";
        return new[] { Double, Triple }.Select(a => a?.Problem(platform)).FirstOrDefault(p => p is not null);
    }
}

/// <summary>
/// What one button does: a single <see cref="Action"/>, a gesture, or a <see cref="RoseMenu"/>. A gesture follows a <see cref="Preset"/>, with
/// <see cref="Action"/> and the double and triple clicks as its clicks; <see cref="Resolve"/> fills in
/// <see cref="Gesture"/> from the preset before the engine runs it. Settings from before presets carry the
/// <see cref="Gesture"/> itself, clicks included, until they're migrated.
/// </summary>
/// <param name="Button">A button ID such as "gesture"; see <see cref="Features.ReprogControls.KnownButtons"/>.</param>
/// <param name="Mode"><c>action</c>, <c>gesture</c> or <c>rose</c>.</param>
/// <param name="DoubleClick">What two quick clicks run, or null. A gesture carried whole keeps its own.</param>
/// <param name="TripleClick">What three quick clicks run, or null.</param>
/// <param name="Preset">For <c>gesture</c>: the ID of the <see cref="GesturePreset"/> it follows.</param>
/// <param name="Rose">
/// For <c>rose</c>: the options shown while it's held. <see cref="Action"/> is then what a quick press that
/// never leaves the middle runs, if anything.
/// </param>
public sealed record ButtonBinding(
    string Button, string Mode, ButtonAction? Action = null, GestureActions? Gesture = null,
    ButtonAction? DoubleClick = null, ButtonAction? TripleClick = null, string? Preset = null, RoseMenu? Rose = null)
{
    public const string ActionMode = "action";
    public const string GestureMode = "gesture";
    public const string RoseMode = "rose";

    [JsonIgnore]
    public bool IsGesture => Mode == GestureMode;

    [JsonIgnore]
    public bool IsRose => Mode == RoseMode;

    /// <summary>Whether its control reports movement instead of moving the pointer while held.</summary>
    [JsonIgnore]
    public bool NeedsRawXY => IsGesture || IsRose;

    /// <summary>The click actions of whichever mode this is; only call it on a binding without a problem.</summary>
    [JsonIgnore]
    public ClickActions Clicks => IsGesture && Gesture is { } gesture
        ? new(gesture.Click, gesture.DoubleClick, gesture.TripleClick)
        : IsRose ? new(Action ?? ButtonAction.Nothing, null, null) : new(Action!, DoubleClick, TripleClick);

    public string? Problem(IPlatform platform)
    {
        if (!Features.ReprogControls.KnownButtons.ContainsKey(Button))
            return $"Unknown button '{Button}'.";
        return Mode switch
        {
            ActionMode when Action is null => "An action binding needs an action.",
            ActionMode => Action.Problem(platform) ?? Clicks.Problem(platform),
            GestureMode when Gesture is not null =>
                Gesture.Click.Problem(platform) ?? Gesture.MovesProblem(platform) ?? Clicks.Problem(platform) ?? ClickHoldsLayer(Gesture.Click),
            GestureMode when Preset is null => "A gesture binding needs a preset or its five actions.",
            GestureMode when Action is null => "A gesture binding needs an action for its click.",
            GestureMode => Action.Problem(platform) ?? Clicks.Problem(platform) ?? ClickHoldsLayer(Action),
            RoseMode when Rose is null => "A rose binding needs its options.",
            RoseMode when DoubleClick is not null || TripleClick is not null => "A rose's quick press runs alone, without double or triple clicks.",
            RoseMode when Action is { Type: ActionTypes.Hold } or { Repeat: true } or { HoldsLayer: true } =>
                "A rose's quick press runs when you let go, so it can't hold keys or a layer, or repeat.",
            RoseMode => Action?.Problem(platform) ?? Rose.Problem(platform),
            _ => $"Unknown mode '{Mode}'.",
        };
    }

    private static string? ClickHoldsLayer(ButtonAction click) =>
        click.HoldsLayer ? "A gesture's click runs when you let go, so it can toggle the second layer but not hold it." : null;

    /// <summary>Every action it runs, the moves of a gesture carried whole included.</summary>
    [JsonIgnore]
    public IEnumerable<ButtonAction> AllActions =>
        new[] { Action, DoubleClick, TripleClick }.OfType<ButtonAction>().Concat(Gesture is { } g
            ? [g.Click, .. new[] { g.DoubleClick, g.TripleClick }.OfType<ButtonAction>(),
                .. new[] { g.Up, g.Down, g.Left, g.Right }.SelectMany(s => s.All).Select(s => s.Action)]
            : []).Concat(Rose?.Items.Where(i => !i.PassThrough).Select(i => i.Action) ?? []);

    /// <summary>
    /// This binding as the engine runs it: a gesture with its preset's moves filled in. One whose preset has gone
    /// is just its clicks.
    /// </summary>
    public ButtonBinding Resolve(Func<string, GesturePreset?> find)
    {
        if (!IsGesture || Gesture is not null || Preset is null || Action is null)
            return this;
        return find(Preset) is { } preset
            ? this with { Gesture = preset.With(Action, DoubleClick, TripleClick) }
            : this with { Mode = ActionMode };
    }

    public static ButtonBinding Hold(string button, params string[] keys) =>
        new(button, ActionMode, new ButtonAction(ActionTypes.Hold, keys));
}
