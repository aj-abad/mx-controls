using System.Diagnostics;
using System.Runtime.Versioning;
using MxControls.Actions;

namespace MxControls.Platform.MacOS;

/// <summary>Apps by bundle path from the folders Finder shows as Applications; everything opens through <c>open</c>.</summary>
[SupportedOSPlatform("macos")]
internal sealed class MacApps : IApps
{
    private static readonly string[] Folders =
    [
        "/Applications", "/Applications/Utilities", "/System/Applications", "/System/Applications/Utilities",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications"),
    ];

    public IReadOnlyList<AppInfo> Installed()
    {
        var apps = new Dictionary<string, AppInfo>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var folder in Folders)
        {
            if (!Directory.Exists(folder))
                continue;
            foreach (var bundle in Directory.EnumerateDirectories(folder, "*.app"))
                apps.TryAdd(Path.GetFileNameWithoutExtension(bundle), new AppInfo(bundle, Path.GetFileNameWithoutExtension(bundle)));
        }
        return [.. apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public void Open(string target) =>
        Run("/usr/bin/open", OpenTarget.IsApp(target) ? target[OpenTarget.AppPrefix.Length..] : Expand(target));

    internal static void Run(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program) { UseShellExecute = false };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start);
    }

    private static string Expand(string path) =>
        path.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..])
            : path;
}

/// <summary>Shortcuts through the <c>shortcuts</c> command, which macOS 12 and later have.</summary>
[SupportedOSPlatform("macos")]
internal sealed class MacShortcuts : IShortcuts
{
    public const string Command = "/usr/bin/shortcuts";

    public IReadOnlyList<ShortcutInfo> List()
    {
        var start = new ProcessStartInfo(Command) { UseShellExecute = false, RedirectStandardOutput = true };
        start.ArgumentList.Add("list");
        start.ArgumentList.Add("--show-identifiers");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10_000);
        var shortcuts = new List<ShortcutInfo>();
        // Each line is "Name (IDENTIFIER)"; a name can hold parentheses of its own, so the identifier is the last pair.
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int open = line.LastIndexOf(" (", StringComparison.Ordinal);
            if (open > 0 && line.EndsWith(')'))
                shortcuts.Add(new ShortcutInfo(line[(open + 2)..^1], line[..open]));
        }
        return [.. shortcuts.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    // The command waits for the Shortcut to finish, so it's started and left to run.
    public void Run(string id) => MacApps.Run(Command, "run", id);
}
