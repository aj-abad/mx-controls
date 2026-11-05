using System.Runtime.Versioning;
using System.Security;

namespace MxControls.Platform.MacOS;

/// <summary>
/// A per-user LaunchAgent. launchd reads the file at the next login, so nothing is loaded now: the running agent is
/// already up, and loading the job would start a second copy. KeepAlive restarts it only after an unsuccessful exit.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacLoginItem : ILoginItem
{
    private static string PathFor(string name)
    {
        var slug = name.ToLowerInvariant().Replace(' ', '-');
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents", $"app.mxcontrols.{slug}.plist");
    }

    public bool IsEnabled(string name) => File.Exists(PathFor(name));

    public void Enable(string name, string executable)
    {
        var path = PathFor(name);
        var contents = Plist(Path.GetFileNameWithoutExtension(path), executable);
        if (File.Exists(path) && File.ReadAllText(path) == contents)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    public void Disable(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path))
            File.Delete(path);
    }

    private static string Plist(string label, string executable) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>Label</key>
          <string>{SecurityElement.Escape(label)}</string>
          <key>ProgramArguments</key>
          <array>
            <string>{SecurityElement.Escape(executable)}</string>
          </array>
          <key>RunAtLoad</key>
          <true/>
          <key>KeepAlive</key>
          <dict>
            <key>SuccessfulExit</key>
            <false/>
          </dict>
          <key>LimitLoadToSessionType</key>
          <string>Aqua</string>
          <key>ProcessType</key>
          <string>Interactive</string>
        </dict>
        </plist>

        """;
}
