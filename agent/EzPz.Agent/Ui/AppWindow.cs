using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Photino.NET;

namespace EzPz.Agent.Ui;

/// <summary>
/// The app's window, a Photino webview the agent opens in its own process: the page talks to the agent over the
/// webview's message bridge, in the pipe's wire format, so it needs no second process and no pipe. The window comes
/// and goes; the agent keeps running without it. Everything about the window happens on the shell's thread.
/// </summary>
public sealed class AppWindow(AgentService service, IWindowShell shell) : IAppUi
{
    private static readonly TimeSpan AutomationTimeout = TimeSpan.FromSeconds(10);

    private const int Width = 1124;
    private const int Height = 660;

    // Touched on the shell's thread only.
    private PhotinoWindow? _window;
    private nint _native;
    private int _nextEval;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<string>> _evals = new();

    // Photino starts the platform's message loop with its first window and blocks there, and on Windows ends it when
    // that window goes. Here the shell's thread runs the loop for as long as the agent does, so Photino is told it's
    // already running, and each window's WaitForClose returns once the window is up.
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "_messageLoopIsStarted")]
    private static extern ref bool MessageLoopStarted(PhotinoWindow? window);

    /// <summary>Whether the webview library is here; builds that bundle the Tauri app leave it out.</summary>
    public static bool Available(IWindowShell shell) =>
        NativeLibrary.TryLoad("Photino.Native", typeof(PhotinoWindow).Assembly, null, out _) && StartUrl(shell) is not null;

    /// <summary>Opens the window, or brings it forward. Any thread.</summary>
    public void Show() => shell.Post(() =>
    {
        if (_window is not null)
        {
            shell.Focus(_native);
            return;
        }
        Open();
    });

    private void Open()
    {
        var start = StartUrl(shell) ?? throw new InvalidOperationException("The app's page isn't here.");
        var window = new PhotinoWindow()
            .SetTitle("ezPz")
            .SetUseOsDefaultSize(false)
            .SetSize(Width, Height)
            .SetResizable(false)
            .Center()
            .SetDevToolsEnabled(Environment.GetEnvironmentVariable("EZPZ_UI_URL") is not null)
            .SetLogVerbosity(0)
            .RegisterCustomSchemeHandler(shell.Scheme, Serve)
            .RegisterWebMessageReceivedHandler(OnMessage)
            .RegisterWindowClosingHandler(OnClosing)
            .Load(start);
        shell.Configure(window);
        MessageLoopStarted(null) = true;
        window.WaitForClose();

        _window = window;
        _native = shell.Attach(window, Width, Height);
        service.Published += Forward;
        Log.Write("Window opened");
    }

    // On macOS Photino calls this as the window goes, too late to keep it; on Windows the window could stay, but
    // it goes on both, so the agent holds nothing of it while it's closed.
    private bool OnClosing(object? sender, EventArgs e)
    {
        service.Published -= Forward;
        _window = null;
        _native = 0;
        // Typing in the page may have started a capture; nothing would end it.
        service.StopKeyCapture();
        shell.Closed();
        Log.Write("Window closed");
        return false;
    }

    private void OnMessage(object? sender, string message)
    {
        var window = (PhotinoWindow)sender!;
        string method;
        try
        {
            using var request = JsonDocument.Parse(message, Protocol.Document);
            method = request.RootElement.GetProperty("method").GetString() ?? "";
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Log.Write($"Ignoring a message from the page: {error.Message}");
            return;
        }
        // The window's own calls run here, on its thread, and at once: a drag must start while the press it came
        // from is still the platform's current event.
        if (method.StartsWith("window.", StringComparison.Ordinal) || method == "ui.evalResult")
        {
            var reply = Requests.RespondAsync(service, message, WindowCall);
            Send(window, reply.Result.ToJsonString(Protocol.Json));
            return;
        }
        _ = Task.Run(async () =>
        {
            var reply = await Requests.RespondAsync(service, message);
            Send(window, reply.ToJsonString(Protocol.Json));
        });
    }

    private bool WindowCall(string method, JsonElement args, out object? result)
    {
        result = null;
        switch (method)
        {
            case "window.drag":
                shell.StartDrag(_native);
                return true;
            case "window.minimize":
                shell.Minimize(_native);
                return true;
            case "ui.evalResult":
                // The page's answer to EvalAsync: the result as JSON, or what the script threw.
                if (_evals.TryRemove(args.GetProperty("id").GetInt32(), out var eval))
                {
                    if (args.TryGetProperty("error", out var thrown))
                        eval.TrySetException(new InvalidOperationException(thrown.GetString()));
                    else
                        eval.TrySetResult(args.TryGetProperty("result", out var value) ? value.GetString() ?? "null" : "null");
                }
                return true;
            case "window.close":
                // After this message's reply, which Close would cut off.
                var window = _window;
                shell.Post(() => window?.Close());
                return true;
            default:
                throw new ArgumentException($"Unknown window call '{method}'.");
        }
    }

    public async Task<string> EvalAsync(string script)
    {
        if (!AgentService.Automation)
            throw new InvalidOperationException("UI automation is off.");
        var id = Interlocked.Increment(ref _nextEval);
        var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _evals[id] = result;
        var json = Requests.Event("automation.eval", new UiEvalRequest(id, script)).ToJsonString(Protocol.Json);
        shell.Post(() =>
        {
            if (_window is { } window)
                window.SendWebMessage(json);
            else if (_evals.TryRemove(id, out var orphan))
                orphan.TrySetException(new InvalidOperationException("The window isn't open."));
        });
        try
        {
            return await result.Task.WaitAsync(AutomationTimeout);
        }
        catch (TimeoutException)
        {
            _evals.TryRemove(id, out _);
            throw new TimeoutException("The page didn't answer the script.");
        }
    }

    public Task<byte[]> SnapshotAsync()
    {
        var png = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        shell.Post(() => shell.Snapshot(_native, image =>
        {
            if (image is null)
                png.TrySetException(new InvalidOperationException("Couldn't take a snapshot; is the window open?"));
            else
                png.TrySetResult(image);
        }));
        return png.Task.WaitAsync(AutomationTimeout);
    }

    private void Forward(string name, object data)
    {
        var json = Requests.Event(name, data).ToJsonString(Protocol.Json);
        if (_window is { } window)
            Send(window, json);
    }

    /// <summary>Posts to the page on the window's thread, unless the window has gone since; its webview goes with it.</summary>
    private void Send(PhotinoWindow window, string json) => shell.Post(() =>
    {
        if (ReferenceEquals(_window, window))
            window.SendWebMessage(json);
    });

    /// <summary>
    /// The dev server in <c>EZPZ_UI_URL</c>, or the generated site: <c>EZPZ_UI_DIR</c>, or <c>ui</c> beside the agent.
    /// </summary>
    private static Uri? StartUrl(IWindowShell shell)
    {
        if (Environment.GetEnvironmentVariable("EZPZ_UI_URL") is { Length: > 0 } url)
            return new Uri(url);
        return File.Exists(Path.Combine(SiteRoot, "index.html")) ? shell.SiteUrl : null;
    }

    private static string SiteRoot { get; } = Path.GetFullPath(
        Environment.GetEnvironmentVariable("EZPZ_UI_DIR") is { Length: > 0 } dir ? dir : Path.Combine(AppContext.BaseDirectory, "ui"));

    private Stream? Serve(object? sender, string scheme, string url, out string contentType)
    {
        var uri = new Uri(url);
        // On Windows every http request comes here; only the page's own origin is answered, and the rest, such as
        // the dev server's, go on to the network.
        if (!string.Equals(uri.Host, shell.SiteUrl.Host, StringComparison.OrdinalIgnoreCase))
        {
            contentType = "";
            return null;
        }
        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        var file = Path.GetFullPath(Path.Combine(SiteRoot, path));
        // A route the page handles itself, such as the root, gets the page.
        if (!file.StartsWith(SiteRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(file))
            file = Path.GetExtension(path) is "" ? Path.Combine(SiteRoot, "index.html") : "";
        contentType = ContentType(file);
        return file is "" || !File.Exists(file) ? null : File.OpenRead(file);
    }

    private static string ContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".html" => "text/html",
        ".js" or ".mjs" => "text/javascript",
        ".css" => "text/css",
        ".json" => "application/json",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".ico" => "image/x-icon",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        ".ttf" => "font/ttf",
        _ => "application/octet-stream",
    };
}
