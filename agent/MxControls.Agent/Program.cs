using System.Diagnostics;
using MxControls;
using MxControls.Agent;

const string WorkerLock = "MxControls.Agent";
const string SupervisorLock = "MxControls.Agent.Supervisor";

if (args.Contains("--stop"))
    return await StopRunningAgentAsync();
if (args is ["--haptic", .. var hapticArgs])
    return await PlayHapticAsync(hapticArgs);
if (!args.Contains("--worker"))
    return await SuperviseAsync();

// One agent per user: a second copy would fight the first over the mouse's diversions.
using var instance = InstanceLock.TryAcquire(WorkerLock);
if (instance is null)
{
    Console.Error.WriteLine("The MX Studio agent is already running.");
    return 0;
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

await using (var service = new AgentService(Platforms.Current(), store))
{
    service.ShutdownRequested += stop.Cancel;
    var server = new PipeServer(service);
    await Task.WhenAll(service.RunAsync(stop.Token), server.RunAsync(stop.Token));
}
// Disposing the service restored every diverted button.
Log.Write("Agent stopped");
return 0;

// Runs the agent as a `--worker` child and starts it again whenever it exits abnormally, so a crash or a
// killed worker leaves the buttons dead for seconds rather than until the next sign-in. A clean exit ends both.
static async Task<int> SuperviseAsync()
{
    using var supervisor = InstanceLock.TryAcquire(SupervisorLock);
    if (supervisor is null)
        return 0;
    Log.Open(new SettingsStore().Folder);

    var delay = TimeSpan.FromSeconds(1);
    while (true)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--worker");
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
        Console.Error.WriteLine($"Usage: mx-agent --haptic <waveform>, one of {string.Join(", ", HapticWaveforms.Names)}");
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
        Console.Error.WriteLine("The MX Studio agent isn't running.");
    }
    catch (Exception error) when (error is IOException or OperationCanceledException)
    {
        Console.Error.WriteLine($"The MX Studio agent didn't reply: {error.Message}");
    }
    return 1;
}
