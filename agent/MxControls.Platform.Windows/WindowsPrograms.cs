using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Runtime.Versioning;
using MxControls.Platform;

namespace MxControls.Platform.Windows;

/// <summary>
/// Programs by executable file name. The foreground is followed with a WinEvent hook on a thread of its own, so a
/// switch is seen as it happens rather than on a poll.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class WindowsPrograms : IPrograms
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMinimizeEnd = 0x0017;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WmQuit = 0x0012;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const uint GwOwner = 4;

    // The hook procedure is a static function pointer, so it finds the watcher here; the agent has just one.
    private static Watcher? s_watcher;

    // Where each program seen running lives, so its icon can be found after it closes.
    private static readonly ConcurrentDictionary<string, string> s_paths = new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [LibraryImport("user32.dll")]
    private static partial nint SetWinEventHook(uint min, uint max, nint module,
        delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> proc, uint process, uint thread, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(out Msg message, nint hwnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out Msg message, nint hwnd, uint min, uint max, uint remove);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint threadId, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(delegate* unmanaged<nint, nint, int> proc, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextLengthW(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint hwnd, uint command);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll")]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageName(nint process, uint flags, char* name, ref uint size);

    public IDisposable WatchForeground(Action<string?> changed)
    {
        var watcher = new Watcher(changed);
        if (Interlocked.CompareExchange(ref s_watcher, watcher, null) is not null)
            throw new InvalidOperationException("The foreground is already being watched.");
        watcher.Start();
        return watcher;
    }

    public IReadOnlyList<ProgramInfo> Running()
    {
        var windows = new List<nint>();
        var handle = GCHandle.Alloc(windows);
        try
        {
            EnumWindows(&CollectWindow, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        var seen = new Dictionary<string, ProgramInfo>();
        foreach (var hwnd in windows)
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == Environment.ProcessId || ImagePath(pid) is not { } path)
                continue;
            var id = Path.GetFileName(path).ToLowerInvariant();
            s_paths[id] = path;
            // Store apps all draw their windows through this host, so it names none of them.
            if (id != "applicationframehost.exe" && !seen.ContainsKey(id))
                seen[id] = new ProgramInfo(id, DisplayName(path));
        }
        return [.. seen.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    // The windows a person switches between: visible, titled, unowned, and not a tool palette.
    [UnmanagedCallersOnly]
    private static int CollectWindow(nint hwnd, nint param)
    {
        if (IsWindowVisible(hwnd) && GetWindowTextLengthW(hwnd) > 0 && GetWindow(hwnd, GwOwner) == 0
            && (GetWindowLongPtr(hwnd, GwlExStyle) & WsExToolWindow) == 0)
            ((List<nint>)GCHandle.FromIntPtr(param).Target!).Add(hwnd);
        return 1;
    }

    private static string DisplayName(string path)
    {
        try
        {
            if (FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description)
                return description.Trim();
        }
        catch (Exception error) when (error is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
        }
        return Path.GetFileNameWithoutExtension(path);
    }

    private static string? ImagePath(uint pid)
    {
        nint process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == 0)
            return null;
        try
        {
            Span<char> buffer = stackalloc char[1024];
            uint size = (uint)buffer.Length;
            fixed (char* name = buffer)
                return QueryFullProcessImageName(process, 0, name, ref size) ? new string(buffer[..(int)size]) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static string? ForegroundId()
    {
        nint hwnd = GetForegroundWindow();
        if (hwnd == 0)
            return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (ImagePath(pid) is not { } path)
            return null;
        var id = Path.GetFileName(path).ToLowerInvariant();
        s_paths[id] = path;
        return id;
    }

    public byte[]? Icon(string id) => PathOf(id) is { } path ? WindowsIcons.Extract(path) : null;

    /// <summary>
    /// Where a program's executable is: where it was seen running, where an installer registered it, or where a
    /// process of it runs now in the background.
    /// </summary>
    private static string? PathOf(string id)
    {
        if (s_paths.TryGetValue(id, out var seen) && File.Exists(seen))
            return seen;
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{id}");
            if (key?.GetValue(null) is string registered && File.Exists(registered.Trim('"')))
                return s_paths[id] = registered.Trim('"');
        }
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (ImagePath((uint)process.Id) is { } path && Path.GetFileName(path).Equals(id, StringComparison.OrdinalIgnoreCase))
                    return s_paths[id] = path;
            }
        }
        return null;
    }

    [UnmanagedCallersOnly]
    private static void OnWinEvent(nint hook, uint e, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            s_watcher?.Check();
        }
        catch
        {
            // An exception can't cross into Windows.
        }
    }

    private sealed class Watcher(Action<string?> changed) : IDisposable
    {
        private Thread? _thread;
        private uint _threadId;
        private string? _last;
        private bool _reported;

        public void Start()
        {
            using var ready = new ManualResetEventSlim();
            _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Foreground watch" };
            _thread.Start();
            ready.Wait();
        }

        private void Run(ManualResetEventSlim ready)
        {
            // A thread gets its message queue on its first message call, and PostThreadMessage needs one.
            PeekMessageW(out _, 0, 0, 0, 0);
            _threadId = GetCurrentThreadId();
            // A window restored from the taskbar doesn't always raise a foreground event of its own.
            nint hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, 0, &OnWinEvent, 0, 0, WineventOutOfContext);
            nint restore = SetWinEventHook(EventSystemMinimizeEnd, EventSystemMinimizeEnd, 0, &OnWinEvent, 0, 0, WineventOutOfContext);
            ready.Set();
            Check();
            while (GetMessageW(out _, 0, 0, 0) > 0)
            {
            }
            if (hook != 0)
                UnhookWinEvent(hook);
            if (restore != 0)
                UnhookWinEvent(restore);
        }

        /// <summary>Hook thread only.</summary>
        public void Check()
        {
            var id = ForegroundId();
            if (_reported && id == _last)
                return;
            _reported = true;
            _last = id;
            changed(id);
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref s_watcher, null, this) != this)
                return;
            PostThreadMessageW(_threadId, WmQuit, 0, 0);
            _thread?.Join(TimeSpan.FromSeconds(1));
        }
    }
}
