using System.Text.Json;

namespace MxControls.Agent;

public sealed record AgentSettings(IReadOnlyList<ButtonMapping> Mappings)
{
    public static readonly AgentSettings Empty = new([]);
}

/// <summary>Settings and logs in the user's application data folder.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Indented = new(Protocol.Json) { WriteIndented = true };

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
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize(File.ReadAllText(SettingsPath), AgentJson.Default.AgentSettings) ?? AgentSettings.Empty
                : AgentSettings.Empty;
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
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Indented.GetTypeInfo(typeof(AgentSettings))));
        File.Move(temporary, SettingsPath, overwrite: true);
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
                File.AppendAllText(_path, line + Environment.NewLine);
        }
    }
}
