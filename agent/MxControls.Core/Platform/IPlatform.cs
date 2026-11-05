namespace MxControls.Platform;

/// <summary>One HID top-level collection.</summary>
public sealed record HidDeviceInfo(string Path, ushort VendorId, ushort ProductId, ushort UsagePage, ushort Usage, string Product);

public interface IHidBackend
{
    IReadOnlyList<HidDeviceInfo> Enumerate(ushort vendorId);

    /// <summary>A stream whose reads return whole input reports and whose writes send whole output reports.</summary>
    Stream Open(HidDeviceInfo device);

    /// <summary>
    /// Calls <paramref name="arrived"/> as soon as a device of this vendor appears, on the platform's own thread, so a
    /// waking mouse needn't wait for the next poll. Null where the platform can't tell; polling still finds it.
    /// Disposing the result stops it.
    /// </summary>
    IDisposable? WatchArrivals(ushort vendorId, Action arrived) => null;
}

/// <summary>Sends input to the focused app. Keys are W3C <c>KeyboardEvent.code</c> names, such as "ShiftLeft".</summary>
public interface IInputInjector
{
    bool Supports(string code);
    void Down(string code);
    void Up(string code);

    /// <summary>Types text as written, whatever the keyboard layout, without pressing any key the text doesn't need.</summary>
    void Type(string text);

    /// <summary>Turns the vertical wheel by whole notches; positive scrolls up.</summary>
    void Scroll(int notches);

    /// <summary>Scrolls sideways by wheel units, 120 to a notch, so a fraction of a notch can be sent; positive scrolls right.</summary>
    void ScrollHorizontal(int units);

    /// <summary>Mouse buttons are named as in <see cref="Actions.MouseButtons"/>, such as "back".</summary>
    bool SupportsMouse(string button);
    void MouseDown(string button);
    void MouseUp(string button);
}

/// <summary>
/// Takes over the keyboard while a shortcut is being recorded: every key goes to the callback instead of to apps
/// and the system, so the Windows key doesn't open Start and Alt+Tab doesn't switch windows. Keys typed by
/// software, ours included, still go through.
/// </summary>
public interface IKeyCapture
{
    /// <summary>
    /// Reports each key going down or up as its <c>KeyboardEvent.code</c> name, or null for a key without one.
    /// Auto-repeat isn't reported. The callback runs on the platform's hook thread and must return quickly.
    /// </summary>
    void Start(Action<string?, bool> onKey);

    /// <summary>Hands the keyboard back. Keys still held stay hidden until released, so no app sees a stray key-up.</summary>
    void Stop();
}

/// <summary>
/// Sees every key the user presses in any app, for global shortcuts, and hides the ones it's told to. Keys typed by
/// software aren't reported.
/// </summary>
public interface IKeyWatch
{
    /// <summary>
    /// Reports each key going down, auto-repeat included, or up as its <c>KeyboardEvent.code</c> name, or null for a
    /// key without one. The callback returns whether to hide that event from apps; it runs on the platform's hook
    /// thread and must return quickly. Starting again only replaces the callback.
    /// </summary>
    void Start(Func<string?, bool, bool> onKey);

    void Stop();
}

/// <summary>Starts a program when the user signs in.</summary>
public interface ILoginItem
{
    bool IsEnabled(string name);
    void Enable(string name, string executable);
    void Disable(string name);
}

/// <summary>A program with a window: its ID, which profiles bind to, and a name to show.</summary>
/// <param name="Id">The executable's file name in lower case, such as "chrome.exe" on Windows.</param>
public sealed record ProgramInfo(string Id, string Name);

/// <summary>Which programs are running, and which one is in front.</summary>
public interface IPrograms
{
    /// <summary>
    /// Calls <paramref name="changed"/> with the ID of the program in front, once at the start and then each time
    /// another program comes to the front; null when it can't tell. It runs on the platform's own thread and must
    /// return quickly. Disposing the result stops it.
    /// </summary>
    IDisposable WatchForeground(Action<string?> changed);

    /// <summary>The programs with a window open, each once.</summary>
    IReadOnlyList<ProgramInfo> Running();
}

public interface IPlatform
{
    IHidBackend Hid { get; }
    IInputInjector Input { get; }
    ILoginItem LoginItem { get; }

    /// <summary>Null where the platform can't tell which program is in front; profiles then never follow programs.</summary>
    IPrograms? Programs { get; }

    /// <summary>Null where the platform can't take over the keyboard; the app then records keys in its own window.</summary>
    IKeyCapture? KeyCapture { get; }

    /// <summary>Null where the platform can't watch keys in other apps; global shortcuts then can't be set.</summary>
    IKeyWatch? KeyWatch { get; }

    /// <summary>Named actions whose keys differ by platform, such as "taskView"; see <see cref="Actions.SystemActions"/>.</summary>
    bool SupportsSystem(string name);
    void RunSystem(string name);

    /// <summary>Whether Logi Options+ is running; it diverts buttons too and will act on them alongside us.</summary>
    bool IsOptionsPlusRunning();
}
