using MxControls.Platform;

namespace MxControls.Actions;

/// <summary>
/// Starts and ends actions for held controls. A <c>hold</c> action keeps its keys down until <see cref="End"/>,
/// and a <c>click</c> its mouse button, so apps see the press and release when the real button makes them;
/// a repeating action fires again after <see cref="ButtonEngine.RepeatDelay"/> and every
/// <see cref="ButtonEngine.RepeatInterval"/> until then. Callers serialize calls.
/// </summary>
/// <remarks>
/// A macro without waits runs at once, like a press. One that waits runs in the background, one macro at a
/// time, so two never interleave their keys; a repeat that comes while one is still running is skipped.
/// </remarks>
internal sealed class ActionRunner(IPlatform platform, object gate, MacroLibrary? macros = null, Action<Exception>? failed = null)
{
    private readonly Dictionary<ushort, (ButtonAction Action, Timer? Repeat)> _active = [];
    private readonly SemaphoreSlim _macroTurn = new(1, 1);

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
            case ActionTypes.Press or ActionTypes.System or ActionTypes.Macro:
                Run(action);
                break;
            case ActionTypes.Open or ActionTypes.AppleShortcut:
                Run(action);
                return;
            default:
                return;
        }
        Timer? repeat = action.Repeat && action.Type is ActionTypes.Press or ActionTypes.System or ActionTypes.Macro
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
    public void Run(ButtonAction action) => Run(action, repeating: false);

    private void Run(ButtonAction action, bool repeating)
    {
        switch (action.Type)
        {
            case ActionTypes.Macro:
                if (macros?.Find(action.Macro!) is { } macro)
                    RunMacro(macro, repeating);
                break;
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
            // Starting a process takes long enough to hold up the mouse's next event, so it goes off to the side.
            case ActionTypes.Open when platform.Apps is { } apps:
                OffThread(() => apps.Open(action.Open!));
                break;
            case ActionTypes.AppleShortcut when platform.Shortcuts is { } shortcuts:
                OffThread(() => shortcuts.Run(action.Shortcut!));
                break;
        }
    }

    private void OffThread(Action work) => _ = Task.Run(() =>
    {
        try { work(); }
        catch (Exception error) { failed?.Invoke(error); }
    });

    private void Repeat(ushort cid, ButtonAction action)
    {
        lock (gate)
        {
            // The timer can fire once more after End disposed it.
            if (_active.TryGetValue(cid, out var active) && ReferenceEquals(active.Action, action))
                Run(action, repeating: true);
        }
    }

    private void RunMacro(Macro macro, bool repeating)
    {
        if (!macro.Waits)
        {
            foreach (var step in macro.Steps)
                RunStep(step);
            return;
        }
        if (repeating && _macroTurn.CurrentCount == 0)
            return;
        _ = Task.Run(async () =>
        {
            await _macroTurn.WaitAsync();
            try
            {
                foreach (var step in macro.Steps)
                {
                    if (step.Type == MacroStepTypes.Wait)
                        await Task.Delay(step.Ms);
                    else
                        RunStep(step);
                }
            }
            catch (Exception error)
            {
                failed?.Invoke(error);
            }
            finally
            {
                _macroTurn.Release();
            }
        });
    }

    private void RunStep(MacroStep step)
    {
        switch (step.Type)
        {
            case MacroStepTypes.Keys:
                foreach (var key in step.Keys!)
                    platform.Input.Down(key);
                foreach (var key in step.Keys!.Reverse())
                    platform.Input.Up(key);
                break;
            case MacroStepTypes.Text:
                platform.Input.Type(step.Text!);
                break;
            case MacroStepTypes.Click:
                platform.Input.MouseDown(step.Mouse!);
                platform.Input.MouseUp(step.Mouse!);
                break;
        }
    }
}
