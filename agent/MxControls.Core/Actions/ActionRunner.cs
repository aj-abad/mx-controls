using MxControls.Platform;

namespace MxControls.Actions;

/// <summary>
/// Starts and ends actions for held controls. A <c>hold</c> action keeps its keys down until <see cref="End"/>,
/// and a <c>click</c> its mouse button, so apps see the press and release when the real button makes them;
/// a repeating action fires again after <see cref="ButtonEngine.RepeatDelay"/> and every
/// <see cref="ButtonEngine.RepeatInterval"/> until then. Callers serialize calls.
/// </summary>
internal sealed class ActionRunner(IPlatform platform, object gate)
{
    private readonly Dictionary<ushort, (ButtonAction Action, Timer? Repeat)> _active = [];

    public void Begin(ushort cid, ButtonAction action)
    {
        End(cid);
        switch (action.Type)
        {
            case ActionTypes.Hold:
                foreach (var key in action.Keys!)
                    platform.Input.Down(key);
                break;
            case ActionTypes.Click:
                platform.Input.MouseDown(action.Mouse!);
                break;
            case ActionTypes.Press or ActionTypes.System:
                Run(action);
                break;
            default:
                return;
        }
        Timer? repeat = action.Repeat && action.Type is ActionTypes.Press or ActionTypes.System
            ? new Timer(_ => Repeat(cid, action), null, ButtonEngine.RepeatDelay, ButtonEngine.RepeatInterval)
            : null;
        _active[cid] = (action, repeat);
    }

    public void End(ushort cid)
    {
        if (!_active.Remove(cid, out var active))
            return;
        active.Repeat?.Dispose();
        if (active.Action.Type == ActionTypes.Hold)
        {
            foreach (var key in active.Action.Keys!.Reverse())
                platform.Input.Up(key);
        }
        else if (active.Action.Type == ActionTypes.Click)
        {
            platform.Input.MouseUp(active.Action.Mouse!);
        }
    }

    public void EndAll()
    {
        foreach (var cid in _active.Keys.ToArray())
            End(cid);
    }

    /// <summary>Runs an action once, as a tap; used for a gesture's click.</summary>
    public void Run(ButtonAction action)
    {
        switch (action.Type)
        {
            case ActionTypes.Hold or ActionTypes.Press:
                foreach (var key in action.Keys!)
                    platform.Input.Down(key);
                foreach (var key in action.Keys!.Reverse())
                    platform.Input.Up(key);
                break;
            case ActionTypes.System:
                platform.RunSystem(action.System!);
                break;
            case ActionTypes.Click:
                platform.Input.MouseDown(action.Mouse!);
                platform.Input.MouseUp(action.Mouse!);
                break;
        }
    }

    private void Repeat(ushort cid, ButtonAction action)
    {
        lock (gate)
        {
            // The timer can fire once more after End disposed it.
            if (_active.TryGetValue(cid, out var active) && ReferenceEquals(active.Action, action))
                Run(action);
        }
    }
}
