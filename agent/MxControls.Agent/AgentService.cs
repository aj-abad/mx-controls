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
    public const string LoginItemName = "MX Controls Agent";

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
    private DateTime _batteryReadAt;
    private KeyRecorder? _recorder;

    /// <summary>An event for every client: its name and payload.</summary>
    public event Action<string, object>? Published;

    /// <summary>A client asked the agent to exit.</summary>
    public event Action? ShutdownRequested;

    public AgentState State =>
        new(_device is not null && _deviceState is not null, _deviceState, _settings.AllBindings, _settings.Active.Wheel,
            _settings.AllProfiles.Select(p => new ProfileSummary(p.Id, p.Name, p.Icon)).ToArray(), _settings.Active.Id,
            _settings.StartAtLogin, _optionsPlus, _error, Interlocked.Read(ref _revision));

    public async Task RunAsync(CancellationToken stop)
    {
        ApplyLoginItem();
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
            try { await Task.Delay(PollInterval, stop); } catch (OperationCanceledException) { }
        }
    }

    /// <summary>Points the login item at this executable, so it follows the app when it moves or updates.</summary>
    private void ApplyLoginItem()
    {
        try
        {
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

    private async Task<bool> TryConnectAsync()
    {
        var found = MxDevice.FindBluetooth(platform.Hid).FirstOrDefault();
        if (found is null)
            return false;
        try
        {
            _device = await MxDevice.OpenAsync(platform.Hid, found);
            _lost = false;
            _device.Channel.Disconnected += _ => _lost = true;
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
        var profile = _settings.Active;
        _engine = await ButtonEngine.StartAsync(mx, platform, UsableBindings(profile));
        _engine.Failed += error => Log.Write($"Running an action failed: {error.Message}");
        _engine.GestureFired += (cid, direction) => Published?.Invoke("gesture", new GestureEvent(ButtonId(cid), direction));

        if (!mx.Has(FeatureIds.ThumbWheel))
            return;
        _thumb = new ThumbWheelEngine(mx, platform);
        _thumb.Failed += error => Log.Write($"Running a thumb wheel action failed: {error.Message}");
        // The buttons are what matter most; a wheel that won't take its settings keeps scrolling as it did.
        try
        {
            await _thumb.UpdateAsync(UsableWheel(profile));
        }
        catch (Exception error)
        {
            Log.Write($"Applying the thumb wheel settings failed: {error.Message}");
        }
    }

    // A binding this platform can't run is skipped rather than stopping the others.
    private IEnumerable<ButtonBinding> UsableBindings(Profile profile) => profile.AllBindings.Where(b => b.Problem(platform) is null);

    private ThumbWheelSettings UsableWheel(Profile profile) =>
        profile.Wheel.Problem(platform) is null ? profile.Wheel : ThumbWheelSettings.Default;

    /// <summary>Runs a profile on the connected mouse, writing only what differs from what's running.</summary>
    private async Task ApplyProfileAsync(Profile profile)
    {
        await _engine!.UpdateAsync(UsableBindings(profile));
        if (_thumb is not null)
            await _thumb.UpdateAsync(UsableWheel(profile));
    }

    private void OnDeviceEvent(HidppEvent e)
    {
        var device = _device;
        if (device is null)
            return;

        // WIRELESS_DEVICE_STATUS asks for reconfiguration after the mouse wakes or reconnects to this host.
        if (device.TryGetIndex(FeatureIds.WirelessDeviceStatus, out var status) && e.FeatureIndex == status && e.Payload[1] == 1)
        {
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

        return new DeviceState(
            name, $"{mx.Interface.ProductId:X4}", main?.ToString() ?? "",
            new BatteryState(battery.Percent, battery.Charging),
            new DpiState(dpi, defaultDpi, range.Min, range.Max, range.Step),
            new SmartShiftSettings(shift.Ratchet, shift.AutoDisengage, shift.Torque, shiftCaps.TunableTorque,
                shiftCaps.DefaultAutoDisengage, shiftCaps.DefaultTorque),
            new HapticsState(haptic.Enabled, haptic.Level, waveforms.Select(w => w.ToString()).ToArray()),
            force is { } f ? new ForceState(f.Current, f.Default, f.Min, f.Max) : null,
            ButtonStates(), ThumbWheelState(), await ReadHostsAsync(mx));
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
                    return s with { Profiles = rest, ActiveProfile = s.Active.Id == doomed ? rest[0].Id : s.Active.Id };
                }));
                break;
            default:
                // A setting: its arguments are checked now, and its work waits its turn, giving way to a newer change.
                await _queue.RunLatestAsync(method, Prepare(method, args));
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
                }
                var distinct = bindings.DistinctBy(b => b.Button.ToLowerInvariant()).ToArray();
                return () => ChangeSettingsAsync(s => s.WithActive(p => p with { Bindings = distinct }));
            case "setThumbWheel":
                var wheel = args.GetProperty("thumbWheel").Deserialize(AgentJson.Default.ThumbWheelSettings)
                    ?? throw new ArgumentException("Missing thumb wheel settings.");
                if (wheel.Problem(platform) is { } wheelProblem)
                    throw new ArgumentException(wheelProblem);
                return () => ChangeSettingsAsync(s => s.WithActive(p => p with { ThumbWheel = wheel.IsDefault ? null : wheel }));
            case "switchProfile":
                var target = ProfileArg(args);
                return () => ChangeSettingsAsync(s => s.AllProfiles.Any(p => p.Id == target.Id)
                    ? s with { ActiveProfile = target.Id }
                    : throw new ArgumentException($"The profile {target.Name} was deleted."));
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
        var profile = (source ?? new Profile("", name, [])) with { Id = Profile.NewId(), Name = name, Icon = icon };
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
        if (apply && _device is not null)
        {
            try
            {
                await ApplyProfileAsync(next.Active);
            }
            catch (Exception error)
            {
                Log.Write($"Applying the profile failed, putting back the one before: {error.Message}");
                try
                {
                    await ApplyProfileAsync(previous.Active);
                }
                catch (Exception again)
                {
                    Log.Write($"Putting back the profile failed too: {again.Message}");
                }
                throw;
            }
            finally
            {
                _deviceState = _deviceState! with { Buttons = ButtonStates(), ThumbWheel = ThumbWheelState() };
            }
        }
        _settings = next;
        store.Save(next);
    }

    public async ValueTask DisposeAsync()
    {
        _recorder?.Dispose();
        await _queue.RunAsync(() => DropDeviceAsync(null));
    }
}
