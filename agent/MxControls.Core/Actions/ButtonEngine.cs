using System.Diagnostics;
using MxControls.Features;
using MxControls.Hidpp;
using MxControls.Platform;

namespace MxControls.Actions;

/// <summary>
/// Runs button bindings in two layers: each control bound in either is diverted, gesture and rose controls with raw XY
/// so the pointer stays still while they're held. Presses, releases and movement become actions. A press runs the binding
/// of the layer that was on when it went down, to its release. <see cref="UpdateAsync"/> changes the bindings,
/// writing only the controls whose diversion changes. Disposing releases held keys and restores each control's
/// original diversion. Callers serialize the async methods.
/// </summary>
public sealed class ButtonEngine : IAsyncDisposable
{
    public static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>What the buttons with a known action of their own do, for a layer that leaves them unbound.</summary>
    private static readonly Dictionary<ushort, ButtonBinding> Own = new[] { MouseButtons.Back, MouseButtons.Forward, MouseButtons.Middle }
        .ToDictionary(id => ReprogControls.KnownButtons[id],
            id => new ButtonBinding(id, ButtonBinding.ActionMode, new ButtonAction(ActionTypes.Click, Mouse: id)));

    private readonly MxDevice _device;
    private readonly IPlatform _platform;
    private readonly LayerSwitch _layers = new();
    // The binding each held control went down with, so a layer change mid-press doesn't change what it lets go of.
    private readonly Dictionary<ushort, ButtonBinding> _pressed = [];
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
    private readonly RoseTracker _roseTracker = new();
    private OpenRose? _rose;
    // Made on the first rose, and kept so a timer firing late finds the rose it belongs to gone.
    private Timer? _roseTimer;
    // Per control, made on its first click, and kept so a timer firing late still finds what it belongs to.
    private readonly Dictionary<ushort, Clicks> _clicks = [];
    private int _multiClickMs = ClickCounter.DefaultWindowMs;

    /// <summary>
    /// The rose open on a held control: the options of the layer that's on, which change with it, and the main
    /// layer's under a second-layer rose that passes some through.
    /// </summary>
    private sealed class OpenRose(ushort cid, long pressedAt)
    {
        public ushort Cid { get; } = cid;
        public long PressedAt { get; } = pressedAt;
        public RoseMenu Own { get; set; } = null!;
        public RoseMenu? Main { get; set; }
        public RoseMenu Shown { get; set; } = null!;
        public bool SecondLayer { get; set; }
        public bool Visible { get; set; }
    }

    private sealed class Clicks(ClickCounter counter, Timer timer)
    {
        public ClickCounter Counter { get; } = counter;
        public Timer Timer { get; } = timer;
        /// <summary>The binding the clicks waiting were counted for.</summary>
        public ButtonBinding? Binding { get; set; }
    }

    /// <summary>
    /// A gesture's path changed or ran: control ID, the directions taken ("up", "down", "left" or "right", one per
    /// step), and whether it ran rather than just moved. A first direction with nothing beyond runs as it's reached.
    /// </summary>
    public event Action<ushort, IReadOnlyList<string>, bool>? GestureFired;

    /// <summary>
    /// A rose's highlight moved or an option ran: control ID, the option's index or null for the middle, and whether
    /// it ran. Raised inside the engine's lock: don't block.
    /// </summary>
    public event Action<ushort, int?, bool>? RoseChanged;

    /// <summary>
    /// A control kept raw XY for a rose open across a layer change, and its binding now doesn't want it; write it
    /// again with <see cref="SyncLayerAsync"/>. Raised inside the engine's lock: don't block.
    /// </summary>
    public event Action? DiversionStale;

    /// <summary>A click ran: control ID and 1, 2 or 3 for a single, double or triple click.</summary>
    public event Action<ushort, int>? ClickFired;

    /// <summary>
    /// The second layer turned on (true) or off. Controls bound as a gesture in one layer and not the other change
    /// their raw XY with it; <see cref="SyncLayerAsync"/> writes that. Raised inside the engine's lock: don't block.
    /// </summary>
    public event Action<bool>? LayerChanged;

    /// <summary>A <c>profile</c> action ran, with the profile's ID. Raised inside the engine's lock: don't block.</summary>
    public event Action<string>? ProfileRequested;

    /// <summary>
    /// A control went down in a layer that leaves it unbound, where nothing can stand in for what it does on its
    /// own, such as Smart Shift's switch to free spin. Raised inside the engine's lock: don't block.
    /// </summary>
    public event Action<ushort>? NativePressed;

    public event Action<Exception>? Failed;

    /// <summary>Waveform played when a gesture direction fires or a rose option is highlighted, or null for none.</summary>
    public HapticWaveform? GestureHaptic { get; set; } = HapticWaveform.SubtleCollision;

    /// <summary>How long a rose button is held before the rose shows, unless the pointer leaves the middle first.</summary>
    public const int RoseShowDelayMs = 140;

    /// <summary>A rose press shorter than this that never left the middle is a click.</summary>
    public const int RoseClickMs = 250;

    private ButtonEngine(MxDevice device, IPlatform platform, MacroLibrary? macros)
    {
        _device = device;
        _platform = platform;
        _runner = new ActionRunner(platform, _gate, macros, error => Failed?.Invoke(error));
        _macros = macros;
    }

    private readonly MacroLibrary? _macros;

    private static long Now => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

    /// <summary>The mouse's DPI, which gesture distances are measured with. Set it when the DPI changes.</summary>
    public int Dpi
    {
        set
        {
            lock (_gate)
                _tracker.Dpi = _roseTracker.Dpi = value;
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
                foreach (var clicks in _clicks.Values)
                    clicks.Counter.Window = value;
            }
        }
    }

    /// <summary>Whether the second layer is on.</summary>
    public bool LayerActive
    {
        get
        {
            lock (_gate)
                return _layers.Active;
        }
    }

    /// <param name="macros">The macros <c>macro</c> actions run, which can change while the engine runs.</param>
    public static async Task<ButtonEngine> StartAsync(
        MxDevice device, IPlatform platform, IEnumerable<ButtonBinding> bindings, MacroLibrary? macros = null,
        IEnumerable<ButtonBinding>? second = null)
    {
        var engine = new ButtonEngine(device, platform, macros);
        device.Channel.EventReceived += engine.OnEvent;
        try
        {
            if (device.Has(FeatureIds.AdjustableDpi))
                engine.Dpi = (await device.Pointer.GetDpiAsync()).Current;
            await engine.UpdateAsync(bindings, second);
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
    /// Runs these bindings from now on: <paramref name="bindings"/> as the main layer, and <paramref name="second"/>
    /// over it while the second layer is on. Only controls whose diversion changes are written, so a failure partway
    /// leaves the others working, and running the old bindings again puts back what was written.
    /// </summary>
    /// <param name="resetLayer">Go back to the main layer, as a profile that has just started running does.</param>
    public async Task UpdateAsync(IEnumerable<ButtonBinding> bindings, IEnumerable<ButtonBinding>? second = null, bool resetLayer = false)
    {
        var main = Map(bindings);
        var over = Map(second ?? []);

        lock (_gate)
        {
            var (oldMain, oldSecond) = (_layers.Main, _layers.Second);
            bool before = _layers.Active;
            _layers.Set(main, over);
            if (resetLayer)
                _layers.Reset();
            // A held control whose binding changes lets go of what it was doing.
            foreach (var (cid, pinned) in _pressed.ToArray())
            {
                if (Effective(cid) == pinned)
                    continue;
                _runner.End(cid);
                _pressed.Remove(cid);
                _layers.Release(cid);
                if (_gestureButton == cid)
                    _gestureButton = null;
                if (_rose?.Cid == cid)
                    CloseRose();
            }
            foreach (var cid in oldMain.Keys.Concat(oldSecond.Keys).Concat(_layers.Controls).Distinct())
            {
                if (oldMain.GetValueOrDefault(cid) != main.GetValueOrDefault(cid) || oldSecond.GetValueOrDefault(cid) != over.GetValueOrDefault(cid))
                    DropClicks(cid);
            }
            if (_layers.Active != before)
                LayerSwitched();
            else
                RefreshRose();
        }
        await WriteAsync();
    }

    private Dictionary<ushort, ButtonBinding> Map(IEnumerable<ButtonBinding> bindings)
    {
        var map = new Dictionary<ushort, ButtonBinding>();
        foreach (var binding in bindings)
        {
            if (binding.Problem(_platform) is { } problem)
                throw new ArgumentException(problem, nameof(bindings));
            if (binding.IsGesture && binding.Gesture is null)
                throw new ArgumentException($"{binding.Button}: resolve the gesture preset first.", nameof(bindings));
            map[ReprogControls.KnownButtons[binding.Button]] = binding;
        }
        return map;
    }

    /// <summary>What a diverted control runs now: its binding in the layer that's on, or what it does on its own.</summary>
    private ButtonBinding? Effective(ushort cid) => _layers[cid] ?? Own.GetValueOrDefault(cid);

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
            _pressed.Clear();
            _gestureButton = null;
            CloseRose();
            if (_layers.ReleaseAll())
                LayerChanged?.Invoke(_layers.Active);
        }
    }

    /// <summary>Diverts the bound controls again, after the mouse reconnected and forgot its diversions.</summary>
    public Task DivertAsync()
    {
        _diverted.Clear();
        return WriteAsync();
    }

    /// <summary>Writes the raw XY each control needs in the layer that's on now, after <see cref="LayerChanged"/>.</summary>
    public Task SyncLayerAsync() => WriteAsync();

    private async Task WriteAsync()
    {
        Dictionary<ushort, bool> wanted;
        lock (_gate)
            // A rose open across a layer change keeps its movement until it closes, whatever the new layer binds.
            wanted = _layers.Controls.ToDictionary(cid => cid, cid => (Effective(cid)?.NeedsRawXY ?? false) || _rose?.Cid == cid);

        foreach (var cid in _diverted.Keys.Where(cid => !wanted.ContainsKey(cid)).ToArray())
        {
            var original = _original[cid];
            await _device.Controls.SetDivertAsync(cid, original.Diverted, original.RawXY);
            _diverted.Remove(cid);
        }
        foreach (var (cid, rawXY) in wanted)
        {
            if (_diverted.TryGetValue(cid, out var written) && written == rawXY)
                continue;
            if (!_original.ContainsKey(cid))
                _original[cid] = await _device.Controls.GetReportingAsync(cid);
            await _device.Controls.SetDivertAsync(cid, divert: true, rawXY: rawXY);
            _diverted[cid] = rawXY;
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
                else if (_device.Controls.ParseRawXY(e) is { } move)
                {
                    if (_rose is not null)
                        OnRoseMove(move.Dx, move.Dy);
                    if (_gestureButton is { } button)
                        OnMove(button, move.Dx, move.Dy);
                }
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
            // Another app's diverted controls are reported too; only ours are ours to act on.
            if (!_layers.Controls.Contains(cid))
                continue;
            if (Effective(cid) is not { } binding)
            {
                NativePressed?.Invoke(cid);
                continue;
            }
            if (_clicks.TryGetValue(cid, out var clicks))
            {
                clicks.Timer.Change(Timeout.Infinite, Timeout.Infinite);
                if (clicks.Counter.Press(Now) is { } late)
                    RunClicks(cid, late);
                // Clicks counted in the other layer run as they were, and this press starts a count of its own.
                if (clicks.Binding is not null && clicks.Binding != binding)
                    InterruptClicks(cid);
            }
            _pressed[cid] = binding;
            if (binding.IsRose)
            {
                OpenRoseOn(cid);
            }
            else if (binding.IsGesture)
            {
                _gestureButton = cid;
                _tracker.Reset(binding.Gesture!);
            }
            else if (binding.Clicks.Most == 1)
            {
                // Nothing waits for a second click, so the action lasts as long as the press, as it always has.
                Begin(cid, binding.Action!);
            }
        }
        foreach (var cid in _held.Except(now))
        {
            if (!_pressed.Remove(cid, out var binding))
                continue;
            if (binding.IsRose)
            {
                if (_rose?.Cid == cid)
                    ReleaseRose(binding);
                End(cid);
                continue;
            }
            if (binding.IsGesture && _gestureButton == cid)
            {
                // A path runs where it ends, as a tap; a step with no action is a junction, and backing out cancels.
                if (_tracker.Step is { } step)
                {
                    // A repeating step ran as it was reached.
                    if (_tracker.ReleaseStep is not null)
                    {
                        RunOnce(step.Action);
                        GestureFired?.Invoke(cid, Names(_tracker.Path), true);
                    }
                }
                else if (_tracker.IsClick)
                    Click(cid, binding);
                else if (_tracker.Direction is null)
                    InterruptClicks(cid);
                _gestureButton = null;
            }
            else if (!binding.IsGesture && binding.Clicks.Most > 1)
            {
                Click(cid, binding);
            }
            End(cid);
            if (!binding.IsGesture && binding.Clicks.Most == 1)
                ClickFired?.Invoke(cid, 1);
        }
        _held = [.. now];
    }

    /// <summary>Starts an action that lasts as long as the press; layers and profiles are the engine's own.</summary>
    private void Begin(ushort cid, ButtonAction action)
    {
        _runner.End(cid);
        if (action.HoldsLayer)
        {
            if (_layers.Hold(cid))
                LayerSwitched();
        }
        else if (action.Type is ActionTypes.Layer or ActionTypes.Profile)
            RunOnce(action);
        else
            _runner.Begin(cid, action);
    }

    private void End(ushort cid)
    {
        _runner.End(cid);
        if (_layers.Release(cid))
            LayerSwitched();
    }

    /// <summary>Runs an action once, as a tap. Holding a layer for no time at all does nothing.</summary>
    private void RunOnce(ButtonAction action)
    {
        switch (action.Type)
        {
            case ActionTypes.Layer when action.Layer == LayerModes.Toggle:
                if (_layers.Toggle())
                    LayerSwitched();
                break;
            case ActionTypes.Layer:
                break;
            case ActionTypes.Profile:
                ProfileRequested?.Invoke(action.Profile!);
                break;
            default:
                _runner.Run(action);
                break;
        }
    }

    /// <summary>A press came up as a click: runs the count it completes, or waits for the next press.</summary>
    private void Click(ushort cid, ButtonBinding binding)
    {
        if (!_clicks.TryGetValue(cid, out var clicks))
        {
            clicks = new Clicks(new ClickCounter { Window = _multiClickMs }, new Timer(_ => OnClickWindow(cid)));
            _clicks[cid] = clicks;
        }
        clicks.Binding = binding;
        if (clicks.Counter.Release(Now, binding.Clicks.Most) is { } count)
            RunClicks(cid, count);
        else
            WaitForClick(clicks);
    }

    private static void WaitForClick(Clicks clicks)
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
        if (!_clicks.TryGetValue(cid, out var clicks) || clicks.Binding is not { } binding)
            return;
        RunOnce(binding.Clicks[count]);
        ClickFired?.Invoke(cid, count);
    }

    private void DropClicks(ushort cid)
    {
        if (!_clicks.TryGetValue(cid, out var clicks))
            return;
        clicks.Timer.Change(Timeout.Infinite, Timeout.Infinite);
        clicks.Counter.Clear();
        clicks.Binding = null;
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
        bool ran = _tracker.Change is PathChange.Ran;
        if (ran)
            RunOnce(_tracker.Step!.Action);
        GestureFired?.Invoke(cid, Names(_tracker.Path), ran);
    }

    /// <summary>
    /// Runs a direction's action: held with the button, or tapped when it repeats, since a repeating direction
    /// fires again with movement rather than with time.
    /// </summary>
    private void Fire(ushort cid, GestureDirection direction)
    {
        if (!_pressed.TryGetValue(cid, out var binding) || binding.Gesture is not { } gesture)
            return;
        if (GestureHaptic is { } waveform)
            Tap(waveform);
        InterruptClicks(cid);
        var action = gesture[direction].Action;
        End(cid);
        if (action.Repeat)
            RunOnce(action);
        else
            Begin(cid, action);
        GestureFired?.Invoke(cid, [Name(direction)], true);
    }

    private void LayerSwitched()
    {
        LayerChanged?.Invoke(_layers.Active);
        RefreshRose();
    }

    /// <summary>
    /// The rose a control shows in the layer that's on: the second layer's over the main one's, or the main one's
    /// where the second layer leaves the control alone or binds it to something else. Null where neither is a rose.
    /// </summary>
    private (RoseMenu Own, RoseMenu? Main, bool Second)? RoseFor(ushort cid)
    {
        var main = _layers.Main.GetValueOrDefault(cid) is { IsRose: true } m ? m.Rose : null;
        if (_layers.Active && _layers.Second.GetValueOrDefault(cid) is { IsRose: true, Rose: { } second })
            return (second, main, true);
        return main is null ? null : (main, null, false);
    }

    private void OpenRoseOn(ushort cid)
    {
        if (_rose is not null)
            CloseRose();
        if (RoseFor(cid) is not { } menu)
            return;
        var rose = new OpenRose(cid, Now);
        _rose = rose;
        Choose(rose, menu);
        _roseTracker.Reset(rose.Shown.Items.Count);
        _roseTimer ??= new Timer(_ => OnRoseTimer());
        _roseTimer.Change(RoseShowDelayMs, Timeout.Infinite);
    }

    private static void Choose(OpenRose rose, (RoseMenu Own, RoseMenu? Main, bool Second) menu)
    {
        rose.Own = menu.Own;
        rose.Main = menu.Main;
        rose.SecondLayer = menu.Second;
        // A main-layer rose has nothing beneath it, so anything it passes through is empty.
        rose.Shown = menu.Own.Over(menu.Main);
    }

    private void OnRoseTimer()
    {
        lock (_gate)
        {
            try
            {
                if (_rose is { Visible: false } rose && Now - rose.PressedAt >= RoseShowDelayMs - 5)
                    ShowRose(rose);
            }
            catch (Exception error)
            {
                Failed?.Invoke(error);
            }
        }
    }

    private void OnRoseMove(int dx, int dy)
    {
        var rose = _rose!;
        bool changed = _roseTracker.Add(dx, dy);
        if (changed)
        {
            // The hand feels each option it reaches; an empty one and the middle are fainter.
            if (GestureHaptic is { } reached)
                Tap(_roseTracker.Selected is { } index && rose.Shown.Items[index].Action.IsSet ? reached : HapticWaveform.WhisperCollision);
            RoseChanged?.Invoke(rose.Cid, _roseTracker.Selected, false);
        }
        if (rose.Visible || _roseTracker.LeftCenter)
            ShowRose(rose);
    }

    /// <summary>The layer changed with a rose open: it shows the new layer's options, if it has a rose of its own.</summary>
    private void RefreshRose()
    {
        if (_rose is not { } rose || RoseFor(rose.Cid) is not { } menu)
            return;
        if (menu.Own == rose.Own && menu.Main == rose.Main && menu.Second == rose.SecondLayer)
            return;
        var before = _roseTracker.Selected;
        Choose(rose, menu);
        _roseTracker.Count = rose.Shown.Items.Count;
        if (GestureHaptic is not null)
            Tap(HapticWaveform.DampStateChange);
        if (_roseTracker.Selected != before)
            RoseChanged?.Invoke(rose.Cid, _roseTracker.Selected, false);
        if (rose.Visible)
            ShowRose(rose);
    }

    private void ShowRose(OpenRose rose)
    {
        rose.Visible = true;
        if (_platform.RoseOverlay is not { } overlay)
            return;
        var slots = rose.Shown.Items.Select((item, i) => new RoseSlot(
            Label(item), item.IconPath, !item.Action.IsSet, rose.SecondLayer && rose.Own.Items[i].PassThrough)).ToArray();
        overlay.Show(new RoseView(slots, _roseTracker.Selected, _roseTracker.X, _roseTracker.Y, RoseTracker.CenterRatio, rose.SecondLayer));
    }

    /// <summary>The rose's button came up: runs the highlighted option, or the quick press, or nothing from the middle.</summary>
    private void ReleaseRose(ButtonBinding binding)
    {
        var rose = _rose!;
        var selected = _roseTracker.Selected;
        bool click = selected is null && !_roseTracker.LeftCenter && Now - rose.PressedAt < RoseClickMs;
        var action = selected is { } index ? rose.Shown.Items[index].Action : null;
        CloseRose();
        if (action is { IsSet: true })
        {
            RunOnce(action);
            RoseChanged?.Invoke(rose.Cid, selected, true);
        }
        else if (click && binding.Clicks.Single.IsSet)
            Click(rose.Cid, binding);
    }

    private void CloseRose()
    {
        if (_rose is not { } rose)
            return;
        _rose = null;
        _roseTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        if (rose.Visible)
            _platform.RoseOverlay?.Hide();
        // _diverted belongs to the writer, outside the lock; a sync with nothing to change writes nothing.
        if (!(Effective(rose.Cid)?.NeedsRawXY ?? false))
            DiversionStale?.Invoke();
    }

    /// <summary>What a rose option says: its label, or a short name for its action.</summary>
    private string Label(RoseItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Label))
            return item.Label.Trim();
        var action = item.Action;
        return action.Type switch
        {
            ActionTypes.None => "",
            ActionTypes.Press or ActionTypes.Hold => string.Join("+", action.Keys?.Select(KeyLabel) ?? []),
            ActionTypes.Macro => _macros?.Find(action.Macro ?? "")?.Name ?? "Macro",
            ActionTypes.System => action.System switch
            {
                SystemActions.TaskView => OperatingSystem.IsMacOS() ? "Mission Control" : "Task View",
                SystemActions.ShowDesktop => "Desktop",
                SystemActions.ZoomIn => "Zoom in",
                SystemActions.ZoomOut => "Zoom out",
                { } name => Words(name),
                _ => "System",
            },
            ActionTypes.Open or ActionTypes.AppleShortcut when !string.IsNullOrWhiteSpace(action.Name) => action.Name,
            ActionTypes.Open => Path.GetFileName(action.Open?.TrimEnd('/', '\\')) is { Length: > 0 } file ? file : "Open",
            ActionTypes.AppleShortcut => "Shortcut",
            ActionTypes.Click => action.Mouse switch
            {
                MouseButtons.Back => "Back",
                MouseButtons.Forward => "Forward",
                _ => "Middle click",
            },
            ActionTypes.Layer => "Layer 2",
            ActionTypes.Profile => "Profile",
            _ => action.Type,
        };
    }

    /// <summary>"lockScreen" as "Lock screen".</summary>
    private static string Words(string name)
    {
        var text = new System.Text.StringBuilder(name.Length + 4);
        foreach (char c in name)
            text.Append(text.Length == 0 ? char.ToUpperInvariant(c) : char.IsUpper(c) ? $" {char.ToLowerInvariant(c)}" : c);
        return text.ToString();
    }

    private static string KeyLabel(string code) => code switch
    {
        "ControlLeft" or "ControlRight" => "Ctrl",
        "ShiftLeft" or "ShiftRight" => "Shift",
        "AltLeft" or "AltRight" => "Alt",
        "MetaLeft" or "MetaRight" => OperatingSystem.IsMacOS() ? "Cmd" : "Win",
        _ when code.StartsWith("Key", StringComparison.Ordinal) && code.Length == 4 => code[3..],
        _ when code.StartsWith("Digit", StringComparison.Ordinal) => code[5..],
        _ => code,
    };

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
            _pressed.Clear();
            _gestureButton = null;
            CloseRose();
            _roseTimer?.Dispose();
            foreach (var clicks in _clicks.Values)
                clicks.Timer.Dispose();
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
