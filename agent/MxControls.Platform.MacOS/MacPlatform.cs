using System.Diagnostics;
using System.Runtime.Versioning;
using MxControls.Actions;

namespace MxControls.Platform.MacOS;

[SupportedOSPlatform("macos")]
public sealed class MacPlatform : IPlatform
{
    private const string MissionControl = "/System/Applications/Mission Control.app/Contents/MacOS/Mission Control";
    private const string ShowDesktopArgument = "1";

    private readonly MacInput _input = new();

    public IHidBackend Hid { get; } = new MacHid();
    public IInputInjector Input => _input;
    public ILoginItem LoginItem { get; } = new MacLoginItem();
    public IKeyCapture? KeyCapture { get; } = new MacKeyCapture();
    public IKeyWatch? KeyWatch { get; } = new MacKeyWatch();
    public IPrograms? Programs { get; } = new MacPrograms();
    public IApps? Apps { get; } = new MacApps();
    public IShortcuts? Shortcuts { get; } = File.Exists(MacShortcuts.Command) ? new MacShortcuts() : null;

    private const string AppExposeArgument = "2";

    // Launchpad became Apps in macOS 26.
    private static readonly string? LaunchpadApp =
        new[] { "/System/Applications/Apps.app", "/System/Applications/Launchpad.app" }.FirstOrDefault(Directory.Exists);

    // macOS's standard shortcuts for the actions that have one; the rest are done in RunSystem.
    private static readonly Dictionary<string, string[]> Chords = new()
    {
        [SystemActions.SwitchApps] = ["MetaLeft", "Tab"],
        [SystemActions.Search] = ["MetaLeft", "Space"],
        [SystemActions.Screenshot] = ["MetaLeft", "ShiftLeft", "Digit5"],
        [SystemActions.Emoji] = ["ControlLeft", "MetaLeft", "Space"],
        [SystemActions.LockScreen] = ["ControlLeft", "MetaLeft", "KeyQ"],
        [SystemActions.MinimizeWindow] = ["MetaLeft", "KeyM"],
        [SystemActions.FullScreen] = ["ControlLeft", "MetaLeft", "KeyF"],
        [SystemActions.CloseWindow] = ["MetaLeft", "KeyW"],
        [SystemActions.QuitApp] = ["MetaLeft", "KeyQ"],
        [SystemActions.LookUp] = ["ControlLeft", "MetaLeft", "KeyD"],
        [SystemActions.BrightnessUp] = ["BrightnessUp"],
        [SystemActions.BrightnessDown] = ["BrightnessDown"],
    };

    // Opened by bundle ID, which stays put when an app moves or is renamed.
    private static readonly Dictionary<string, string> Bundles = new()
    {
        [SystemActions.FileManager] = "com.apple.finder",
        [SystemActions.Settings] = "com.apple.systempreferences",
        [SystemActions.Calculator] = "com.apple.calculator",
    };

    public bool SupportsSystem(string name) =>
        Chords.ContainsKey(name) || Bundles.ContainsKey(name)
            || name is SystemActions.TaskView or SystemActions.AppExpose or SystemActions.ShowDesktop or SystemActions.ZoomIn or SystemActions.ZoomOut
            || (name == SystemActions.Launchpad && LaunchpadApp is not null);

    public void RunSystem(string name)
    {
        if (Chords.TryGetValue(name, out var chord))
        {
            Tap(chord);
            return;
        }
        if (Bundles.TryGetValue(name, out var bundle))
        {
            MacApps.Run("/usr/bin/open", "-b", bundle);
            return;
        }
        switch (name)
        {
            // By the app rather than its shortcuts, which a person can change or turn off.
            case SystemActions.TaskView:
                Start();
                break;
            case SystemActions.AppExpose:
                Start(AppExposeArgument);
                break;
            case SystemActions.ShowDesktop:
                Start(ShowDesktopArgument);
                break;
            case SystemActions.Launchpad:
                MacApps.Run("/usr/bin/open", LaunchpadApp!);
                break;
            // Cmd with the wheel doesn't zoom on a Mac, and Ctrl with it zooms the whole screen if that's on.
            case SystemActions.ZoomIn:
                Tap("MetaLeft", "Equal");
                break;
            case SystemActions.ZoomOut:
                Tap("MetaLeft", "Minus");
                break;
            default:
                throw new ArgumentException($"Unknown system action '{name}'", nameof(name));
        }
    }

    public bool IsOptionsPlusRunning()
    {
        // By path: Process.GetProcessesByName sees macOS's short names, which cut this one off.
        foreach (var pid in Proc.All())
        {
            if (Proc.PathOf(pid) is { } path && Path.GetFileName(path) == "logioptionsplus_agent")
                return true;
        }
        return false;
    }

    private static void Start(params string[] arguments)
    {
        var start = new ProcessStartInfo(MissionControl) { UseShellExecute = false };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start);
    }

    private void Tap(params string[] keys)
    {
        foreach (var key in keys)
            _input.Down(key);
        foreach (var key in keys.Reverse())
            _input.Up(key);
    }
}
