using System.Text.Json;
using System.Text.Json.Serialization;

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

public sealed record AgentState(
    bool Connected, DeviceState? Device, IReadOnlyList<ButtonMapping> Mappings, bool OptionsPlusRunning, string? Error);

public sealed record DeviceState(
    string Name, string ProductId, string Firmware, BatteryState Battery, DpiState Dpi, SmartShiftSettings SmartShift,
    HapticsState Haptics, ForceState? ForceButton, IReadOnlyList<ButtonState> Buttons);

public sealed record BatteryState(int Percent, bool Charging);

public sealed record DpiState(int Current, int Default, int Min, int Max, int Step);

public sealed record SmartShiftSettings(bool Ratchet, int AutoDisengage, int Torque, bool TunableTorque, int DefaultAutoDisengage, int DefaultTorque);

public sealed record HapticsState(bool Enabled, int Level, IReadOnlyList<string> Waveforms);

public sealed record ForceState(int Current, int Default, int Min, int Max);

/// <param name="DivertedElsewhere">Another app, most likely Logi Options+, had diverted it before us and also acts on it.</param>
public sealed record ButtonState(string Id, string Name, int Cid, bool DivertedElsewhere);

/// <param name="Button">A button ID such as "middle".</param>
/// <param name="Key">A <c>KeyboardEvent.code</c> such as "ShiftLeft".</param>
public sealed record ButtonMapping(string Button, string Key);

public sealed record ButtonEvent(string? Button, int Cid, bool Down);

/// <summary>Source-generated serializers, so the agent runs without reflection under NativeAOT.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(AgentState))]
[JsonSerializable(typeof(ButtonEvent))]
[JsonSerializable(typeof(AgentSettings))]
[JsonSerializable(typeof(ButtonMapping[]))]
internal sealed partial class AgentJson : JsonSerializerContext;
