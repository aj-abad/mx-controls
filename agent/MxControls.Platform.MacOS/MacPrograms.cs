using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MxControls.Platform.MacOS;

/// <summary>
/// Programs by executable file name, such as "google chrome". The window server's window list is read on a poll:
/// NSWorkspace's notifications and frontmostApplication only update on a main run loop, which the agent doesn't run.
/// Owner IDs in the list need no permission.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed partial class MacPrograms : IPrograms
{
    private const string Graphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const uint ListAll = 0;
    private const uint ListOnScreenOnly = 1;
    private const uint ExcludeDesktopElements = 16;
    private const string AppInBundle = ".app/Contents/MacOS/";
    private const string SystemServices = "/System/Library/";
    private const string Finder = "/Finder";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private static readonly nint Library = NativeLibrary.Load(Graphics);
    private static readonly nint LayerKey = Cf.Constant(Library, "kCGWindowLayer");
    private static readonly nint OwnerKey = Cf.Constant(Library, "kCGWindowOwnerPID");
    private static readonly nint AlphaKey = Cf.Constant(Library, "kCGWindowAlpha");

    [LibraryImport(Graphics)] private static partial nint CGWindowListCopyWindowInfo(uint options, uint relativeTo);

    public IDisposable WatchForeground(Action<string?> changed) => new Watcher(changed);

    public IReadOnlyList<ProgramInfo> Running()
    {
        var seen = new Dictionary<string, ProgramInfo>();
        var checkedPids = new HashSet<int>();
        foreach (var pid in WindowOwners(ListAll | ExcludeDesktopElements))
        {
            if (pid == Environment.ProcessId || !checkedPids.Add(pid) || Proc.PathOf(pid) is not { } path)
                continue;
            // A helper's own bundle sits inside its app's, and is no program a person switches to.
            int bundle = path.IndexOf(".app/", StringComparison.Ordinal);
            if (bundle < 0 || path.IndexOf(AppInBundle, StringComparison.Ordinal) != bundle)
                continue;
            // macOS's own background pieces, such as loginwindow, have windows too. Its apps are in /System/Applications.
            if (path.StartsWith(SystemServices, StringComparison.Ordinal) && !path.EndsWith(Finder, StringComparison.Ordinal))
                continue;
            var id = Path.GetFileName(path).ToLowerInvariant();
            seen.TryAdd(id, new ProgramInfo(id, Path.GetFileName(path[..bundle])));
        }
        return [.. seen.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    // The window list runs front to back, so the frontmost normal window's owner is the program in front.
    private static string? ForegroundId()
    {
        foreach (var pid in WindowOwners(ListOnScreenOnly | ExcludeDesktopElements))
            return Proc.PathOf(pid) is { } path ? Path.GetFileName(path).ToLowerInvariant() : null;
        return null;
    }

    /// <summary>The owner of each visible normal window, in the window server's order.</summary>
    private static List<int> WindowOwners(uint options)
    {
        var owners = new List<int>();
        nint list = CGWindowListCopyWindowInfo(options, 0);
        if (list == 0)
            return owners;
        try
        {
            for (nint i = 0; i < Cf.CFArrayGetCount(list); i++)
            {
                nint window = Cf.CFArrayGetValueAtIndex(list, i);
                if (Cf.ToLong(Cf.CFDictionaryGetValue(window, LayerKey)) != 0)
                    continue;
                if (Cf.ToDouble(Cf.CFDictionaryGetValue(window, AlphaKey)) is 0)
                    continue;
                if (Cf.ToLong(Cf.CFDictionaryGetValue(window, OwnerKey)) is { } pid)
                    owners.Add((int)pid);
            }
        }
        finally
        {
            Cf.CFRelease(list);
        }
        return owners;
    }

    private sealed class Watcher : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Thread _thread;

        public Watcher(Action<string?> changed)
        {
            _thread = new Thread(() => Run(changed)) { IsBackground = true, Name = "Foreground watch" };
            _thread.Start();
        }

        private void Run(Action<string?> changed)
        {
            string? last = null;
            bool reported = false;
            while (!_stop.IsCancellationRequested)
            {
                var id = ForegroundId();
                if (!reported || id != last)
                {
                    reported = true;
                    last = id;
                    changed(id);
                }
                _stop.Token.WaitHandle.WaitOne(PollInterval);
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _thread.Join(TimeSpan.FromSeconds(1));
            _stop.Dispose();
        }
    }
}
