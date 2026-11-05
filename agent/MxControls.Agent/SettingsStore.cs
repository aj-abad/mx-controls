using System.Text.Json;
using System.Text.Json.Serialization;
using MxControls.Actions;

namespace MxControls.Agent;

/// <summary>
/// A named set of button bindings and thumb wheel settings, with a second layer over them. One runs at a time: the
/// profile bound to the program in front, or else the one chosen last among those bound to no program.
/// </summary>
/// <param name="Icon">A Phosphor icon name the app draws for the profile, or null for its initial.</param>
/// <param name="Layer">The second layer, which runs over the main one while a button holds or toggles it on.</param>
/// <param name="Programs">
/// Program IDs, such as "chrome.exe", that run this profile while they're in front. A profile with programs is
/// never chosen by hand or by a <c>profile</c> action.
/// </param>
public sealed record Profile(
    string Id, string Name, IReadOnlyList<ButtonBinding>? Bindings = null, ThumbWheelSettings? ThumbWheel = null, string? Icon = null,
    ProfileLayer? Layer = null, IReadOnlyList<string>? Programs = null)
{
    public const string DefaultId = "default";
    public static readonly Profile Default = new(DefaultId, "Default", []);

    [JsonIgnore]
    public IReadOnlyList<ButtonBinding> AllBindings => Bindings ?? [];

    [JsonIgnore]
    public ThumbWheelSettings Wheel => ThumbWheel ?? ThumbWheelSettings.Default;

    [JsonIgnore]
    public ProfileLayer SecondLayer => Layer ?? ProfileLayer.Empty;

    [JsonIgnore]
    public IReadOnlyList<string> AllPrograms => Programs ?? [];

    /// <summary>Whether it can be chosen by hand or by a <c>profile</c> action: it follows no programs.</summary>
    [JsonIgnore]
    public bool Switchable => AllPrograms.Count == 0;

    public bool Follows(string program) => AllPrograms.Any(p => string.Equals(p, program, StringComparison.OrdinalIgnoreCase));

    public static string NewId() => Guid.NewGuid().ToString("N")[..8];
}

/// <summary>
/// A profile's second layer. A button it leaves unbound runs its main binding, and a null thumb wheel keeps the main
/// layer's settings.
/// </summary>
public sealed record ProfileLayer(IReadOnlyList<ButtonBinding>? Bindings = null, ThumbWheelSettings? ThumbWheel = null)
{
    public static readonly ProfileLayer Empty = new();

    [JsonIgnore]
    public IReadOnlyList<ButtonBinding> AllBindings => Bindings ?? [];

    [JsonIgnore]
    public bool IsEmpty => AllBindings.Count == 0 && ThumbWheel is null;
}

/// <summary>Keys that send the mouse to an Easy-Switch channel, from anywhere on this computer.</summary>
/// <param name="Host">The channel, from 0.</param>
/// <param name="Keys">Modifiers then one key, as <c>KeyboardEvent.code</c> names.</param>
public sealed record HostShortcut(int Host, IReadOnlyList<string> Keys);

/// <param name="Bindings">Bindings from before profiles; read once and saved back as the default profile.</param>
/// <param name="Mappings">Held-key mappings from before bindings; read once and saved back the same way.</param>
/// <param name="MultiClickMs">How long a click waits for another; one setting for every button and profile.</param>
/// <param name="GesturePresets">
/// The user's own presets, and built-in ones they've edited, under the built-in's ID. They belong to no profile.
/// </param>
/// <param name="Macros">The saved macros, which <c>macro</c> actions run by ID. They belong to no profile.</param>
/// <param name="HostShortcuts">Keys for the Easy-Switch channels. They belong to no profile.</param>
public sealed record AgentSettings(
    IReadOnlyList<ButtonBinding>? Bindings = null, bool StartAtLogin = true, IReadOnlyList<LegacyMapping>? Mappings = null,
    IReadOnlyList<Profile>? Profiles = null, string? ActiveProfile = null, int MultiClickMs = ClickCounter.DefaultWindowMs,
    IReadOnlyList<GesturePreset>? GesturePresets = null, IReadOnlyList<Macro>? Macros = null, bool? ScrollInverted = null,
    IReadOnlyList<HostShortcut>? HostShortcuts = null)
{
    public const int MinMultiClickMs = 150;
    public const int MaxMultiClickMs = 600;

    public static readonly AgentSettings Empty = new();

    [JsonIgnore]
    public IReadOnlyList<Profile> AllProfiles => Profiles is { Count: > 0 } profiles ? profiles : [Profile.Default];

    /// <summary>
    /// The profile chosen by hand or by a <c>profile</c> action, which runs whenever the program in front has none
    /// of its own; the first profile without programs if that one is gone or has gained programs since.
    /// </summary>
    [JsonIgnore]
    public Profile Active =>
        AllProfiles.FirstOrDefault(p => p.Id == ActiveProfile && p.Switchable)
        ?? AllProfiles.FirstOrDefault(p => p.Switchable)
        ?? AllProfiles[0];

    [JsonIgnore]
    public IReadOnlyList<ButtonBinding> AllBindings => Active.AllBindings;

    /// <summary>The profile bound to <paramref name="program"/>, or null.</summary>
    public Profile? ProfileFor(string? program) => program is null ? null : AllProfiles.FirstOrDefault(p => p.Follows(program));

    /// <summary>What runs with <paramref name="program"/> in front: its own profile, or else <see cref="Active"/>.</summary>
    public Profile Running(string? program) => ProfileFor(program) ?? Active;

    public Profile? FindProfile(string id) => AllProfiles.FirstOrDefault(p => p.Id == id);

    /// <summary>The built-in presets, as edited if they have been, then the user's own.</summary>
    [JsonIgnore]
    public IReadOnlyList<GesturePreset> AllPresets
    {
        get
        {
            var stored = GesturePresets ?? [];
            return
            [
                .. PresetLibrary.BuiltIn.Select(b => stored.FirstOrDefault(p => p.Id == b.Id) ?? b),
                .. stored.Where(p => !PresetLibrary.IsBuiltIn(p.Id)),
            ];
        }
    }

    public GesturePreset? FindPreset(string id) => AllPresets.FirstOrDefault(p => p.Id == id);

    /// <summary>Bindings as the engine runs them, with their presets' moves filled in.</summary>
    public IEnumerable<ButtonBinding> Resolved(IEnumerable<ButtonBinding> bindings) => bindings.Select(b => b.Resolve(FindPreset));

    /// <summary>The active profile's bindings as the engine runs them.</summary>
    [JsonIgnore]
    public IEnumerable<ButtonBinding> ResolvedBindings => Resolved(AllBindings);

    /// <summary>Replaces the active profile with what <paramref name="change"/> makes of it.</summary>
    public AgentSettings WithActive(Func<Profile, Profile> change)
    {
        var active = Active;
        return WithProfile(active.Id, change) with { ActiveProfile = active.Id };
    }

    /// <summary>Replaces the profile <paramref name="id"/> with what <paramref name="change"/> makes of it.</summary>
    public AgentSettings WithProfile(string id, Func<Profile, Profile> change) =>
        this with { Profiles = AllProfiles.Select(p => p.Id == id ? change(p) : p).ToArray() };

    public AgentSettings Migrated()
    {
        var legacy = Bindings ?? Mappings?.Select(m => ButtonBinding.Hold(m.Button, m.Key)).ToArray();
        if (legacy is null)
            return PresetLibrary.Migrated(this);
        var profiles = Profiles is { Count: > 0 } existing ? existing : [Profile.Default with { Bindings = legacy }];
        return PresetLibrary.Migrated(
            this with { Bindings = null, Mappings = null, Profiles = profiles, ActiveProfile = ActiveProfile ?? profiles[0].Id });
    }
}

public sealed record LegacyMapping(string Button, string Key);

/// <summary>Settings and logs in the user's application data folder.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions File = new(Protocol.Json)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Folder { get; }
    private string SettingsPath => Path.Combine(Folder, "settings.json");

    public SettingsStore()
    {
        var root = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Folder = Path.Combine(root, "MxControls");
        Directory.CreateDirectory(Folder);
    }

    public AgentSettings Load()
    {
        try
        {
            if (!System.IO.File.Exists(SettingsPath))
                return AgentSettings.Empty;
            var settings = JsonSerializer.Deserialize(System.IO.File.ReadAllText(SettingsPath), AgentJson.Default.AgentSettings)
                ?? AgentSettings.Empty;
            var migrated = settings.Migrated();
            if (!ReferenceEquals(migrated, settings))
                Save(migrated);
            return migrated;
        }
        catch (JsonException error)
        {
            Log.Write($"Ignoring unreadable settings: {error.Message}");
            return AgentSettings.Empty;
        }
    }

    public void Save(AgentSettings settings)
    {
        var temporary = SettingsPath + ".tmp";
        System.IO.File.WriteAllText(temporary, JsonSerializer.Serialize(settings, File.GetTypeInfo(typeof(AgentSettings))));
        System.IO.File.Move(temporary, SettingsPath, overwrite: true);
    }
}

public static class Log
{
    private static readonly object Lock = new();
    private static string? _path;

    public static void Open(string folder)
    {
        var logs = Path.Combine(folder, "Logs");
        Directory.CreateDirectory(logs);
        _path = Path.Combine(logs, "agent.log");
    }

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}";
        lock (Lock)
        {
            Console.WriteLine(line);
            if (_path is not null)
                System.IO.File.AppendAllText(_path, line + Environment.NewLine);
        }
    }
}
