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
    public IPrograms? Programs { get; } = new MacPrograms();

    public bool SupportsSystem(string name) =>
        name is SystemActions.TaskView or SystemActions.ShowDesktop or SystemActions.ZoomIn or SystemActions.ZoomOut;

    public void RunSystem(string name)
    {
        switch (name)
        {
            // By the app rather than its shortcuts, which a person can change or turn off.
            case SystemActions.TaskView:
                Start();
                break;
            case SystemActions.ShowDesktop:
                Start(ShowDesktopArgument);
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
