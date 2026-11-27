using System.Diagnostics;
using EzPz;
using EzPz.Agent;

const string WorkerLock = "EzPz.Agent";
const string SupervisorLock = "EzPz.Agent.Supervisor";

if (args.Contains("--stop"))
    return await StopRunningAgentAsync();
if (args is ["--ui", .. var uiArgs])
    return await DriveUiAsync(uiArgs);
if (args is ["--haptic", .. var hapticArgs])
    return await PlayHapticAsync(hapticArgs);
// `--window` opens the agent's own window, where it has one, and asks a running agent for it.
var openWindow = args.Contains("--window");
if (!args.Contains("--worker"))
    return await SuperviseAsync(openWindow);

// One agent per user: a second copy would fight the first over the mouse's diversions.
using var instance = InstanceLock.TryAcquire(WorkerLock);
if (instance is null)
{
    Console.Error.WriteLine("The ezPz agent is already running.");
    return openWindow ? await ShowRunningWindowAsync() : 0;
}

// Read now, while the file on disk is still the one running; an update can replace it at any time.
_ = AgentBuild.Hash;

var store = new SettingsStore();
Log.Open(store.Folder);
Log.Write($"Agent started; settings in {store.Folder}");
AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"Crashed: {e.ExceptionObject}");
TaskScheduler.UnobservedTaskException += (_, e) => Log.Write($"Unobserved task error: {e.Exception.GetBaseException().Message}");

// Not disposed: ProcessExit also fires after a normal return from here, and must still be able to cancel it.
var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Cancel();

var service = new AgentService(Platforms.Current(), store);
service.ShutdownRequested += stop.Cancel;
var server = new PipeServer(service);

if (EzPz.Agent.Ui.IWindowShell.Current is { } shell && EzPz.Agent.Ui.AppWindow.Available(shell))
{
    var window = new EzPz.Agent.Ui.AppWindow(service, shell);
    service.Ui = window;
    if (openWindow)
        window.Show();
    // AppKit runs on the main thread or not at all, so there the agent moves off it. Nothing above has awaited
    // yet, so this is still the main thread. Windows gives the window a thread of its own instead.
    if (OperatingSystem.IsMacOS())
    {
        _ = Task.Run(RunAgentAsync).ContinueWith(_ =>
        {
            if (OperatingSystem.IsMacOS())
                EzPz.Platform.MacOS.MacShell.Exit();
        });
        // Doesn't return: the process ends in AppKit's terminate, once the agent has stopped.
        EzPz.Platform.MacOS.MacShell.Run(EzPz.Agent.Ui.MacWindowShell.DelegateClass, stop.Cancel);
    }
}
return await RunAgentAsync();

async Task<int> RunAgentAsync()
{
    await using (service)
        await Task.WhenAll(service.RunAsync(stop.Token), server.RunAsync(stop.Token));
    // Disposing the service restored every diverted button.
    Log.Write("Agent stopped");
    return 0;
}

// Runs the agent as a `--worker` child and starts it again whenever it exits abnormally, so a crash or a
// killed worker leaves the buttons dead for seconds rather than until the next sign-in. A clean exit ends both.
static async Task<int> SuperviseAsync(bool openWindow)
{
    using var supervisor = InstanceLock.TryAcquire(SupervisorLock);
    if (supervisor is null)
        return openWindow ? await ShowRunningWindowAsync() : 0;
    Log.Open(new SettingsStore().Folder);

    var delay = TimeSpan.FromSeconds(1);
    while (true)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--worker");
        // Only the first: a worker started again after a crash comes back without a window.
        if (openWindow)
            start.ArgumentList.Add("--window");
        openWindow = false;
        var started = Stopwatch.StartNew();
        using (var worker = Process.Start(start)!)
        {
            await worker.WaitForExitAsync();
            if (worker.ExitCode == 0)
                return 0;
            Log.Write($"The agent exited with code {worker.ExitCode}; starting it again in {delay.TotalSeconds:0} s");
        }
        await Task.Delay(delay);
        // Back off while it keeps failing, and start fresh once it has stayed up for a while.
        delay = started.Elapsed > TimeSpan.FromMinutes(5)
            ? TimeSpan.FromSeconds(1)
            : TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
    }
}

// Has the running agent open its window, as a second launch of the app does.
static async Task<int> ShowRunningWindowAsync()
{
    // Windows only lets the agent take the foreground if this process, the one the user just started, allows it.
    if (OperatingSystem.IsWindows())
        EzPz.Platform.Windows.WindowsShell.AllowForeground();
    try
    {
        var error = await AgentClient.CallAsync("showWindow", "{}", connectTimeoutMs: 2000, replyTimeout: TimeSpan.FromSeconds(3));
        if (error is null)
            return 0;
        Console.Error.WriteLine(error);
    }
    catch (Exception error) when (error is TimeoutException or IOException or OperationCanceledException)
    {
        Console.Error.WriteLine($"The ezPz agent didn't open its window: {error.Message}");
    }
    return 1;
}

// Drives the running agent's window, for tests and tools: the agent must have started with EZPZ_AUTOMATION=1.
//   --ui show                 opens the window
//   --ui eval <script>        runs the body of an async function in the page and prints what it returns, as JSON
//   --ui snapshot <file.png>  saves the page as it's drawn now
static async Task<int> DriveUiAsync(string[] args)
{
    var (method, parameters) = args switch
    {
        ["show"] => ("showWindow", "{}"),
        ["eval", var script] => ("ui.eval", $$"""{"script":{{System.Text.Json.JsonSerializer.Serialize(script, AgentJson.Default.String)}}}"""),
        ["snapshot", _] => ("ui.snapshot", "{}"),
        _ => (null, null),
    };
    if (method is null)
    {
        Console.Error.WriteLine("Usage: ezpz-agent --ui show | eval <script> | snapshot <file.png>");
        return 2;
    }
    try
    {
        var (error, result) = await AgentClient.CallForResultAsync(
            method, parameters!, connectTimeoutMs: 2000, replyTimeout: TimeSpan.FromSeconds(15));
        if (error is not null)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        if (args[0] == "snapshot")
        {
            var base64 = System.Text.Json.JsonSerializer.Deserialize(result!, AgentJson.Default.String)!;
            File.WriteAllBytes(args[1], Convert.FromBase64String(base64));
            Console.WriteLine(Path.GetFullPath(args[1]));
        }
        else if (args[0] == "eval")
        {
            // The page's JSON, as the page wrote it, rather than as a JSON string of it.
            Console.WriteLine(System.Text.Json.JsonSerializer.Deserialize(result!, AgentJson.Default.String));
        }
        return 0;
    }
    catch (Exception error) when (error is TimeoutException or IOException or OperationCanceledException)
    {
        Console.Error.WriteLine($"The ezPz agent didn't answer: {error.Message}");
        return 1;
    }
}

// Asks a running agent to exit cleanly, so it hands the buttons back; used by the installer before replacing it.
static async Task<int> StopRunningAgentAsync()
{
    try
    {
        await AgentClient.CallAsync("shutdown", "{}", connectTimeoutMs: 2000, replyTimeout: TimeSpan.FromSeconds(3));
    }
    catch (Exception error) when (error is TimeoutException or IOException or OperationCanceledException)
    {
        if (!InstanceLock.IsHeld(WorkerLock) && !InstanceLock.IsHeld(SupervisorLock))
            return 0;
    }
    // The worker holds its lock until it has restored the buttons, and the supervisor until it has seen it go.
    for (int i = 0; i < 50; i++)
    {
        if (!InstanceLock.IsHeld(WorkerLock) && !InstanceLock.IsHeld(SupervisorLock))
            return 0;
        await Task.Delay(100);
    }
    return 1;
}

// Asks the running agent to play a waveform and never starts one: the agent has the mouse open, and a program
// reporting an event shouldn't leave a new agent behind. The exit code is the result, since a WinExe started from a
// console shows no output unless it's redirected.
static async Task<int> PlayHapticAsync(string[] args)
{
    if (args.Length != 1 || HapticWaveforms.Find(args[0]) is not { } waveform)
    {
        Console.Error.WriteLine($"Usage: ezpz-agent --haptic <waveform>, one of {string.Join(", ", HapticWaveforms.Names)}");
        return 2;
    }
    try
    {
        var error = await AgentClient.CallAsync(
            "playHaptic", $$"""{"waveform":"{{waveform}}"}""", connectTimeoutMs: 500, replyTimeout: TimeSpan.FromSeconds(5));
        if (error is null)
            return 0;
        Console.Error.WriteLine(error);
    }
    catch (TimeoutException)
    {
        Console.Error.WriteLine("The ezPz agent isn't running.");
    }
    catch (Exception error) when (error is IOException or OperationCanceledException)
    {
        Console.Error.WriteLine($"The ezPz agent didn't reply: {error.Message}");
    }
    return 1;
}
