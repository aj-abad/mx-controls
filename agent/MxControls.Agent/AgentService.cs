using System.Diagnostics;
using System.Text.Json;
using MxControls.Actions;
using MxControls.Features;
using MxControls.Hidpp;
using MxControls.Input;
using MxControls.Platform;

namespace MxControls.Agent;

/// <summary>
/// Owns the mouse: connects when it appears, runs the active profile, reapplies it when the mouse reconnects,
/// and turns client requests into device calls. Every device call waits its turn in one queue, where a newer
/// change to a setting replaces one still waiting. A change reaches the mouse before it's saved, and one the
/// mouse refuses is undone, so the settings on disk are always ones that ran.
/// </summary>
public sealed class AgentService(IPlatform platform, SettingsStore store) : IAsyncDisposable
{
    public const string LoginItemName = "MX Studio Agent";
    // The app was called MX Controls; its task would start the agent a second time.
    private const string FormerLoginItemName = "MX Controls Agent";

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan BatteryInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan KeyCaptureTimeout = TimeSpan.FromSeconds(15);

    private readonly RequestQueue _queue = new();
    private AgentSettings _settings = store.Load();
    private MxDevice? _device;
    private ButtonEngine? _engine;
    private ThumbWheelEngine? _thumb;
    private DeviceState? _deviceState;
    // The controls as read at connect, so button states can be rebuilt without asking the mouse again.
    private IReadOnlyList<(string Id, string Name, ushort Cid, bool Diverted)> _controls = [];
    private bool _thumbDivertedAtConnect;
    private long _revision;
    private HashSet<ushort> _held = [];
    private bool _optionsPlus;
    private string? _error;
    private volatile bool _lost;
    private readonly SemaphoreSlim _pollNow = new(0);
    private IDisposable? _arrivals;
    private DateTime _batteryReadAt;
    private KeyRecorder? _recorder;
    // What `macro` actions run; both engines read it, and it changes with the settings.
    private readonly MacroLibrary _macros = new();
    // The program in front, written by the platform's watcher thread.
    private volatile string? _foreground;
    private IDisposable? _foregroundWatch;
    // The profile the engines run, so starting another one goes back to its main layer.
    private string? _appliedProfile;
    private string? _publishedProfile;

    /// <summary>An event for every client: its name and payload.</summary>
    public event Action<string, object>? Published;

    /// <summary>A client asked the agent to exit.</summary>
    public event Action? ShutdownRequested;

    public AgentState State
    {
        get
        {
            var settings = _settings;
            var running = settings.Running(_foreground);
            return new(_device is not null && _deviceState is not null, _deviceState, running.AllBindings, running.Wheel,
                settings.AllProfiles.Select(Summary).ToArray(), running.Id,
                settings.StartAtLogin, _optionsPlus, _error, Interlocked.Read(ref _revision), settings.MultiClickMs,
                settings.AllPresets, PresetLibrary.BuiltIn, PresetUses(settings), settings.Macros ?? [], MacroUses(settings),
                settings.Active.Id, _engine?.LayerActive ?? false, platform.Programs is not null, AgentBuild.Hash);
        }
    }

    private static ProfileSummary Summary(Profile p) =>
        new(p.Id, p.Name, p.Icon, p.AllPrograms, p.AllBindings, p.Wheel, new LayerSummary(p.SecondLayer.AllBindings, p.SecondLayer.ThumbWheel));

    private static IReadOnlyList<PresetUse> PresetUses(AgentSettings settings) =>
        settings.AllProfiles.SelectMany(p =>
            p.AllBindings.Select(b => (Binding: b, Layer: false)).Concat(p.SecondLayer.AllBindings.Select(b => (Binding: b, Layer: true)))
                .Where(x => x.Binding is { IsGesture: true, Preset: not null })
                .Select(x => new PresetUse(x.Binding.Preset!, p.Id, x.Binding.Button, x.Layer))).ToArray();

    public async Task RunAsync(CancellationToken stop)
    {
        ApplyLoginItem();
        try
        {
            _foregroundWatch = platform.Programs?.WatchForeground(OnForeground);
        }
        catch (Exception error)
        {
            Log.Write($"Watching which program is in front failed; profiles won't follow programs: {error.Message}");
        }
        try
        {
            _arrivals = platform.Hid.WatchArrivals(MxDevice.LogitechVendorId, PollNow);
        }
        catch (Exception error)
        {
            Log.Write($"Watching for the mouse failed; it'll be found by polling: {error.Message}");
        }
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await TickAsync();
            }
            catch (Exception error)
            {
                Log.Write($"Agent loop: {error.Message}");
            }
            try { await _pollNow.WaitAsync(PollInterval, stop); } catch (OperationCanceledException) { }
        }
    }

    /// <summary>Points the login item at this executable, so it follows the app when it moves or updates.</summary>
    private void ApplyLoginItem()
    {
        try
        {
            platform.LoginItem.Disable(FormerLoginItemName);
            if (_settings.StartAtLogin && Environment.ProcessPath is { } executable)
                platform.LoginItem.Enable(LoginItemName, executable);
            else if (!_settings.StartAtLogin)
                platform.LoginItem.Disable(LoginItemName);
        }
        catch (Exception error)
        {
            Log.Write($"Updating the login item failed: {error.Message}");
        }
    }

    private async Task TickAsync()
    {
        bool changed = false;
        bool optionsPlus = platform.IsOptionsPlusRunning();
        if (optionsPlus != _optionsPlus)
        {
            _optionsPlus = optionsPlus;
            changed = true;
        }

        await _queue.RunAsync(async () =>
        {
            if (_lost)
                changed |= await DropDeviceAsync("The mouse disconnected.");
            if (_device is null)
                changed |= await TryConnectAsync();
            else if (DateTime.UtcNow - _batteryReadAt > BatteryInterval)
                changed |= await RefreshBatteryAsync();
        });
        if (changed)
            PublishState();
    }

    /// <summary>Ends the wait for the next poll, when the mouse has just gone or come.</summary>
    private void PollNow()
    {
        if (_pollNow.CurrentCount == 0)
            _pollNow.Release();
    }

    /// <summary>
    /// Puts the wheel the way it's set, ahead of everything else when the mouse comes back, since the mouse forgets
    /// when it powers off. Does nothing until a direction has been chosen.
    /// </summary>
    /// <param name="since">When the mouse was found or woke, for the time in the log.</param>
    private async Task ApplyScrollDirectionAsync(MxDevice mx, string when, Stopwatch since)
    {
        if (_settings.ScrollInverted is not { } inverted)
            return;
        try
        {
            if (!await mx.FindFeatureAsync(FeatureIds.HiResWheel))
                return;
            Log.Write(await mx.Wheels.SetHiResInvertedAsync(inverted)
                ? $"The mouse had lost its scroll direction {when}; set again after {since.ElapsedMilliseconds} ms"
                : $"The mouse kept its scroll direction {when}; checked after {since.ElapsedMilliseconds} ms");
        }
        catch (Exception error)
        {
            Log.Write($"Setting the scroll direction failed {when}: {error.Message}");
        }
    }

    private async Task<bool> TryConnectAsync()
    {
        var found = MxDevice.FindBluetooth(platform.Hid).FirstOrDefault();
        if (found is null)
            return false;
        try
        {
            var since = Stopwatch.StartNew();
            _device = await MxDevice.OpenAsync(platform.Hid, found, first: mx => ApplyScrollDirectionAsync(mx, "on connecting", since));
            _lost = false;
            _device.Channel.Disconnected += _ =>
            {
                _lost = true;
                PollNow();
            };
            _device.Channel.EventReceived += OnDeviceEvent;
            _device.CallRetried += call => Log.Write($"No reply to {call}; sent it again");
            await StartEnginesAsync(_device);
            _deviceState = await ReadDeviceAsync(_device);
            _batteryReadAt = DateTime.UtcNow;
            _error = null;
            Log.Write($"Connected to {_deviceState.Name}");
        }
        catch (Exception error)
        {
            Log.Write($"Connecting failed: {error.Message}");
            await DropDeviceAsync($"Couldn't talk to the mouse: {error.Message}");
        }
        return true;
    }

    private async Task<bool> DropDeviceAsync(string? reason)
    {
        if (_device is null)
            return false;
        if (_engine is not null)
            await _engine.DisposeAsync();
        if (_thumb is not null)
            await _thumb.DisposeAsync();
        await _device.DisposeAsync();
        _engine = null;
        _thumb = null;
        _appliedProfile = null;
        _device = null;
        _deviceState = null;
        _held = [];
        _lost = false;
        _error = reason;
        Log.Write(reason ?? "Released the mouse");
        return true;
    }

    private async Task<bool> RefreshBatteryAsync() => ApplyBattery(await _device!.Info.GetBatteryAsync());

    private bool ApplyBattery(Features.BatteryState battery)
    {
        _batteryReadAt = DateTime.UtcNow;
        var next = new BatteryState(battery.Percent, battery.Charging);
        if (_deviceState is null || _deviceState.Battery == next)
            return false;
        _deviceState = _deviceState with { Battery = next };
        return true;
    }

    private async Task StartEnginesAsync(MxDevice mx)
    {
        var profile = _settings.Running(_foreground);
        _macros.Set(_settings.Macros ?? []);
        var engine = await ButtonEngine.StartAsync(mx, platform, UsableBindings(_settings, profile.AllBindings), _macros,
            UsableBindings(_settings, profile.SecondLayer.AllBindings));
        _engine = engine;
        _appliedProfile = profile.Id;
        engine.Failed += error => Log.Write($"Running an action failed: {error.Message}");
        engine.GestureFired += (cid, path, ran) => Published?.Invoke("gesture",
            new GestureEvent(ButtonId(cid), path.Count > 0 ? path[^1] : null, path, ran ? "fire" : "step"));
        engine.ClickFired += (cid, count) => Published?.Invoke("click", new ClickEvent(ButtonId(cid), count));
        engine.LayerChanged += active => OnLayerChanged(engine, active);
        engine.ProfileRequested += OnProfileRequested;
        engine.NativePressed += cid => OnNativePressed(engine, cid);
        engine.MultiClickMs = _settings.MultiClickMs;

        if (!mx.Has(FeatureIds.ThumbWheel))
            return;
        _thumb = new ThumbWheelEngine(mx, platform, _macros);
        _thumb.Failed += error => Log.Write($"Running a thumb wheel action failed: {error.Message}");
        // The buttons are what matter most; a wheel that won't take its settings keeps scrolling as it did.
        try
        {
            await _thumb.UpdateAsync(UsableWheel(profile.Wheel), UsableSecondWheel(profile));
        }
        catch (Exception error)
        {
            Log.Write($"Applying the thumb wheel settings failed: {error.Message}");
        }
    }

    // A binding this platform can't run is skipped rather than stopping the others.
    private IEnumerable<ButtonBinding> UsableBindings(AgentSettings settings, IEnumerable<ButtonBinding> bindings) =>
        settings.Resolved(bindings).Where(b => b.Problem(platform) is null);

    private ThumbWheelSettings UsableWheel(ThumbWheelSettings wheel) =>
        wheel.Problem(platform) is null ? wheel : ThumbWheelSettings.Default;

    private ThumbWheelSettings? UsableSecondWheel(Profile profile) =>
        profile.SecondLayer.ThumbWheel is { } wheel && wheel.Problem(platform) is null ? wheel : null;

    /// <summary>
    /// Runs the profile for the program in front on the connected mouse, writing only what differs from what's
    /// running. A profile that wasn't running starts on its main layer.
    /// </summary>
    private async Task ApplyProfileAsync(AgentSettings settings)
    {
        var profile = settings.Running(_foreground);
        try
        {
            await _engine!.UpdateAsync(UsableBindings(settings, profile.AllBindings),
                UsableBindings(settings, profile.SecondLayer.AllBindings), resetLayer: profile.Id != _appliedProfile);
            _appliedProfile = profile.Id;
            if (_thumb is not null)
                await _thumb.UpdateAsync(UsableWheel(profile.Wheel), UsableSecondWheel(profile));
        }
        finally
        {
            if (_deviceState is not null)
                _deviceState = _deviceState with { Buttons = ButtonStates(), ThumbWheel = ThumbWheelState() };
        }
    }

    private void OnForeground(string? program)
    {
        if (program == _foreground)
            return;
        _foreground = program;
        _ = Task.Run(FollowForegroundAsync);
    }

    /// <summary>Runs the profile of the program now in front, or the chosen one when it has none.</summary>
    private async Task FollowForegroundAsync()
    {
        bool changed = false;
        try
        {
            await _queue.RunLatestAsync("foreground", async () =>
            {
                var running = _settings.Running(_foreground).Id;
                changed = running != _publishedProfile;
                if (_engine is not null && running != _appliedProfile)
                {
                    Log.Write($"Running the profile {_settings.FindProfile(running)?.Name} for {_foreground ?? "no program"}");
                    await ApplyProfileAsync(_settings);
                }
            });
        }
        catch (Exception error)
        {
            Log.Write($"Switching to the profile for {_foreground} failed: {error.Message}");
        }
        if (changed)
            PublishState();
    }

    /// <summary>
    /// The second layer turned on or off. The thumb wheel follows at once; buttons that are a gesture in one layer
    /// only change their raw XY, a write that waits its turn.
    /// </summary>
    private void OnLayerChanged(ButtonEngine engine, bool active)
    {
        if (_thumb is { } thumb)
            thumb.LayerActive = active;
        _ = Task.Run(async () =>
        {
            try
            {
                await _queue.RunLatestAsync("layer", () => _engine == engine ? engine.SyncLayerAsync() : Task.CompletedTask);
            }
            catch (Exception error)
            {
                Log.Write($"Changing raw XY for the layer failed: {error.Message}");
            }
            PublishState();
        });
    }

    /// <summary>A <c>profile</c> action ran: it chooses that profile, unless programs choose it instead.</summary>
    private void OnProfileRequested(string id)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _queue.RunLatestAsync("switchProfile", () => ChangeSettingsAsync(s =>
                {
                    if (s.FindProfile(id) is not { } profile)
                    {
                        Log.Write($"A button switched to the profile '{id}', which was deleted");
                        return s;
                    }
                    if (!profile.Switchable)
                    {
                        Log.Write($"A button switched to {profile.Name}, which runs only with its programs");
                        return s;
                    }
                    return s.Active.Id == id ? s : s with { ActiveProfile = id };
                }));
                PublishState();
            }
            catch (Exception error)
            {
                Log.Write($"Switching profiles from a button failed: {error.Message}");
            }
        });
    }

    /// <summary>
    /// A button bound only in the layer that's off went down. Smart Shift's own switch between ratchet and free spin
    /// is done for it; the other buttons do nothing of their own without Logi Options+.
    /// </summary>
    private void OnNativePressed(ButtonEngine engine, ushort cid)
    {
        if (ButtonId(cid) != "smartshift")
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                await _queue.RunAsync(async () =>
                {
                    if (_engine != engine || _device is not { } mx)
                        return;
                    var shift = await mx.Wheels.GetSmartShiftAsync();
                    await mx.Wheels.SetSmartShiftAsync(shift with { Ratchet = !shift.Ratchet });
                    shift = await mx.Wheels.GetSmartShiftAsync();
                    _deviceState = _deviceState! with
                    {
                        SmartShift = _deviceState.SmartShift with { Ratchet = shift.Ratchet, AutoDisengage = shift.AutoDisengage, Torque = shift.Torque },
                    };
                });
                PublishState();
            }
            catch (Exception error)
            {
                Log.Write($"Switching the wheel's ratchet failed: {error.Message}");
            }
        });
    }

    private void OnDeviceEvent(HidppEvent e)
    {
        var device = _device;
        if (device is null)
            return;

        // WIRELESS_DEVICE_STATUS asks for reconfiguration after the mouse wakes or reconnects to this host.
        if (device.TryGetIndex(FeatureIds.WirelessDeviceStatus, out var status) && e.FeatureIndex == status && e.Payload[1] == 1)
        {
            // At once rather than queued with the re-diversion, so held keys don't stay down behind other work.
            _engine?.ReleaseAll();
            foreach (var cid in _held)
                Published?.Invoke("button", new ButtonEvent(ButtonId(cid), cid, false));
            _held = [];
            // Not queued either: this is the setting a person notices first, and the rest can take a moment.
            var since = Stopwatch.StartNew();
            _ = Task.Run(() => ApplyScrollDirectionAsync(device, "after waking", since));
            _ = Task.Run(ReapplyAfterReconnectAsync);
            return;
        }

        if (device.Info.ParseBatteryEvent(e) is { } battery)
        {
            _ = Task.Run(() => ApplyBatteryEventAsync(device, battery));
            return;
        }

        if (device.Controls.ParseHeld(e) is not { } now)
            return;
        foreach (var cid in now.Except(_held))
            Published?.Invoke("button", new ButtonEvent(ButtonId(cid), cid, true));
        foreach (var cid in _held.Except(now))
            Published?.Invoke("button", new ButtonEvent(ButtonId(cid), cid, false));
        _held = [.. now];
    }

    private Task ReapplyAfterReconnectAsync() => _queue.RunAsync(async () =>
    {
        try
        {
            if (_engine is not null)
                await _engine.DivertAsync();
            if (_thumb is not null)
                await _thumb.ReapplyAsync();
            Log.Write("The mouse asked for reconfiguration; diverted bound controls again");
        }
        catch (Exception error)
        {
            Log.Write($"Reapplying bindings failed: {error.Message}");
        }
    });

    // Queued with the device work: connecting or changing profiles rebuilds the whole state from earlier reads,
    // and would drop a battery change made alongside it.
    private async Task ApplyBatteryEventAsync(MxDevice device, Features.BatteryState battery)
    {
        bool changed = false;
        await _queue.RunLatestAsync("battery", () =>
        {
            changed = _device == device && ApplyBattery(battery);
            return Task.CompletedTask;
        });
        if (changed)
            PublishState();
    }

    private static string? ButtonId(ushort cid) =>
        ReprogControls.KnownButtons.FirstOrDefault(b => b.Value == cid).Key;

    private async Task<DeviceState> ReadDeviceAsync(MxDevice mx)
    {
        var name = await mx.Info.GetNameAsync();
        var main = (await mx.Info.GetFirmwareAsync()).LastOrDefault(f => f.Type == 0);
        var battery = await mx.Info.GetBatteryAsync();

        var (dpi, defaultDpi) = await mx.Pointer.GetDpiAsync();
        var range = await mx.Pointer.GetDpiRangeAsync() ?? new DpiRange(dpi, dpi, 1);
        var shiftCaps = await mx.Wheels.GetSmartShiftCapabilitiesAsync();
        var shift = await mx.Wheels.GetSmartShiftAsync();
        var haptic = await mx.Haptics.GetLevelAsync();
        var waveforms = await mx.Haptics.GetWaveformsAsync();
        var forceButtons = await mx.Haptics.GetForceButtonsAsync();
        ForceButton? force = forceButtons.Count > 0 ? forceButtons[0] : null;

        _controls = (await mx.Controls.GetControlsAsync())
            .Where(c => ButtonId(c.Cid) is not null)
            .Select(c => (ButtonId(c.Cid)!, c.Name, c.Cid, c.Reporting.Diverted))
            .ToArray();
        if (mx.Has(FeatureIds.ThumbWheel))
            _thumbDivertedAtConnect = (await mx.Wheels.GetThumbWheelAsync()).Diverted;

        var wheel = mx.Has(FeatureIds.HiResWheel) ? await mx.Wheels.GetHiResWheelAsync() : (Features.HiResWheelState?)null;

        return new DeviceState(
            name, $"{mx.Interface.ProductId:X4}", main?.ToString() ?? "",
            new BatteryState(battery.Percent, battery.Charging),
            new DpiState(dpi, defaultDpi, range.Min, range.Max, range.Step),
            new SmartShiftSettings(shift.Ratchet, shift.AutoDisengage, shift.Torque, shiftCaps.TunableTorque,
                shiftCaps.DefaultAutoDisengage, shiftCaps.DefaultTorque),
            new HapticsState(haptic.Enabled, haptic.Level, waveforms.Select(w => w.ToString()).ToArray()),
            force is { } f ? new ForceState(f.Current, f.Default, f.Min, f.Max) : null,
            ButtonStates(), ThumbWheelState(), await ReadHostsAsync(mx),
            wheel is { CanInvert: true } ? wheel.Value.Inverted : null);
    }

    // Whether another app had diverted each control before us; one we never diverted shows how it was at connect.
    private IReadOnlyList<ButtonState> ButtonStates() =>
        _controls.Select(c => new ButtonState(c.Id, c.Name, c.Cid, _engine?.DivertedBefore(c.Cid) ?? c.Diverted)).ToArray();

    private ThumbWheelDevice? ThumbWheelState() =>
        _thumb is null ? null : new ThumbWheelDevice(_thumb.DivertedBefore ?? _thumbDivertedAtConnect);

    // Easy-Switch is extra: a mouse that won't describe its channels still works for everything else.
    private static async Task<HostsInfo?> ReadHostsAsync(MxDevice mx)
    {
        if (!mx.Hosts.Supported)
            return null;
        try
        {
            return await mx.Hosts.GetAsync();
        }
        catch (Exception error) when (error is HidppException or TimeoutException or NotSupportedException)
        {
            Log.Write($"Reading the Easy-Switch channels failed: {error.Message}");
            return null;
        }
    }

    private void PublishState()
    {
        _publishedProfile = _settings.Running(_foreground).Id;
        Interlocked.Increment(ref _revision);
        Published?.Invoke("state", State);
    }

    /// <summary>Handles one client request and returns its result.</summary>
    public async Task<object?> HandleAsync(string method, JsonElement args)
    {
        switch (method)
        {
            case "getState":
                return State;
            case "setStartAtLogin":
                _settings = _settings with { StartAtLogin = args.GetProperty("enabled").GetBoolean() };
                store.Save(_settings);
                ApplyLoginItem();
                break;
            case "setMultiClickMs":
                int ms = args.GetProperty("ms").GetInt32();
                if (ms is < AgentSettings.MinMultiClickMs or > AgentSettings.MaxMultiClickMs)
                    throw new ArgumentException(
                        $"The multi-click window is {AgentSettings.MinMultiClickMs} to {AgentSettings.MaxMultiClickMs} ms.");
                await _queue.RunAsync(async () =>
                {
                    await ChangeSettingsAsync(s => s with { MultiClickMs = ms }, apply: false);
                    if (_engine is not null)
                        _engine.MultiClickMs = ms;
                });
                break;
            case "startKeyCapture":
                LazyInitializer.EnsureInitialized(ref _recorder, CreateRecorder).Start();
                return null;
            case "stopKeyCapture":
                StopKeyCapture();
                return null;
            case "shutdown":
                Log.Write("A client asked the agent to stop");
                _ = Task.Delay(100).ContinueWith(_ => ShutdownRequested?.Invoke());
                return null;
            case "playHaptic":
                var waveform = Enum.Parse<HapticWaveform>(args.GetProperty("waveform").GetString()!);
                await _queue.RunAsync(() => Device().Haptics.PlayAsync(waveform));
                return null;
            case "switchHost":
                int host = args.GetProperty("host").GetInt32();
                await _queue.RunAsync(() => SwitchHostAsync(host));
                return State;
            case "listPrograms":
                return (platform.Programs?.Running() ?? []).ToArray();
            case "setProfilePrograms":
                var bound = ProfileArg(args).Id;
                var programs = ProgramsArg(args);
                await _queue.RunAsync(() => ChangeSettingsAsync(s => SetPrograms(s, bound, programs)));
                break;
            case "createProfile":
                var source = args.TryGetProperty("copyFrom", out var from) && from.GetString() is { } fromId ? FindProfile(fromId) : null;
                var created = ProfileName(args);
                var createdIcon = args.TryGetProperty("icon", out _) ? ProfileIcon(args) : source?.Icon;
                await _queue.RunAsync(() => ChangeSettingsAsync(s => CreateProfile(s, created, createdIcon, source)));
                break;
            case "setProfileIcon":
                var iconFor = ProfileArg(args).Id;
                var icon = ProfileIcon(args);
                await _queue.RunAsync(() => ChangeSettingsAsync(
                    s => s with { Profiles = s.AllProfiles.Select(p => p.Id == iconFor ? p with { Icon = icon } : p).ToArray() }, apply: false));
                break;
            case "renameProfile":
                var id = ProfileArg(args).Id;
                var name = ProfileName(args);
                await _queue.RunAsync(() => ChangeSettingsAsync(
                    s => s with { Profiles = s.AllProfiles.Select(p => p.Id == id ? p with { Name = name } : p).ToArray() }, apply: false));
                break;
            case "deleteProfile":
                var doomed = ProfileArg(args).Id;
                await _queue.RunAsync(() => ChangeSettingsAsync(s =>
                {
                    var rest = s.AllProfiles.Where(p => p.Id != doomed).ToArray();
                    if (rest.Length == 0)
                        throw new InvalidOperationException("The last profile can't be deleted.");
                    if (!rest.Any(p => p.Switchable))
                        throw new InvalidOperationException(KeepOneChosen);
                    var chosen = s.Active.Id == doomed ? rest.First(p => p.Switchable).Id : s.Active.Id;
                    return s with { Profiles = rest, ActiveProfile = chosen };
                }));
                break;
            case "setGesturePreset":
                var preset = args.GetProperty("preset").Deserialize(AgentJson.Default.GesturePreset)
                    ?? throw new ArgumentException("Missing the gesture preset.");
                PresetArg(preset);
                ButtonAction[] steps = [.. new[] { preset.Up, preset.Down, preset.Left, preset.Right }.SelectMany(s => s.All).Select(s => s.Action)];
                CheckMacros(steps);
                CheckProfileActions(steps);
                await _queue.RunAsync(() => ChangeSettingsAsync(s => s with
                {
                    GesturePresets = s.GesturePresets?.Any(p => p.Id == preset.Id) == true
                        ? s.GesturePresets.Select(p => p.Id == preset.Id ? preset : p).ToArray()
                        : [.. s.GesturePresets ?? [], preset],
                }));
                break;
            case "deleteGesturePreset":
                // A built-in one goes back to how it came. Buttons keep pointing at a deleted one and run just their
                // clicks, so putting it back with the same ID, as undo does, brings their moves back too.
                var gone = args.GetProperty("id").GetString() ?? "";
                await _queue.RunAsync(() => ChangeSettingsAsync(s => s with
                {
                    GesturePresets = (s.GesturePresets ?? []).Where(p => p.Id != gone).ToArray(),
                }));
                break;
            case "setMacro":
                var macro = args.GetProperty("macro").Deserialize(AgentJson.Default.Macro)
                    ?? throw new ArgumentException("Missing the macro.");
                if (!IsId(macro.Id))
                    throw new ArgumentException($"'{macro.Id}' isn't a macro ID.");
                if (macro.Problem(platform) is { } macroProblem)
                    throw new ArgumentException(macroProblem);
                await _queue.RunAsync(() => ChangeSettingsAsync(s => s with
                {
                    Macros = s.Macros?.Any(m => m.Id == macro.Id) == true
                        ? s.Macros.Select(m => m.Id == macro.Id ? macro : m).ToArray()
                        : [.. s.Macros ?? [], macro],
                }, apply: false));
                break;
            case "deleteMacro":
                // Actions keep naming a deleted macro and do nothing, so putting it back with the same ID, as undo
                // does, brings them back too.
                var unwanted = args.GetProperty("id").GetString() ?? "";
                await _queue.RunAsync(() => ChangeSettingsAsync(
                    s => s with { Macros = (s.Macros ?? []).Where(m => m.Id != unwanted).ToArray() }, apply: false));
                break;
            default:
                // A setting: its arguments are checked now, and its work waits its turn, giving way to a newer change
                // to the same setting. Each layer of each profile is a setting of its own.
                var work = Prepare(method, args);
                var key = method is "setBindings" or "setThumbWheel" ? $"{method}:{EditedProfile(args)}:{EditsLayer(args)}" : method;
                await _queue.RunLatestAsync(key, work);
                break;
        }
        PublishState();
        return State;
    }

    private MxDevice Device() => _device ?? throw new InvalidOperationException("The mouse isn't connected.");

    /// <summary>
    /// The work for a request that changes one setting. A device write rereads only what it changed and keeps the
    /// rest of what's known, so it comes back in a few calls.
    /// </summary>
    private Func<Task> Prepare(string method, JsonElement args)
    {
        switch (method)
        {
            case "setBindings":
                var bindings = args.GetProperty("bindings").Deserialize(AgentJson.Default.ButtonBindingArray) ?? [];
                foreach (var binding in bindings)
                {
                    if (binding.Problem(platform) is { } problem)
                        throw new ArgumentException($"{binding.Button}: {problem}");
                    if (binding is { IsGesture: true, Gesture: null, Preset: { } preset } && _settings.FindPreset(preset) is null)
                        throw new ArgumentException($"{binding.Button}: that gesture preset was deleted.");
                    CheckMacros(binding.AllActions);
                    CheckProfileActions(binding.AllActions);
                }
                var distinct = bindings.DistinctBy(b => b.Button.ToLowerInvariant()).ToArray();
                var bindingsFor = EditedProfile(args);
                return EditsLayer(args)
                    ? () => ChangeSettingsAsync(s => EditProfile(s, bindingsFor, p => WithLayer(p, p.SecondLayer with { Bindings = distinct })))
                    : () => ChangeSettingsAsync(s => EditProfile(s, bindingsFor, p => p with { Bindings = distinct }));
            case "setThumbWheel":
                var layer = EditsLayer(args);
                var wheelArg = args.GetProperty("thumbWheel");
                // The second layer takes null for keeping the main layer's settings.
                var wheel = layer && wheelArg.ValueKind == JsonValueKind.Null
                    ? null
                    : wheelArg.Deserialize(AgentJson.Default.ThumbWheelSettings) ?? throw new ArgumentException("Missing thumb wheel settings.");
                if (wheel?.Problem(platform) is { } wheelProblem)
                    throw new ArgumentException(wheelProblem);
                CheckMacros([wheel?.Left, wheel?.Right]);
                var wheelFor = EditedProfile(args);
                return layer
                    ? () => ChangeSettingsAsync(s => EditProfile(s, wheelFor, p => WithLayer(p, p.SecondLayer with { ThumbWheel = wheel })))
                    : () => ChangeSettingsAsync(s => EditProfile(s, wheelFor, p => p with { ThumbWheel = wheel!.IsDefault ? null : wheel }));
            case "switchProfile":
                var target = ProfileArg(args);
                if (!target.Switchable)
                    throw new ArgumentException($"{target.Name} runs only while its programs are in front, so it can't be chosen.");
                return () => ChangeSettingsAsync(s => s.FindProfile(target.Id) is { Switchable: true }
                    ? s with { ActiveProfile = target.Id }
                    : throw new ArgumentException($"The profile {target.Name} was deleted or follows programs now."));
            case "setDpi":
                int dpi = args.GetProperty("dpi").GetInt32();
                return async () =>
                {
                    await Device().Pointer.SetDpiAsync(dpi);
                    var (current, _) = await Device().Pointer.GetDpiAsync();
                    _deviceState = _deviceState! with { Dpi = _deviceState.Dpi with { Current = current } };
                    if (_engine is not null)
                        _engine.Dpi = current;
                };
            case "setSmartShift":
                var wanted = new SmartShiftState(args.GetProperty("ratchet").GetBoolean(),
                    (byte)args.GetProperty("autoDisengage").GetInt32(), (byte)args.GetProperty("torque").GetInt32());
                return async () =>
                {
                    await Device().Wheels.SetSmartShiftAsync(wanted);
                    var shift = await Device().Wheels.GetSmartShiftAsync();
                    _deviceState = _deviceState! with
                    {
                        SmartShift = _deviceState.SmartShift with { Ratchet = shift.Ratchet, AutoDisengage = shift.AutoDisengage, Torque = shift.Torque },
                    };
                };
            case "setScrollInverted":
                bool inverted = args.GetProperty("inverted").GetBoolean();
                return async () =>
                {
                    await Device().Wheels.SetHiResInvertedAsync(inverted);
                    await ChangeSettingsAsync(s => s with { ScrollInverted = inverted }, apply: false);
                    var mode = await Device().Wheels.GetHiResWheelAsync();
                    _deviceState = _deviceState! with { ScrollInverted = mode.Inverted };
                };
            case "setHapticLevel":
                int level = args.GetProperty("level").GetInt32();
                return async () =>
                {
                    await Device().Haptics.SetLevelAsync(level);
                    var haptic = await Device().Haptics.GetLevelAsync();
                    _deviceState = _deviceState! with { Haptics = _deviceState.Haptics with { Enabled = haptic.Enabled, Level = haptic.Level } };
                };
            case "setForce":
                int force = args.GetProperty("force").GetInt32();
                return async () =>
                {
                    await Device().Haptics.SetForceAsync(0, force);
                    var button = (await Device().Haptics.GetForceButtonsAsync())[0];
                    _deviceState = _deviceState! with { ForceButton = _deviceState.ForceButton! with { Current = button.Current } };
                };
            case "setHostName":
                var hostName = args.GetProperty("name").GetString()?.Trim();
                if (string.IsNullOrEmpty(hostName))
                    throw new ArgumentException("The name can't be empty.");
                return async () =>
                {
                    await Device().Hosts.RenameCurrentAsync(hostName);
                    _deviceState = _deviceState! with { Hosts = await Device().Hosts.GetAsync() };
                };
            default:
                throw new ArgumentException($"Unknown method '{method}'");
        }
    }

    private async Task SwitchHostAsync(int host)
    {
        var mx = Device();
        var hosts = _deviceState!.Hosts ?? throw new NotSupportedException("This mouse has no Easy-Switch channels.");
        if (hosts.Channels.FirstOrDefault(c => c.Index == host) is not { Paired: true } channel)
            throw new ArgumentException($"Channel {host + 1} has no computer paired.");
        if (channel.Index == hosts.Current)
            return;
        Log.Write($"Switching the mouse to channel {host + 1}");
        // The mouse leaves now; the poll notices it's gone and connects again when it comes back.
        await mx.Hosts.SwitchAsync(host);
    }

    /// <summary>Hands the keyboard back if a client was recording a shortcut.</summary>
    public void StopKeyCapture() => _recorder?.Stop();

    private KeyRecorder CreateRecorder()
    {
        var capture = platform.KeyCapture ?? throw new NotSupportedException("This platform can't record keys for the app.");
        var recorder = new KeyRecorder(capture, KeyCaptureTimeout);
        recorder.Key += (code, down) => Published?.Invoke("key", new KeyEvent(code, down));
        recorder.TimedOut += () => Published?.Invoke("keyCapture", new KeyCaptureEvent(false));
        return recorder;
    }

    private Profile ProfileArg(JsonElement args) => FindProfile(args.GetProperty("id").GetString() ?? "");

    private Profile FindProfile(string id) =>
        _settings.AllProfiles.FirstOrDefault(p => p.Id == id) ?? throw new ArgumentException($"There's no profile '{id}'.");

    private static bool IsId(string id) => id.Length is > 0 and <= 40 && id.All(c => c is '-' or '_' || char.IsAsciiLetterOrDigit(c));

    /// <summary>Refuses actions running a macro that isn't there, which would otherwise do nothing without saying so.</summary>
    private void CheckMacros(IEnumerable<ButtonAction?> actions)
    {
        foreach (var action in actions)
        {
            if (action is { Type: ActionTypes.Macro, Macro: { } id } && _settings.Macros?.Any(m => m.Id == id) != true)
                throw new ArgumentException("That macro was deleted.");
        }
    }

    /// <summary>Refuses switching to a profile by something that isn't a profile ID.</summary>
    private static void CheckProfileActions(IEnumerable<ButtonAction?> actions)
    {
        foreach (var action in actions)
        {
            if (action is { Type: ActionTypes.Profile, Profile: { } id } && !IsId(id))
                throw new ArgumentException($"'{id}' isn't a profile ID.");
        }
    }

    /// <summary>The profile a <c>setBindings</c> or <c>setThumbWheel</c> changes: the one named, or the one running.</summary>
    private string EditedProfile(JsonElement args) =>
        args.TryGetProperty("profile", out var profile) && profile.GetString() is { } id ? FindProfile(id).Id : _settings.Running(_foreground).Id;

    private static bool EditsLayer(JsonElement args) =>
        args.TryGetProperty("layer", out var layer) && layer.ValueKind == JsonValueKind.True;

    private static AgentSettings EditProfile(AgentSettings settings, string id, Func<Profile, Profile> change) =>
        settings.FindProfile(id) is null
            ? throw new ArgumentException("That profile was deleted.")
            : settings.WithProfile(id, change);

    private static Profile WithLayer(Profile profile, ProfileLayer layer) => profile with { Layer = layer.IsEmpty ? null : layer };

    private const string KeepOneChosen = "Keep at least one profile without programs; it runs for every other program.";

    /// <summary>Program IDs as stored: trimmed, lower case and each once. A path is cut down to its file name.</summary>
    private static string[] ProgramsArg(JsonElement args)
    {
        var programs = args.GetProperty("programs").Deserialize(AgentJson.Default.StringArray) ?? [];
        return programs
            .Select(p => Path.GetFileName(p.Trim()).ToLowerInvariant())
            .Select(p => p.Length is > 0 and <= 260 ? p : throw new ArgumentException($"'{p}' isn't a program name."))
            .Distinct()
            .ToArray();
    }

    /// <summary>
    /// Binds <paramref name="programs"/> to the profile, replacing what it had. A program follows one profile, and
    /// one profile stays without programs to run for the rest.
    /// </summary>
    private static AgentSettings SetPrograms(AgentSettings settings, string id, IReadOnlyList<string> programs)
    {
        if (settings.FindProfile(id) is null)
            throw new ArgumentException("That profile was deleted.");
        foreach (var program in programs)
        {
            if (settings.ProfileFor(program) is { } other && other.Id != id)
                throw new ArgumentException($"{program} already runs {other.Name}.");
        }
        var next = settings.WithProfile(id, p => p with { Programs = programs.Count > 0 ? [.. programs] : null });
        if (!next.AllProfiles.Any(p => p.Switchable))
            throw new ArgumentException(KeepOneChosen);
        // Profiles that follow programs aren't chosen; the chosen one stays put unless it's this one.
        return next with { ActiveProfile = next.Active.Id };
    }

    private static IReadOnlyList<MacroUse> MacroUses(AgentSettings settings)
    {
        static IEnumerable<string> Run(IEnumerable<ButtonAction?> actions) =>
            actions.OfType<ButtonAction>().Where(a => a.Type == ActionTypes.Macro && a.Macro is not null).Select(a => a.Macro!);
        var uses = new List<MacroUse>();
        foreach (var profile in settings.AllProfiles)
        {
            foreach (var b in profile.AllBindings)
                uses.AddRange(Run(b.AllActions).Select(id => new MacroUse(id, profile.Id, b.Button)));
            foreach (var b in profile.SecondLayer.AllBindings)
                uses.AddRange(Run(b.AllActions).Select(id => new MacroUse(id, profile.Id, b.Button, Layer: true)));
            if (profile.ThumbWheel is { } wheel)
                uses.AddRange(Run([wheel.Left, wheel.Right]).Select(id => new MacroUse(id, profile.Id, "thumbwheel")));
            if (profile.SecondLayer.ThumbWheel is { } second)
                uses.AddRange(Run([second.Left, second.Right]).Select(id => new MacroUse(id, profile.Id, "thumbwheel", Layer: true)));
        }
        foreach (var preset in settings.AllPresets)
        {
            var steps = new[] { preset.Up, preset.Down, preset.Left, preset.Right }.SelectMany(s => s.All).Select(s => s.Action);
            uses.AddRange(Run(steps).Select(id => new MacroUse(id, Preset: preset.Id)));
        }
        return uses.Distinct().ToArray();
    }

    private void PresetArg(GesturePreset preset)
    {
        if (!IsId(preset.Id))
            throw new ArgumentException($"'{preset.Id}' isn't a preset ID.");
        if (preset.Problem(platform) is { } problem)
            throw new ArgumentException(problem);
        if (PresetLibrary.BuiltIn.FirstOrDefault(p => p.Id == preset.Id) is { } builtIn && builtIn.Name != preset.Name)
            throw new ArgumentException("A built-in preset keeps its name. Duplicate it to name your own.");
        if (preset.Icon is { } icon && !(icon.Length is > 0 and <= 40 && icon.All(c => c is '-' or (>= 'a' and <= 'z') or (>= '0' and <= '9'))))
            throw new ArgumentException($"'{icon}' isn't an icon name.");
    }

    private static string ProfileName(JsonElement args) =>
        args.GetProperty("name").GetString()?.Trim() is { Length: > 0 } name ? name : throw new ArgumentException("A profile needs a name.");

    /// <summary>The app only draws names it knows, so anything else is refused rather than stored as a blank.</summary>
    private static string? ProfileIcon(JsonElement args) =>
        args.TryGetProperty("icon", out var icon) && icon.GetString() is { } value
            ? value.Length is > 0 and <= 40 && value.All(c => c is '-' or (>= 'a' and <= 'z') or (>= '0' and <= '9'))
                ? value
                : throw new ArgumentException($"'{value}' isn't an icon name.")
            : null;

    /// <summary>A new profile, active from now on: a copy of <paramref name="source"/>, or every control at its default.</summary>
    private static AgentSettings CreateProfile(AgentSettings settings, string name, string? icon, Profile? source)
    {
        // A program follows one profile, so a copy starts without the source's programs.
        var profile = (source ?? new Profile("", name, [])) with { Id = Profile.NewId(), Name = name, Icon = icon, Programs = null };
        return settings with { Profiles = [.. settings.AllProfiles, profile], ActiveProfile = profile.Id };
    }

    /// <summary>
    /// Runs changed settings on the mouse, then saves them. If the mouse refuses, what ran before is put back and
    /// the settings stay as they were. Only call it in the queue's turn.
    /// </summary>
    private async Task ChangeSettingsAsync(Func<AgentSettings, AgentSettings> change, bool apply = true)
    {
        var previous = _settings;
        var next = change(previous);
        if (ReferenceEquals(next, previous))
            return;
        if (apply && _engine is not null)
        {
            try
            {
                await ApplyProfileAsync(next);
            }
            catch (Exception error)
            {
                Log.Write($"Applying the profile failed, putting back the one before: {error.Message}");
                try
                {
                    await ApplyProfileAsync(previous);
                }
                catch (Exception again)
                {
                    Log.Write($"Putting back the profile failed too: {again.Message}");
                }
                throw;
            }
        }
        _settings = next;
        _macros.Set(next.Macros ?? []);
        store.Save(next);
    }

    public async ValueTask DisposeAsync()
    {
        _foregroundWatch?.Dispose();
        _arrivals?.Dispose();
        _recorder?.Dispose();
        await _queue.RunAsync(() => DropDeviceAsync(null));
    }
}
