using MxControls;
using MxControls.Agent;

// One agent per user: a second copy would fight the first over the mouse's diversions.
using var instance = new Mutex(initiallyOwned: true, "MxControls.Agent", out bool first);
if (!first)
{
    Console.Error.WriteLine("The MX Controls agent is already running.");
    return 0;
}

var store = new SettingsStore();
Log.Open(store.Folder);
Log.Write($"Agent started; settings in {store.Folder}");

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Cancel();

await using var service = new AgentService(Platforms.Current(), store);
var server = new PipeServer(service);
await Task.WhenAll(service.RunAsync(stop.Token), server.RunAsync(stop.Token));
Log.Write("Agent stopped");
return 0;
