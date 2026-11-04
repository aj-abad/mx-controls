using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using MxControls.Platform;

namespace MxControls.Platform.Windows;

/// <summary>
/// A Task Scheduler task that starts the program at sign-in, in the user's own session. Task Scheduler's own
/// restart-on-failure only covers a task that fails to launch, so restarting a crashed agent is the agent's job.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsLoginItem : ILoginItem
{
    // Earlier builds started the agent from the Run key; the task replaces it.
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled(string name) => Schtasks("/Query", "/TN", name).ExitCode == 0;

    public void Enable(string name, string executable)
    {
        RemoveRunKey(name);
        var current = Schtasks("/Query", "/TN", name, "/XML", "ONE");
        if (current.ExitCode == 0 && CommandPattern().Match(current.Output) is { Success: true } command
            && string.Equals(System.Net.WebUtility.HtmlDecode(command.Groups[1].Value).Trim('"'), executable, StringComparison.OrdinalIgnoreCase))
            return;

        var definition = Path.Combine(Path.GetTempPath(), $"mx-controls-task-{Environment.ProcessId}.xml");
        try
        {
            // schtasks reads the definition as UTF-16, as the XML declaration says.
            File.WriteAllText(definition, TaskXml(executable), Encoding.Unicode);
            var created = Schtasks("/Create", "/TN", name, "/XML", definition, "/F");
            if (created.ExitCode != 0)
                throw new InvalidOperationException($"schtasks couldn't create the task: {created.Output.Trim()}");
        }
        finally
        {
            File.Delete(definition);
        }
    }

    public void Disable(string name)
    {
        RemoveRunKey(name);
        if (IsEnabled(name))
            Schtasks("/Delete", "/TN", name, "/F");
    }

    private static string TaskXml(string executable)
    {
        var user = SecurityElement.Escape($@"{Environment.UserDomainName}\{Environment.UserName}");
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Runs MX Studio button mappings and gestures for the MX Master mouse.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>"{SecurityElement.Escape(executable)}"</Command>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static void RemoveRunKey(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    private static (int ExitCode, string Output) Schtasks(params string[] arguments)
    {
        var start = new ProcessStartInfo("schtasks.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    [GeneratedRegex(@"<Command>([^<]+)</Command>")]
    private static partial Regex CommandPattern();
}
