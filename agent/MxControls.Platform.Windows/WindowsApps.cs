using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using MxControls.Actions;

namespace MxControls.Platform.Windows;

/// <summary>
/// Apps as the Start menu lists them, Store apps included, each by its app user model ID or its shortcut's target;
/// Explorer opens either through <c>shell:AppsFolder</c>. Files, folders and links open as a double-click would.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsApps : IApps
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    private readonly object _lock = new();
    private IReadOnlyList<AppInfo>? _cached;
    private DateTime _cachedAt;

    public IReadOnlyList<AppInfo> Installed()
    {
        lock (_lock)
        {
            if (_cached is not null && DateTime.UtcNow - _cachedAt < CacheFor)
                return _cached;
            _cached = Read();
            _cachedAt = DateTime.UtcNow;
            return _cached;
        }
    }

    public void Open(string target)
    {
        string app = OpenTarget.IsApp(target) ? target[OpenTarget.AppPrefix.Length..] : "";
        // Apps without an app model ID are listed by their executable's path, which opens directly.
        var start = app.Length > 0 && !File.Exists(app)
            ? new ProcessStartInfo("explorer.exe", $@"shell:AppsFolder\{app}")
            : new ProcessStartInfo(Environment.ExpandEnvironmentVariables(app.Length > 0 ? app : target)) { UseShellExecute = true };
        using var process = Process.Start(start);
    }

    // Get-StartApps reads the same list the Start menu shows, which only the shell's COM interfaces otherwise reach.
    private static IReadOnlyList<AppInfo> Read()
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command",
            "[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-StartApps | Select-Object Name,AppID | ConvertTo-Json -Compress" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10_000);
        if (string.IsNullOrWhiteSpace(output))
            return [];

        var apps = new Dictionary<string, AppInfo>(StringComparer.CurrentCultureIgnoreCase);
        using var json = JsonDocument.Parse(output);
        var list = json.RootElement.ValueKind == JsonValueKind.Array ? json.RootElement.EnumerateArray().ToArray() : [json.RootElement];
        foreach (var entry in list)
        {
            if (entry.GetProperty("Name").GetString() is not { Length: > 0 } name || entry.GetProperty("AppID").GetString() is not { Length: > 0 } id)
                continue;
            // Uninstallers and help files sit in the Start menu beside the apps, and nobody opens those from a mouse.
            if (name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase) || id.EndsWith(".url", StringComparison.OrdinalIgnoreCase)
                || id.EndsWith(".chm", StringComparison.OrdinalIgnoreCase) || id.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                continue;
            apps.TryAdd(name, new AppInfo(id, name));
        }
        return [.. apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }
}
