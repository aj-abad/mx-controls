using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MxControls.Actions;
using MxControls.Platform;

namespace MxControls.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsPlatform : IPlatform
{
    private const int Minimize = 6, Maximize = 3, Restore = 9;

    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool LockWorkStation();
    [LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool ShowWindow(nint hwnd, int command);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsZoomed(nint hwnd);

    private readonly WindowsInput _input = new();

    public IHidBackend Hid { get; } = new WindowsHid();
    public IInputInjector Input => _input;
    public ILoginItem LoginItem { get; } = new WindowsLoginItem();
    public IKeyCapture? KeyCapture { get; } = new WindowsKeyCapture();
    public IKeyWatch? KeyWatch { get; } = new WindowsKeyWatch();
    public IPrograms? Programs { get; } = new WindowsPrograms();
    public IRoseOverlay? RoseOverlay { get; } = new WindowsRoseOverlay();
    public IApps? Apps { get; } = new WindowsApps();

    // What each named action presses; the rest are done in RunSystem.
    private static readonly Dictionary<string, string[]> Chords = new()
    {
        [SystemActions.TaskView] = ["MetaLeft", "Tab"],
        [SystemActions.ShowDesktop] = ["MetaLeft", "KeyD"],
        [SystemActions.SwitchApps] = ["AltLeft", "Tab"],
        [SystemActions.Search] = ["MetaLeft", "KeyS"],
        [SystemActions.Screenshot] = ["MetaLeft", "ShiftLeft", "KeyS"],
        [SystemActions.Emoji] = ["MetaLeft", "Period"],
        [SystemActions.Dictation] = ["MetaLeft", "KeyH"],
        [SystemActions.Notifications] = ["MetaLeft", "KeyN"],
        [SystemActions.FileManager] = ["MetaLeft", "KeyE"],
        [SystemActions.Settings] = ["MetaLeft", "KeyI"],
        [SystemActions.FullScreen] = ["F11"],
        [SystemActions.CloseWindow] = ["AltLeft", "F4"],
        // Windows 11's own mute for calls, which follows whichever app has the microphone.
        [SystemActions.MicMute] = ["MetaLeft", "AltLeft", "KeyK"],
    };

    public bool SupportsSystem(string name) =>
        Chords.ContainsKey(name) || name is SystemActions.ZoomIn or SystemActions.ZoomOut or SystemActions.LockScreen
            or SystemActions.Calculator or SystemActions.MinimizeWindow or SystemActions.MaximizeWindow;

    public void RunSystem(string name)
    {
        if (Chords.TryGetValue(name, out var chord))
        {
            Tap(chord);
            return;
        }
        switch (name)
        {
            // Win+L can't be typed by software; Windows keeps it for the secure desktop.
            case SystemActions.LockScreen:
                LockWorkStation();
                break;
            case SystemActions.Calculator:
                using (Process.Start(new ProcessStartInfo("calc.exe") { UseShellExecute = true })) { }
                break;
            // Win+Down restores a maximized window before it minimizes one, so the window is told directly.
            case SystemActions.MinimizeWindow:
                ShowWindow(GetForegroundWindow(), Minimize);
                break;
            case SystemActions.MaximizeWindow:
                nint window = GetForegroundWindow();
                ShowWindow(window, IsZoomed(window) ? Restore : Maximize);
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
