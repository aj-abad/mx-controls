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

    /// <summary>A gesture fired: control ID and "click", "up", "down", "left" or "right".</summary>
    public event Action<ushort, string>? GestureFired;
    public event Action<Exception>? Failed;

    /// <summary>Waveform played when a gesture direction fires, or null for none.</summary>
    public HapticWaveform? GestureHaptic { get; set; } = HapticWaveform.SubtleCollision;

    private ButtonEngine(MxDevice device, IPlatform platform)
    {
        _device = device;
        _platform = platform;
        _runner = new ActionRunner(platform, _gate);
    }

    /// <summary>The mouse's DPI, which gesture distances are measured with. Set it when the DPI changes.</summary>
    public int Dpi
    {
        set
        {
            lock (_gate)
                _tracker.Dpi = value;
        }
    }

    public static async Task<ButtonEngine> StartAsync(MxDevice device, IPlatform platform, IEnumerable<ButtonBinding> bindings)
    {
        var engine = new ButtonEngine(device, platform);
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
                if (_gestureButton == cid)
                    _gestureButton = null;
            }
            _bindings = next;
        }
        await WriteAsync(next);
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
            if (binding.IsGesture)
            {
                _gestureButton = cid;
                _tracker.Reset();
            }
            else
            {
                _runner.Begin(cid, binding.Action!);
            }
        }
        foreach (var cid in _held.Except(now))
        {
            if (!_bindings.TryGetValue(cid, out var binding))
                continue;
            if (binding.IsGesture && _gestureButton == cid)
            {
                if (_tracker.IsClick)
                {
                    _runner.Run(binding.Gesture!.Click);
                    GestureFired?.Invoke(cid, "click");
                }
                _gestureButton = null;
            }
            _runner.End(cid);
        }
        _held = [.. now];
    }

    private void OnMove(ushort cid, int dx, int dy)
    {
        if (_tracker.Add(dx, dy) is { } direction)
            Fire(cid, direction);
    }

    /// <summary>Runs a direction's action, held with the button.</summary>
    private void Fire(ushort cid, GestureDirection direction)
    {
        // The tap goes out first so it lands with the action rather than after it.
        if (GestureHaptic is { } waveform)
            _ = Task.Run(() => _device.Haptics.TapAsync(waveform));
        _runner.Begin(cid, _bindings[cid].Gesture![direction]);
        GestureFired?.Invoke(cid, direction.ToString().ToLowerInvariant());
    }

    public async ValueTask DisposeAsync()
    {
        _device.Channel.EventReceived -= OnEvent;
        lock (_gate)
        {
            try { _runner.EndAll(); } catch { }
            _held.Clear();
            _gestureButton = null;
        }
        foreach (var cid in _diverted.Keys)
        {
            var original = _original[cid];
            try { await _device.Controls.SetDivertAsync(cid, original.Diverted, original.RawXY); } catch { }
        }
        _diverted.Clear();
    }
}
