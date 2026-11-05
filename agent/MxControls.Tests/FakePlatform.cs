using MxControls.Actions;
using MxControls.Platform;

namespace MxControls.Tests;

/// <summary>
/// Records input as lines such as "down ShiftLeft", "up ShiftLeft", "type hello", "scroll 1", "hscroll 60", "system taskView" and "mouse down back".
/// </summary>
internal sealed class FakePlatform : IPlatform, IInputInjector
{
    private readonly object _lock = new();
    private readonly List<string> _log = [];

    public IReadOnlyList<string> Log
    {
        get { lock (_lock) return [.. _log]; }
    }

    public IHidBackend Hid => throw new NotSupportedException();
    public IInputInjector Input => this;
    public ILoginItem LoginItem => throw new NotSupportedException();
    public IKeyCapture? KeyCapture => null;
    public IKeyWatch? KeyWatch => null;
    public IPrograms? Programs => null;

    public bool Supports(string code) => code is "ShiftLeft" or "ControlLeft" or "KeyC" or "KeyV" or "Delete" or "MediaPlayPause";
    public void Down(string code) => Record($"down {code}");
    public void Up(string code) => Record($"up {code}");
    public void Type(string text) => Record($"type {text}");
    public void Scroll(int notches) => Record($"scroll {notches}");
    public void ScrollHorizontal(int units) => Record($"hscroll {units}");
    public bool SupportsMouse(string button) => button is MouseButtons.Back or MouseButtons.Forward;
    public void MouseDown(string button) => Record($"mouse down {button}");
    public void MouseUp(string button) => Record($"mouse up {button}");

    public bool SupportsSystem(string name) => name == SystemActions.TaskView;
    public void RunSystem(string name) => Record($"system {name}");
    public bool IsOptionsPlusRunning() => false;

    private void Record(string line)
    {
        lock (_lock) _log.Add(line);
    }
}
