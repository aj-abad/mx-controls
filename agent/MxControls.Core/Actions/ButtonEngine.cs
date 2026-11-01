using System.Diagnostics;
using MxControls.Features;
using MxControls.Hidpp;
using MxControls.Platform;

namespace MxControls.Actions;

/// <summary>
/// Runs button bindings: each bound control is diverted, gesture controls with raw XY so the pointer stays still
/// while they're held. Presses, releases and movement become actions. <see cref="UpdateAsync"/> changes the
/// bindings, writing only the controls whose diversion changes. Disposing releases held keys and restores each
/// control's original diversion. Callers serialize the async methods.
/// </summary>
public sealed class ButtonEngine : IAsyncDisposable
{
    public static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(200);

    private readonly MxDevice _device;
    private readonly IPlatform _platform;
    private Dictionary<ushort, ButtonBinding> _bindings = [];
    // How each control was before the first diversion, kept after it's restored so its owner stays known.
    private readonly Dictionary<ushort, ControlReporting> _original = [];
    // What the mouse has been told: each diverted control and whether with raw XY.
    private readonly Dictionary<ushort, bool> _diverted = [];
    private readonly object _gate = new();
    private readonly ActionRunner _runner;
    private readonly GestureTracker _tracker = new();
    private HashSet<ushort> _held = [];
    private ushort? _gestureButton;
    private int _hapticSending;
    // Per control, made on its first click, and kept so a timer firing late still finds what it belongs to.
    private readonly Dictionary<ushort, (ClickCounter Counter, Timer Timer)> _clicks = [];
    private int _multiClickMs = ClickCounter.DefaultWindowMs;

    /// <summary>
    /// A gesture's path changed or ran: control ID, the directions taken ("up", "down", "left" or "right", one per
    /// step), and whether it ran rather than just moved. A first direction with nothing beyond runs as it's reached.
    /// </summary>
    public event Action<ushort, IReadOnlyList<string>, bool>? GestureFired;

    /// <summary>A click ran: control ID and 1, 2 or 3 for a single, double or triple click.</summary>
    public event Action<ushort, int>? ClickFired;
    public event Action<Exception>? Failed;

    /// <summary>Waveform played when a gesture direction fires, or null for none.</summary>
    public HapticWaveform? GestureHaptic { get; set; } = HapticWaveform.SubtleCollision;

    private ButtonEngine(MxDevice device, IPlatform platform, MacroLibrary? macros)
    {
        _device = device;
        _platform = platform;
        _runner = new ActionRunner(platform, _gate, macros, error => Failed?.Invoke(error));
    }

    private static long Now => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

    /// <summary>The mouse's DPI, which gesture distances are measured with. Set it when the DPI changes.</summary>
    public int Dpi
    {
        set
        {
            lock (_gate)
                _tracker.Dpi = value;
        }
    }

    /// <summary>How long a click waits for another, on controls with a double or triple click set.</summary>
    public int MultiClickMs
    {
        set
        {
            lock (_gate)
            {
                _multiClickMs = value;
                foreach (var (counter, _) in _clicks.Values)
                    counter.Window = value;
            }
        }
    }

    /// <param name="macros">The macros <c>macro</c> actions run, which can change while the engine runs.</param>
    public static async Task<ButtonEngine> StartAsync(
        MxDevice device, IPlatform platform, IEnumerable<ButtonBinding> bindings, MacroLibrary? macros = null)
    {
        var engine = new ButtonEngine(device, platform, macros);
        device.Channel.EventReceived += engine.OnEvent;
        try
        {
            if (device.Has(FeatureIds.AdjustableDpi))
                engine.Dpi = (await device.Pointer.GetDpiAsync()).Current;
            await engine.UpdateAsync(bindings);
            return engine;
        }
        catch
        {
            await engine.DisposeAsync();
            throw;
        }
    }

    /// <summary>Whether another app, most likely Logi Options+, had diverted the control before we did; null if we never have.</summary>
    public bool? DivertedBefore(ushort cid) => _original.TryGetValue(cid, out var original) ? original.Diverted : null;

    /// <summary>
    /// Runs these bindings from now on. Only controls whose diversion changes are written, so a failure partway
    /// leaves the others working, and running the old bindings again puts back what was written.
    /// </summary>
    public async Task UpdateAsync(IEnumerable<ButtonBinding> bindings)
    {
        var next = new Dictionary<ushort, ButtonBinding>();
        foreach (var binding in bindings)
        {
            if (binding.Problem(_platform) is { } problem)
                throw new ArgumentException(problem, nameof(bindings));
            if (binding.IsGesture && binding.Gesture is null)
                throw new ArgumentException($"{binding.Button}: resolve the gesture preset first.", nameof(bindings));
            next[ReprogControls.KnownButtons[binding.Button]] = binding;
        }

        lock (_gate)
        {
            // A held control whose binding changes lets go of what it was doing.
            foreach (var (cid, binding) in _bindings)
            {
                if (next.TryGetValue(cid, out var kept) && kept == binding)
                    continue;
                _runner.End(cid);
                DropClicks(cid);
                if (_gestureButton == cid)
                    _gestureButton = null;
            }
            _bindings = next;
        }
        await WriteAsync(next);
    }

    /// <summary>
    /// Lets go of every press without running anything, after the mouse reconnected: whatever was held when the link
    /// dropped never sends its release, so its keys would stay down and its next click would count as a second.
    /// </summary>
    public void ReleaseAll()
    {
        lock (_gate)
        {
            _runner.EndAll();
            // Clicks already released keep waiting; only a press cut off partway has nothing left to count.
            foreach (var cid in _held)
                DropClicks(cid);
            _held = [];
            _gestureButton = null;
        }
    }

    /// <summary>Diverts the bound controls again, after the mouse reconnected and forgot its diversions.</summary>
    public Task DivertAsync()
    {
        _diverted.Clear();
        return WriteAsync(_bindings);
    }

    private async Task WriteAsync(IReadOnlyDictionary<ushort, ButtonBinding> bindings)
    {
        foreach (var cid in _diverted.Keys.Where(cid => !bindings.ContainsKey(cid)).ToArray())
        {
            var original = _original[cid];
            await _device.Controls.SetDivertAsync(cid, original.Diverted, original.RawXY);
            _diverted.Remove(cid);
        }
        foreach (var (cid, binding) in bindings)
        {
            if (_diverted.TryGetValue(cid, out var rawXY) && rawXY == binding.IsGesture)
                continue;
            if (!_original.ContainsKey(cid))
                _original[cid] = await _device.Controls.GetReportingAsync(cid);
            await _device.Controls.SetDivertAsync(cid, divert: true, rawXY: binding.IsGesture);
            _diverted[cid] = binding.IsGesture;
        }
    }

    private void OnEvent(HidppEvent e)
    {
        lock (_gate)
        {
            try
            {
                if (_device.Controls.ParseHeld(e) is { } held)
                    OnHeld(held);
                else if (_device.Controls.ParseRawXY(e) is { } move && _gestureButton is { } button)
                    OnMove(button, move.Dx, move.Dy);
            }
            catch (Exception error)
            {
                Failed?.Invoke(error);
            }
        }
    }

    private void OnHeld(IReadOnlySet<ushort> now)
    {
        foreach (var cid in now.Except(_held))
        {
            if (!_bindings.TryGetValue(cid, out var binding))
                continue;
            if (_clicks.TryGetValue(cid, out var clicks))
            {
                clicks.Timer.Change(Timeout.Infinite, Timeout.Infinite);
                if (clicks.Counter.Press(Now) is { } late)
                    RunClicks(cid, late);
            }
            if (binding.IsGesture)
            {
                _gestureButton = cid;
                _tracker.Reset(binding.Gesture!);
            }
            else if (binding.Clicks.Most == 1)
            {
                // Nothing waits for a second click, so the action lasts as long as the press, as it always has.
                _runner.Begin(cid, binding.Action!);
            }
        }
        foreach (var cid in _held.Except(now))
        {
            if (!_bindings.TryGetValue(cid, out var binding))
                continue;
            if (binding.IsGesture && _gestureButton == cid)
            {
                // A path runs where it ends, as a tap; a step with no action is a junction, and backing out cancels.
                if (_tracker.Step is { } step)
                {
                    _runner.Run(step.Action);
                    GestureFired?.Invoke(cid, Names(_tracker.Path), true);
                }
                else if (_tracker.IsClick)
                    Click(cid, binding.Clicks);
                else if (_tracker.Direction is null)
                    InterruptClicks(cid);
                _gestureButton = null;
            }
            else if (!binding.IsGesture && binding.Clicks.Most > 1)
            {
                Click(cid, binding.Clicks);
            }
            _runner.End(cid);
            if (!binding.IsGesture && binding.Clicks.Most == 1)
                ClickFired?.Invoke(cid, 1);
        }
        _held = [.. now];
    }

    /// <summary>A press came up as a click: runs the count it completes, or waits for the next press.</summary>
    private void Click(ushort cid, ClickActions actions)
    {
        if (!_clicks.TryGetValue(cid, out var clicks))
        {
            clicks = (new ClickCounter { Window = _multiClickMs }, new Timer(_ => OnClickWindow(cid)));
            _clicks[cid] = clicks;
        }
        if (clicks.Counter.Release(Now, actions.Most) is { } count)
            RunClicks(cid, count);
        else
            WaitForClick(clicks);
    }

    private void WaitForClick((ClickCounter Counter, Timer Timer) clicks)
    {
        if (clicks.Counter.Deadline is { } deadline)
            clicks.Timer.Change(Math.Max(1, deadline - Now), Timeout.Infinite);
    }

    private void OnClickWindow(ushort cid)
    {
        lock (_gate)
        {
            try
            {
                if (!_clicks.TryGetValue(cid, out var clicks))
                    return;
                if (clicks.Counter.Expire(Now) is { } count)
                    RunClicks(cid, count);
                else
                    // The timer can wake a little before the clock reads the deadline.
                    WaitForClick(clicks);
            }
            catch (Exception error)
            {
                Failed?.Invoke(error);
            }
        }
    }

    /// <summary>The press became something other than a click, so clicks waiting for it run first.</summary>
    private void InterruptClicks(ushort cid)
    {
        if (_clicks.TryGetValue(cid, out var clicks) && clicks.Counter.Interrupt() is { } count)
            RunClicks(cid, count);
    }

    private void RunClicks(ushort cid, int count)
    {
        if (!_bindings.TryGetValue(cid, out var binding))
            return;
        _runner.Run(binding.Clicks[count]);
        ClickFired?.Invoke(cid, count);
    }

    private void DropClicks(ushort cid)
    {
        if (!_clicks.TryGetValue(cid, out var clicks))
            return;
        clicks.Timer.Change(Timeout.Infinite, Timeout.Infinite);
        clicks.Counter.Clear();
    }

    private void OnMove(ushort cid, int dx, int dy)
    {
        if (_tracker.Add(dx, dy, Now) is { } direction)
        {
            Fire(cid, direction);
            return;
        }
        if (_tracker.Change is PathChange.None)
            return;
        // Reaching a step that turns feels different, so the hand knows a turn is there without looking.
        if (GestureHaptic is { } reached)
            Tap(_tracker.Change switch
            {
                PathChange.ReachedBranch => HapticWaveform.DampStateChange,
                PathChange.BackedOff => HapticWaveform.WhisperCollision,
                _ => reached,
            });
        InterruptClicks(cid);
        GestureFired?.Invoke(cid, Names(_tracker.Path), false);
    }

    /// <summary>
    /// Runs a direction's action: held with the button, or tapped when it repeats, since a repeating direction
    /// fires again with movement rather than with time.
    /// </summary>
    private void Fire(ushort cid, GestureDirection direction)
    {
        if (GestureHaptic is { } waveform)
            Tap(waveform);
        InterruptClicks(cid);
        var action = _bindings[cid].Gesture![direction].Action;
        _runner.End(cid);
        if (action.Repeat)
            _runner.Run(action);
        else
            _runner.Begin(cid, action);
        GestureFired?.Invoke(cid, [Name(direction)], true);
    }

    /// <summary>
    /// Plays a haptic tap. It goes out before the action so it lands with it rather than after. Repeats and steps
    /// can come faster than the radio carries taps, so one is skipped while the last is still going out.
    /// </summary>
    private void Tap(HapticWaveform waveform)
    {
        if (Interlocked.Exchange(ref _hapticSending, 1) != 0)
            return;
        _ = Task.Run(async () =>
        {
            try { await _device.Haptics.TapAsync(waveform); }
            finally { Volatile.Write(ref _hapticSending, 0); }
        });
    }

    private static string Name(GestureDirection direction) => direction.ToString().ToLowerInvariant();

    private static string[] Names(IReadOnlyList<GestureDirection> path) => [.. path.Select(Name)];

    public async ValueTask DisposeAsync()
    {
        _device.Channel.EventReceived -= OnEvent;
        lock (_gate)
        {
            try { _runner.EndAll(); } catch { }
            _held.Clear();
            _gestureButton = null;
            foreach (var (_, timer) in _clicks.Values)
                timer.Dispose();
            _clicks.Clear();
        }
        foreach (var cid in _diverted.Keys)
        {
            var original = _original[cid];
            try { await _device.Controls.SetDivertAsync(cid, original.Diverted, original.RawXY); } catch { }
        }
        _diverted.Clear();
    }
}
