using System.Diagnostics;
using System.Runtime.Versioning;
using MxControls.Platform;

namespace MxControls.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsPlatform : IPlatform
{
    public IHidBackend Hid { get; } = new WindowsHid();
    public IKeyInjector Keys { get; } = new WindowsKeys();

    public bool IsOptionsPlusRunning()
    {
        var agents = Process.GetProcessesByName("logioptionsplus_agent");
        foreach (var process in agents)
            process.Dispose();
        return agents.Length > 0;
    }
}
