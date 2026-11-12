using System.Text.Json;
using System.Text.Json.Serialization;
using MxControls.Actions;
using MxControls.Features;
using MxControls.Platform;

namespace MxControls.Agent;

/// <summary>
/// The agent's wire format: one JSON object per line. Clients send <c>{"id", "method", "params"}</c> and get
/// <c>{"id", "result"}</c> or <c>{"id", "error"}</c>; the agent also pushes <c>{"event", "data"}</c> at any time.
/// </summary>
public static class Protocol
{
    public const string PipeName = "mx-controls-agent";

    /// <summary>Rose folders nest as deep as people like, each a few levels of JSON, so the usual limit of 64 is raised.</summary>
    public const int MaxDepth = 512;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { TypeInfoResolver = AgentJson.Default, MaxDepth = MaxDepth };

    public static readonly JsonDocumentOptions Document = new() { MaxDepth = MaxDepth };
}

/// <param name="Bindings">The running profile's main bindings.</param>
/// <param name="ThumbWheel">The running profile's main thumb wheel settings.</param>
/// <param name="ActiveProfile">The profile running now: the one the program in front follows, or else <paramref name="SelectedProfile"/>.</param>
/// <param name="Revision">Goes up with every change, so a client can tell an older state from a newer one.</param>
/// <param name="GesturePresets">Every preset: the built-in ones, as edited if they have been, then the user's own.</param>
/// <param name="BuiltInPresets">The built-in presets as they come, so a client can tell an edited one and reset it.</param>
/// <param name="PresetUses">Which buttons follow which preset, in every profile.</param>
/// <param name="MacroUses">Which buttons, thumb wheels, gesture presets and rose menus run which macro.</param>
/// <param name="RoseMenus">The library's rose menus, which buttons show by ID.</param>
/// <param name="RoseUses">Which buttons show which rose menu, in every profile.</param>
/// <param name="BaseProfile">The profile whose buttons others can follow with an <c>inherit</c> binding.</param>
/// <param name="SelectedProfile">The profile chosen by hand or a <c>profile</c> action; it never follows programs.</param>
/// <param name="LayerActive">Whether the running profile's second layer is on.</param>
/// <param name="FollowsPrograms">Whether this platform can tell which program is in front, so profiles can follow programs.</param>
/// <param name="AgentHash">The SHA-256 of the running agent's executable, so an app can tell an agent from another build.</param>
/// <param name="HostShortcuts">Keys that send the mouse to an Easy-Switch channel.</param>
/// <param name="SystemActions">The named system actions this platform runs, from <see cref="Actions.SystemActions.All"/>.</param>
/// <param name="OpensApps">Whether <c>open</c> actions work here.</param>
/// <param name="RunsShortcuts">Whether there's a Shortcuts app to run, which is only on a Mac.</param>
public sealed record AgentState(
    bool Connected, DeviceState? Device, IReadOnlyList<ButtonBinding> Bindings, ThumbWheelSettings ThumbWheel,
    IReadOnlyList<ProfileSummary> Profiles, string ActiveProfile, bool StartAtLogin, bool OptionsPlusRunning, string? Error,
    long Revision, int MultiClickMs, IReadOnlyList<GesturePreset> GesturePresets, IReadOnlyList<GesturePreset> BuiltInPresets,
    IReadOnlyList<PresetUse> PresetUses, IReadOnlyList<Macro> Macros, IReadOnlyList<MacroUse> MacroUses,
    string SelectedProfile, bool LayerActive, bool FollowsPrograms, string? AgentHash,
    IReadOnlyList<HostShortcut> HostShortcuts, IReadOnlyList<string> SystemActions, bool OpensApps, bool RunsShortcuts,
    IReadOnlyList<RoseMenu> RoseMenus, IReadOnlyList<RoseUse> RoseUses, string BaseProfile);

/// <param name="Layer">Whether the button is bound in the profile's second layer rather than its main one.</param>
public sealed record PresetUse(string Preset, string Profile, string Button, bool Layer = false);

/// <param name="Layer">Whether the button shows it in the profile's second layer rather than its main one.</param>
public sealed record RoseUse(string Menu, string Profile, string Button, bool Layer = false);

/// <summary>
/// Where a macro runs: a button or the thumb wheel in a profile, a direction of a gesture preset, or an option of a
/// rose menu.
/// </summary>
/// <param name="Layer">Whether it's in the profile's second layer rather than its main one.</param>
public sealed record MacroUse(
    string Macro, string? Profile = null, string? Button = null, string? Preset = null, bool Layer = false, string? Rose = null);

/// <summary>Everything a profile holds, so a client can show and edit one that isn't running.</summary>
/// <param name="Programs">The programs that run it while they're in front; empty for a profile chosen by hand.</param>
public sealed record ProfileSummary(
    string Id, string Name, string? Icon, IReadOnlyList<string> Programs, IReadOnlyList<ButtonBinding> Bindings,
    ThumbWheelSettings ThumbWheel, LayerSummary Layer);

/// <param name="ThumbWheel">Null where the second layer keeps the main layer's settings.</param>
public sealed record LayerSummary(IReadOnlyList<ButtonBinding> Bindings, ThumbWheelSettings? ThumbWheel);

public sealed record DeviceState(
    string Name, string ProductId, string Firmware, BatteryState Battery, DpiState Dpi, SmartShiftSettings SmartShift,
    HapticsState Haptics, ForceState? ForceButton, IReadOnlyList<ButtonState> Buttons, ThumbWheelDevice? ThumbWheel, HostsInfo? Hosts,
    bool? ScrollInverted);

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

/// <param name="Direction">"up", "down", "left" or "right", as it ran; clicks come as <see cref="ClickEvent"/>.</param>
public sealed record GestureEvent(string? Button, string Direction);

/// <param name="Option">The highlighted option's index in the ring showing, or null back in its middle.</param>
/// <param name="Ran">Whether the option ran, as the button came up on it.</param>
/// <param name="Path">The folders open, by index from the rose's own ring; empty there.</param>
public sealed record RoseEvent(string? Button, int? Option, bool Ran, IReadOnlyList<int> Path);

/// <param name="Count">1, 2 or 3 for a single, double or triple click.</param>
public sealed record ClickEvent(string? Button, int Count);

/// <param name="Code">The key's <c>KeyboardEvent.code</c>, or null for a key the agent can't send.</param>
public sealed record KeyEvent(string? Code, bool Down);

/// <summary>Sent when recording stops without the client asking, after a while with no keys.</summary>
public sealed record KeyCaptureEvent(bool Active);

/// <summary>Source-generated serializers, so the agent runs without reflection under NativeAOT.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, MaxDepth = Protocol.MaxDepth)]
[JsonSerializable(typeof(AgentState))]
[JsonSerializable(typeof(ButtonEvent))]
[JsonSerializable(typeof(GestureEvent))]
[JsonSerializable(typeof(ClickEvent))]
[JsonSerializable(typeof(RoseEvent))]
[JsonSerializable(typeof(KeyEvent))]
[JsonSerializable(typeof(KeyCaptureEvent))]
[JsonSerializable(typeof(AgentSettings))]
[JsonSerializable(typeof(ButtonBinding[]))]
[JsonSerializable(typeof(GesturePreset))]
[JsonSerializable(typeof(RoseMenu))]
[JsonSerializable(typeof(Macro))]
[JsonSerializable(typeof(ThumbWheelSettings))]
[JsonSerializable(typeof(ProgramInfo[]))]
[JsonSerializable(typeof(AppInfo[]))]
[JsonSerializable(typeof(ShortcutInfo[]))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(Dictionary<string, string?>))]
internal sealed partial class AgentJson : JsonSerializerContext;
