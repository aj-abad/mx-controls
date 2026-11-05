using System.Diagnostics;
using System.Runtime.Versioning;
using MxControls.Actions;
using MxControls.Platform;

namespace MxControls.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsPlatform : IPlatform
{
    private readonly WindowsInput _input = new();

    public IHidBackend Hid { get; } = new WindowsHid();
    public IInputInjector Input => _input;
    public ILoginItem LoginItem { get; } = new WindowsLoginItem();
    public IKeyCapture? KeyCapture { get; } = new WindowsKeyCapture();
    public IKeyWatch? KeyWatch { get; } = new WindowsKeyWatch();
    public IPrograms? Programs { get; } = new WindowsPrograms();

    public bool SupportsSystem(string name) =>
        name is SystemActions.TaskView or SystemActions.ShowDesktop or SystemActions.ZoomIn or SystemActions.ZoomOut;

    public void RunSystem(string name)
    {
        switch (name)
        {
            case SystemActions.TaskView:
                Tap("MetaLeft", "Tab");
                break;
            case SystemActions.ShowDesktop:
                Tap("MetaLeft", "KeyD");
                break;
            // Ctrl with the wheel zooms in browsers, editors and DAWs alike, where Ctrl+= doesn't.
            case SystemActions.ZoomIn or SystemActions.ZoomOut:
                _input.Down("ControlLeft");
                _input.Scroll(name == SystemActions.ZoomIn ? 1 : -1);
                _input.Up("ControlLeft");
                break;
            default:
                throw new ArgumentException($"Unknown system action '{name}'", nameof(name));
        }
    }

    public bool IsOptionsPlusRunning()
    {
        var agents = Process.GetProcessesByName("logioptionsplus_agent");
        foreach (var process in agents)
            process.Dispose();
        return agents.Length > 0;
    }

    private void Tap(params string[] keys)
    {
        foreach (var key in keys)
            _input.Down(key);
        foreach (var key in keys.Reverse())
            _input.Up(key);
    }
}
