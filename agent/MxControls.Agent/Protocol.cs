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
/// <param name="GesturePresets">Every preset: the built-in ones, as edited if they have been, then the user's own.</param>
/// <param name="BuiltInPresets">The built-in presets as they come, so a client can tell an edited one and reset it.</param>
/// <param name="PresetUses">Which buttons follow which preset, in every profile.</param>
/// <param name="MacroUses">Which buttons, thumb wheels and gesture presets run which macro.</param>
public sealed record AgentState(
    bool Connected, DeviceState? Device, IReadOnlyList<ButtonBinding> Bindings, ThumbWheelSettings ThumbWheel,
    IReadOnlyList<ProfileSummary> Profiles, string ActiveProfile, bool StartAtLogin, bool OptionsPlusRunning, string? Error,
    long Revision, int MultiClickMs, IReadOnlyList<GesturePreset> GesturePresets, IReadOnlyList<GesturePreset> BuiltInPresets,
    IReadOnlyList<PresetUse> PresetUses, IReadOnlyList<Macro> Macros, IReadOnlyList<MacroUse> MacroUses);

public sealed record PresetUse(string Preset, string Profile, string Button);

/// <summary>Where a macro runs: a button or the thumb wheel in a profile, or a step of a gesture preset.</summary>
public sealed record MacroUse(string Macro, string? Profile = null, string? Button = null, string? Preset = null);

public sealed record ProfileSummary(string Id, string Name, string? Icon);

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

/// <param name="Direction">The last of <paramref name="Path"/>, or null once a path is backed out of.</param>
/// <param name="Path">"up", "down", "left" or "right", one per step; clicks come as <see cref="ClickEvent"/>.</param>
/// <param name="Phase">"fire" when the path ran, "step" when it changed without running.</param>
public sealed record GestureEvent(string? Button, string? Direction, IReadOnlyList<string> Path, string Phase);

/// <param name="Count">1, 2 or 3 for a single, double or triple click.</param>
public sealed record ClickEvent(string? Button, int Count);

/// <param name="Code">The key's <c>KeyboardEvent.code</c>, or null for a key the agent can't send.</param>
public sealed record KeyEvent(string? Code, bool Down);

/// <summary>Sent when recording stops without the client asking, after a while with no keys.</summary>
public sealed record KeyCaptureEvent(bool Active);

/// <summary>Source-generated serializers, so the agent runs without reflection under NativeAOT.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(AgentState))]
[JsonSerializable(typeof(ButtonEvent))]
[JsonSerializable(typeof(GestureEvent))]
[JsonSerializable(typeof(ClickEvent))]
[JsonSerializable(typeof(KeyEvent))]
[JsonSerializable(typeof(KeyCaptureEvent))]
[JsonSerializable(typeof(AgentSettings))]
[JsonSerializable(typeof(ButtonBinding[]))]
[JsonSerializable(typeof(GesturePreset))]
[JsonSerializable(typeof(Macro))]
[JsonSerializable(typeof(ThumbWheelSettings))]
internal sealed partial class AgentJson : JsonSerializerContext;
