using System.Text.Json;
using MxControls.Features;
using MxControls.Hidpp;
using MxControls.Platform;
using MxControls.Remap;

namespace MxControls.Agent;

/// <summary>
/// Owns the mouse: connects when it appears, applies the saved button mappings, reapplies them when the mouse
/// reconnects, and turns client requests into device calls. Every device call goes through one gate.
/// </summary>
public sealed class AgentService(IPlatform platform, SettingsStore store) : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan BatteryInterval = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private AgentSettings _settings = store.Load();
    private MxDevice? _device;
    private HoldRemapper? _remapper;
    private DeviceState? _deviceState;
    private HashSet<ushort> _held = [];
    private bool _optionsPlus;
    private string? _error;
    private volatile bool _lost;
    private DateTime _batteryReadAt;

    /// <summary>An event for every client: its name and payload.</summary>
    public event Action<string, object>? Published;

    public AgentState State => new(_device is not null && _deviceState is not null, _deviceState, _settings.Mappings, _optionsPlus, _error);

    public async Task RunAsync(CancellationToken stop)
    {
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

    private async Task TickAsync()
    {
        bool changed = false;
        bool optionsPlus = platform.IsOptionsPlusRunning();
        if (optionsPlus != _optionsPlus)
        {
            _optionsPlus = optionsPlus;
            changed = true;
        }

        await _gate.WaitAsync();
        try
        {
            if (_lost)
                changed |= await DropDeviceAsync("The mouse disconnected.");
            if (_device is null)
                changed |= await TryConnectAsync();
            else if (DateTime.UtcNow - _batteryReadAt > BatteryInterval)
                changed |= await RefreshBatteryAsync();
        }
        finally
        {
            _gate.Release();
        }
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
            await ApplyMappingsAsync();
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
        if (_remapper is not null)
            await _remapper.DisposeAsync();
        await _device.DisposeAsync();
        _remapper = null;
        _device = null;
        _deviceState = null;
        _held = [];
        _lost = false;
        _error = reason;
        Log.Write(reason ?? "Released the mouse");
        return true;
    }

    private async Task<bool> RefreshBatteryAsync()
    {
        var battery = await _device!.Info.GetBatteryAsync();
        _batteryReadAt = DateTime.UtcNow;
        var next = new BatteryState(battery.Percent, battery.Charging);
        if (_deviceState is null || _deviceState.Battery == next)
            return false;
        _deviceState = _deviceState with { Battery = next };
        return true;
    }

    private async Task ApplyMappingsAsync()
    {
        if (_remapper is not null)
        {
            await _remapper.DisposeAsync();
            _remapper = null;
        }
        var map = _settings.Mappings
            .Where(m => ReprogControls.KnownButtons.ContainsKey(m.Button) && platform.Keys.Supports(m.Key))
            .ToDictionary(m => ReprogControls.KnownButtons[m.Button], m => m.Key);
        if (map.Count == 0 || _device is null)
            return;
        _remapper = await HoldRemapper.StartAsync(_device, platform.Keys, map);
        _remapper.Failed += error => Log.Write($"Key injection failed: {error.Message}");
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

        if (device.Controls.ParseHeld(e) is not { } now)
            return;
        foreach (var cid in now.Except(_held))
            Published?.Invoke("button", new ButtonEvent(ButtonId(cid), cid, true));
        foreach (var cid in _held.Except(now))
            Published?.Invoke("button", new ButtonEvent(ButtonId(cid), cid, false));
        _held = [.. now];
    }

    private async Task ReapplyAfterReconnectAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_remapper is not null)
                await _remapper.DivertAsync();
            Log.Write("The mouse asked for reconfiguration; diverted mapped buttons again");
        }
        catch (Exception error)
        {
            Log.Write($"Reapplying mappings failed: {error.Message}");
        }
        finally
        {
            _gate.Release();
        }
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

        var shared = _remapper?.SharedControls.ToHashSet() ?? [];
        var mapped = _settings.Mappings.Select(m => m.Button).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var buttons = new List<ButtonState>();
        foreach (var control in await mx.Controls.GetControlsAsync())
        {
            if (ButtonId(control.Cid) is not { } id)
                continue;
            bool elsewhere = mapped.Contains(id) ? shared.Contains(control.Cid) : control.Reporting.Diverted;
            buttons.Add(new ButtonState(id, control.Name, control.Cid, elsewhere));
        }

        return new DeviceState(
            name, $"{mx.Interface.ProductId:X4}", main?.ToString() ?? "",
            new BatteryState(battery.Percent, battery.Charging),
            new DpiState(dpi, defaultDpi, range.Min, range.Max, range.Step),
            new SmartShiftSettings(shift.Ratchet, shift.AutoDisengage, shift.Torque, shiftCaps.TunableTorque,
                shiftCaps.DefaultAutoDisengage, shiftCaps.DefaultTorque),
            new HapticsState(haptic.Enabled, haptic.Level, waveforms.Select(w => w.ToString()).ToArray()),
            force is { } f ? new ForceState(f.Current, f.Default, f.Min, f.Max) : null,
            buttons);
    }

    private void PublishState() => Published?.Invoke("state", State);

    /// <summary>Handles one client request and returns its result.</summary>
    public async Task<object?> HandleAsync(string method, JsonElement args)
    {
        if (method == "getState")
            return State;
        if (method == "setMappings")
            return await SetMappingsAsync(args.GetProperty("mappings").Deserialize(AgentJson.Default.ButtonMappingArray) ?? []);

        await _gate.WaitAsync();
        try
        {
            var mx = _device ?? throw new InvalidOperationException("The mouse isn't connected.");
            var current = _deviceState!;
            // Each write rereads only what it changed, so a slider's commit comes back quickly.
            switch (method)
            {
                case "setDpi":
                    await mx.Pointer.SetDpiAsync(args.GetProperty("dpi").GetInt32());
                    var (dpi, _) = await mx.Pointer.GetDpiAsync();
                    _deviceState = current with { Dpi = current.Dpi with { Current = dpi } };
                    break;
                case "setSmartShift":
                    await mx.Wheels.SetSmartShiftAsync(new SmartShiftState(
                        args.GetProperty("ratchet").GetBoolean(),
                        (byte)args.GetProperty("autoDisengage").GetInt32(),
                        (byte)args.GetProperty("torque").GetInt32()));
                    var shift = await mx.Wheels.GetSmartShiftAsync();
                    _deviceState = current with
                    {
                        SmartShift = current.SmartShift with { Ratchet = shift.Ratchet, AutoDisengage = shift.AutoDisengage, Torque = shift.Torque },
                    };
                    break;
                case "setHapticLevel":
                    await mx.Haptics.SetLevelAsync(args.GetProperty("level").GetInt32());
                    var haptic = await mx.Haptics.GetLevelAsync();
                    _deviceState = current with { Haptics = current.Haptics with { Enabled = haptic.Enabled, Level = haptic.Level } };
                    break;
                case "playHaptic":
                    await mx.Haptics.PlayAsync(Enum.Parse<HapticWaveform>(args.GetProperty("waveform").GetString()!));
                    return null;
                case "setForce":
                    await mx.Haptics.SetForceAsync(0, args.GetProperty("force").GetInt32());
                    var force = (await mx.Haptics.GetForceButtonsAsync())[0];
                    _deviceState = current with { ForceButton = current.ForceButton! with { Current = force.Current } };
                    break;
                default:
                    throw new ArgumentException($"Unknown method '{method}'");
            }
        }
        finally
        {
            _gate.Release();
        }
        PublishState();
        return State;
    }

    private async Task<AgentState> SetMappingsAsync(IReadOnlyList<ButtonMapping> mappings)
    {
        foreach (var m in mappings)
        {
            if (!ReprogControls.KnownButtons.ContainsKey(m.Button))
                throw new ArgumentException($"Unknown button '{m.Button}'");
            if (!platform.Keys.Supports(m.Key))
                throw new ArgumentException($"This platform can't send the key '{m.Key}'");
        }
        await _gate.WaitAsync();
        try
        {
            _settings = _settings with { Mappings = mappings.DistinctBy(m => m.Button.ToLowerInvariant()).ToArray() };
            store.Save(_settings);
            if (_device is not null)
            {
                await ApplyMappingsAsync();
                _deviceState = await ReadDeviceAsync(_device);
            }
        }
        finally
        {
            _gate.Release();
        }
        PublishState();
        return State;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await DropDeviceAsync(null);
        }
        finally
        {
            _gate.Release();
        }
    }
}
