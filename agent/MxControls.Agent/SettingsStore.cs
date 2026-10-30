using System.Text.Json;
using System.Text.Json.Serialization;
using MxControls.Actions;

namespace MxControls.Agent;

/// <summary>A named set of button bindings and thumb wheel settings; one is active at a time.</summary>
/// <param name="Icon">A Phosphor icon name the app draws for the profile, or null for its initial.</param>
public sealed record Profile(
    string Id, string Name, IReadOnlyList<ButtonBinding>? Bindings = null, ThumbWheelSettings? ThumbWheel = null, string? Icon = null)
{
    public const string DefaultId = "default";
    public static readonly Profile Default = new(DefaultId, "Default", []);

    [JsonIgnore]
    public IReadOnlyList<ButtonBinding> AllBindings => Bindings ?? [];

    [JsonIgnore]
    public ThumbWheelSettings Wheel => ThumbWheel ?? ThumbWheelSettings.Default;

    public static string NewId() => Guid.NewGuid().ToString("N")[..8];
}

/// <param name="Bindings">Bindings from before profiles; read once and saved back as the default profile.</param>
/// <param name="Mappings">Held-key mappings from before bindings; read once and saved back the same way.</param>
/// <param name="MultiClickMs">How long a click waits for another; one setting for every button and profile.</param>
/// <param name="GesturePresets">
/// The user's own presets, and built-in ones they've edited, under the built-in's ID. They belong to no profile.
/// </param>
/// <param name="Macros">The saved macros, which <c>macro</c> actions run by ID. They belong to no profile.</param>
public sealed record AgentSettings(
    IReadOnlyList<ButtonBinding>? Bindings = null, bool StartAtLogin = true, IReadOnlyList<LegacyMapping>? Mappings = null,
    IReadOnlyList<Profile>? Profiles = null, string? ActiveProfile = null, int MultiClickMs = ClickCounter.DefaultWindowMs,
    IReadOnlyList<GesturePreset>? GesturePresets = null, IReadOnlyList<Macro>? Macros = null)
{
    public const int MinMultiClickMs = 150;
    public const int MaxMultiClickMs = 600;

    public static readonly AgentSettings Empty = new();

    [JsonIgnore]
    public IReadOnlyList<Profile> AllProfiles => Profiles is { Count: > 0 } profiles ? profiles : [Profile.Default];

    [JsonIgnore]
    public Profile Active => AllProfiles.FirstOrDefault(p => p.Id == ActiveProfile) ?? AllProfiles[0];

    [JsonIgnore]
    public IReadOnlyList<ButtonBinding> AllBindings => Active.AllBindings;

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

    /// <summary>The active profile's bindings as the engine runs them, with their presets' moves filled in.</summary>
    [JsonIgnore]
    public IEnumerable<ButtonBinding> ResolvedBindings => AllBindings.Select(b => b.Resolve(FindPreset));

    /// <summary>Replaces the active profile with what <paramref name="change"/> makes of it.</summary>
    public AgentSettings WithActive(Func<Profile, Profile> change)
    {
        var active = Active;
        return this with { Profiles = AllProfiles.Select(p => p.Id == active.Id ? change(p) : p).ToArray(), ActiveProfile = active.Id };
    }

    public AgentSettings Migrated()
    {
        var legacy = Bindings ?? Mappings?.Select(m => ButtonBinding.Hold(m.Button, m.Key)).ToArray();
        if (legacy is null)
            return MacroMigration.Migrated(PresetLibrary.Migrated(this));
        var profiles = Profiles is { Count: > 0 } existing ? existing : [Profile.Default with { Bindings = legacy }];
        return MacroMigration.Migrated(PresetLibrary.Migrated(
            this with { Bindings = null, Mappings = null, Profiles = profiles, ActiveProfile = ActiveProfile ?? profiles[0].Id }));
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
