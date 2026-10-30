using System.Text.Json;
using System.Text.Json.Serialization;
using MxControls.Actions;
using MxControls.Features;

namespace MxControls.Agent;

/// <summary>
/// The agent's wire format: one JSON object per line. Clients send <c>{"id", "method", "params"}</c> and get
/// <c>{"id", "result"}</c> or <c>{"id", "error"}</c>; the agent also pushes <c>{"event", "data"}</c> at any time.
/// </summary>
public static class Protocol
{
    public const string PipeName = "mx-controls-agent";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { TypeInfoResolver = AgentJson.Default };
}

/// <param name="Bindings">The active profile's bindings.</param>
/// <param name="ThumbWheel">The active profile's thumb wheel settings.</param>
/// <param name="Revision">Goes up with every change, so a client can tell an older state from a newer one.</param>
public sealed record AgentState(
    bool Connected, DeviceState? Device, IReadOnlyList<ButtonBinding> Bindings, ThumbWheelSettings ThumbWheel,
    IReadOnlyList<ProfileSummary> Profiles, string ActiveProfile, bool StartAtLogin, bool OptionsPlusRunning, string? Error,
    long Revision);

public sealed record ProfileSummary(string Id, string Name);

public sealed record DeviceState(
    string Name, string ProductId, string Firmware, BatteryState Battery, DpiState Dpi, SmartShiftSettings SmartShift,
    HapticsState Haptics, ForceState? ForceButton, IReadOnlyList<ButtonState> Buttons, ThumbWheelDevice? ThumbWheel, HostsInfo? Hosts);

public sealed record BatteryState(int Percent, bool Charging);

public sealed record DpiState(int Current, int Default, int Min, int Max, int Step);

public sealed record SmartShiftSettings(bool Ratchet, int AutoDisengage, int Torque, bool TunableTorque, int DefaultAutoDisengage, int DefaultTorque);

public sealed record HapticsState(bool Enabled, int Level, IReadOnlyList<string> Waveforms);

public sealed record ForceState(int Current, int Default, int Min, int Max);

/// <param name="DivertedElsewhere">Another app, most likely Logi Options+, had diverted the wheel before us.</param>
public sealed record ThumbWheelDevice(bool DivertedElsewhere);

/// <param name="DivertedElsewhere">Another app, most likely Logi Options+, had diverted it before us and also acts on it.</param>
public sealed record ButtonState(string Id, string Name, int Cid, bool DivertedElsewhere);

public sealed record ButtonEvent(string? Button, int Cid, bool Down);

/// <param name="Direction">"click", "up", "down", "left" or "right".</param>
public sealed record GestureEvent(string? Button, string Direction);

/// <param name="Code">The key's <c>KeyboardEvent.code</c>, or null for a key the agent can't send.</param>
public sealed record KeyEvent(string? Code, bool Down);

/// <summary>Sent when recording stops without the client asking, after a while with no keys.</summary>
public sealed record KeyCaptureEvent(bool Active);

/// <summary>Source-generated serializers, so the agent runs without reflection under NativeAOT.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(AgentState))]
[JsonSerializable(typeof(ButtonEvent))]
[JsonSerializable(typeof(GestureEvent))]
[JsonSerializable(typeof(KeyEvent))]
[JsonSerializable(typeof(KeyCaptureEvent))]
[JsonSerializable(typeof(AgentSettings))]
[JsonSerializable(typeof(ButtonBinding[]))]
[JsonSerializable(typeof(ThumbWheelSettings))]
internal sealed partial class AgentJson : JsonSerializerContext;
