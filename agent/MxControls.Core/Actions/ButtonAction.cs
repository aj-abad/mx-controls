using System.Text.Json.Serialization;
using MxControls.Platform;

namespace MxControls.Actions;

/// <summary>
/// What a button, or one direction of a gesture, does. <see cref="Type"/> is one of <see cref="ActionTypes"/>:
/// <c>hold</c> keeps <see cref="Keys"/> down for as long as the button is held, <c>press</c> taps them,
/// <c>system</c> runs <see cref="System"/>, <c>click</c> holds the mouse button <see cref="Mouse"/>,
/// and <c>none</c> does nothing.
/// </summary>
/// <param name="Keys"><c>KeyboardEvent.code</c> names in press order, modifiers first.</param>
/// <param name="Repeat">For <c>press</c> and <c>system</c>: repeat while held, after <see cref="ButtonEngine.RepeatDelay"/>.</param>
/// <param name="Mouse">For <c>click</c>: one of <see cref="MouseButtons"/>.</param>
public sealed record ButtonAction(
    string Type, IReadOnlyList<string>? Keys = null, string? System = null, bool Repeat = false, string? Mouse = null)
{
    public static readonly ButtonAction Nothing = new(ActionTypes.None);

    public string? Problem(IPlatform platform) => Type switch
    {
        ActionTypes.None => null,
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
}

/// <summary>Names for <see cref="ButtonAction.Mouse"/>. Back and forward are the side buttons apps use for history.</summary>
public static class MouseButtons
{
    public const string Middle = "middle";
    public const string Back = "back";
    public const string Forward = "forward";
}

/// <summary>Names for <see cref="ButtonAction.System"/>.</summary>
public static class SystemActions
{
    public const string TaskView = "taskView";
    public const string ShowDesktop = "showDesktop";
    public const string ZoomIn = "zoomIn";
    public const string ZoomOut = "zoomOut";
}

/// <summary>A gesture: an action for a click without movement, and one for each direction.</summary>
public sealed record GestureActions(ButtonAction Click, ButtonAction Up, ButtonAction Down, ButtonAction Left, ButtonAction Right)
{
    public ButtonAction this[GestureDirection direction] => direction switch
    {
        GestureDirection.Up => Up,
        GestureDirection.Down => Down,
        GestureDirection.Left => Left,
        _ => Right,
    };

    [JsonIgnore]
    public IEnumerable<ButtonAction> All => [Click, Up, Down, Left, Right];
}

/// <summary>What one button does: a single <see cref="Action"/>, or a <see cref="Gesture"/>.</summary>
/// <param name="Button">A button ID such as "gesture"; see <see cref="Features.ReprogControls.KnownButtons"/>.</param>
/// <param name="Mode"><c>action</c> or <c>gesture</c>.</param>
public sealed record ButtonBinding(string Button, string Mode, ButtonAction? Action = null, GestureActions? Gesture = null)
{
    public const string ActionMode = "action";
    public const string GestureMode = "gesture";

    [JsonIgnore]
    public bool IsGesture => Mode == GestureMode;

    public string? Problem(IPlatform platform)
    {
        if (!Features.ReprogControls.KnownButtons.ContainsKey(Button))
            return $"Unknown button '{Button}'.";
        return Mode switch
        {
            ActionMode when Action is null => "An action binding needs an action.",
            ActionMode => Action.Problem(platform),
            GestureMode when Gesture is null => "A gesture binding needs its five actions.",
            GestureMode => Gesture.All.Select(a => a.Problem(platform)).FirstOrDefault(p => p is not null),
            _ => $"Unknown mode '{Mode}'.",
        };
    }

    public static ButtonBinding Hold(string button, params string[] keys) =>
        new(button, ActionMode, new ButtonAction(ActionTypes.Hold, keys));
}
