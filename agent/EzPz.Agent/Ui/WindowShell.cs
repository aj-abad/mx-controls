using System.Runtime.Versioning;
using EzPz.Platform.MacOS;
using EzPz.Platform.Windows;
using Photino.NET;

namespace EzPz.Agent.Ui;

/// <summary>
/// What differs between platforms around a Photino window: the thread it lives on, how the page is served, and the
/// native calls that give the window the app's look and move it.
/// </summary>
public interface IWindowShell
{
    /// <summary>The address the generated page is served at, through <see cref="Scheme"/>'s handler.</summary>
    Uri SiteUrl { get; }

    /// <summary>The URL scheme whose requests the agent answers from the generated page.</summary>
    string Scheme { get; }

    /// <summary>Runs <paramref name="work"/> on the window's thread, later. Any thread.</summary>
    void Post(Action work);

    /// <summary>Sets what has to be set before the native window exists.</summary>
    void Configure(PhotinoWindow window);

    /// <summary>Styles the window once it exists and returns its native handle. The window's thread.</summary>
    nint Attach(PhotinoWindow window, int width, int height);

    void StartDrag(nint window);
    void Minimize(nint window);
    void Focus(nint window);

    /// <summary>The window has gone. The window's thread.</summary>
    void Closed();

    /// <summary>Calls <paramref name="done"/> on the window's thread with the page as a PNG, or null.</summary>
    void Snapshot(nint window, Action<byte[]?> done);

    public static IWindowShell? Current =>
        OperatingSystem.IsMacOS() ? new MacWindowShell()
        : OperatingSystem.IsWindows() ? new WindowsWindowShell()
        : null;
}

[SupportedOSPlatform("macos")]
internal sealed class MacWindowShell : IWindowShell
{
    /// <summary>Photino's application delegate class, which <see cref="MacShell.Run"/> patches.</summary>
    public const string DelegateClass = "AppDelegate";

    // WKWebView takes any scheme it doesn't handle itself.
    public string Scheme => "ezpz";
    public Uri SiteUrl { get; } = new("ezpz://app/");

    public void Post(Action work) => MacShell.OnMain(work);

    public void Configure(PhotinoWindow window) =>
        // Photino's own ("Photino WebView") would hide the platform from the page, which reads it for shortcuts and
        // layout. This is WebKit's usual one, which it no longer changes between versions.
        window.SetUserAgent("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) ezPz");

    public nint Attach(PhotinoWindow window, int width, int height)
    {
        nint native = MacShell.FindWebViewWindow();
        if (native != 0)
            MacShell.Style(native, width, height);
        MacShell.QuitClosesWindow("Close ezPz");
        MacShell.Focus(native);
        return native;
    }

    public void StartDrag(nint window) => MacShell.StartDrag(window);
    public void Minimize(nint window) => MacShell.Minimize(window);
    public void Focus(nint window) => MacShell.Focus(window);
    public void Closed() => MacShell.SetInDock(false);
    public void Snapshot(nint window, Action<byte[]?> done) => MacShell.Snapshot(window, done);
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsWindowShell : IWindowShell
{
    // WebView2 only lets the host answer schemes it knows, so the page has an http origin of its own, under
    // .localhost so that nothing outside this machine could ever answer it. Requests elsewhere pass through.
    public string Scheme => "http";
    public Uri SiteUrl { get; } = new("http://ezpz.localhost/");

    public void Post(Action work) => WindowsShell.Post(work);

    public void Configure(PhotinoWindow window) => window
        // The title bar is the page's own, as under Tauri.
        .SetChromeless(true)
        // A see-through webview over the window's backdrop; Attach takes back the layered style this also asks for.
        .SetTransparent(true)
        // Beside the agent would be Program Files, which the webview can't write to.
        .SetTemporaryFilesPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EzPz", "WebView2"));

    public nint Attach(PhotinoWindow window, int width, int height)
    {
        nint hwnd = window.WindowHandle;
        WindowsShell.Style(hwnd, width, height);
        WindowsShell.Focus(hwnd);
        return hwnd;
    }

    public void StartDrag(nint window) => WindowsShell.StartDrag(window);
    public void Minimize(nint window) => WindowsShell.Minimize(window);
    public void Focus(nint window) => WindowsShell.Focus(window);
    public void Closed() { }
    public void Snapshot(nint window, Action<byte[]?> done) => done(WindowsShell.Snapshot(window));
}
