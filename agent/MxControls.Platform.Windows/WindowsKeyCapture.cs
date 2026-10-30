using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MxControls.Platform;

namespace MxControls.Platform.Windows;

/// <summary>
/// A low-level keyboard hook on a thread of its own, installed only while recording. A key hidden from the hook
/// chain is hidden from every app and from the shell too, which is what keeps Win from opening Start.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class WindowsKeyCapture : IKeyCapture
{
    private const int WhKeyboardLl = 13;
    private const uint WmKeyDown = 0x0100;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmCheckIdle = 0x8000;
    private const uint LlkhfExtended = 0x01;
    private const uint LlkhfInjected = 0x10;

    // Auto-repeat comes far more often than this. A key-down after a longer gap is a new press whose key-up we
    // never saw, as when Ctrl+Alt+Del or Win+L switches desktops mid-recording.
    private const uint RepeatGap = 1500;

    // The hook procedure is a static function pointer, so it finds the capture here; the agent has just one.
    private static WindowsKeyCapture? s_hooked;

    private readonly object _lock = new();
    private volatile Action<string?, bool>? _onKey;
    private Thread? _thread;
    private uint _threadId;
    private int _hookError;

    /// <summary>When each key whose key-down we hid was last seen, so its key-up is hidden too. Hook thread only.</summary>
    private readonly Dictionary<uint, uint> _held = [];

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

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

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowsHookExW(int idHook, delegate* unmanaged<int, nint, nint, nint> proc, nint module, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(out Msg message, nint hwnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out Msg message, nint hwnd, uint min, uint max, uint remove);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint threadId, uint message, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);

    public void Start(Action<string?, bool> onKey)
    {
        lock (_lock)
        {
            _onKey = onKey;
            // The thread may still be hiding the key-ups of the last recording; it carries on with this one.
            if (_thread is not null)
                return;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Key capture" };
            thread.Start();
            ready.Wait();
            if (_hookError != 0)
            {
                _onKey = null;
                throw new Win32Exception(_hookError);
            }
            _thread = thread;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _onKey = null;
            if (_thread is not null)
                PostThreadMessageW(_threadId, WmCheckIdle, 0, 0);
        }
    }

    private void Run(ManualResetEventSlim ready)
    {
        // A thread gets its message queue on its first message call, and PostThreadMessage needs one.
        PeekMessageW(out _, 0, 0, 0, 0);
        _threadId = GetCurrentThreadId();
        s_hooked = this;
        nint hook = SetWindowsHookExW(WhKeyboardLl, &HookProc, GetModuleHandleW(null), 0);
        _hookError = hook == 0 ? Marshal.GetLastPInvokeError() : 0;
        ready.Set();
        if (hook == 0)
            return;

        // The hook procedure runs inside GetMessage. The thread ends once recording has stopped and every key it
        // hid has come back up; unhooking under the lock keeps a new Start from racing a second hook in.
        while (GetMessageW(out var message, 0, 0, 0) > 0)
        {
            if (message.Message != WmCheckIdle)
                continue;
            lock (_lock)
            {
                if (_onKey is not null || _held.Count > 0)
                    continue;
                UnhookWindowsHookEx(hook);
                _thread = null;
                return;
            }
        }
        lock (_lock)
        {
            UnhookWindowsHookEx(hook);
            _thread = null;
        }
    }

    [UnmanagedCallersOnly]
    private static nint HookProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && s_hooked is { } capture)
        {
            try
            {
                if (capture.Hide((uint)wParam, *(KbdLlHookStruct*)lParam))
                    return 1;
            }
            catch
            {
                // An exception can't cross into Windows; let the key through instead.
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    /// <summary>Whether to keep one key event from every app and the system.</summary>
    private bool Hide(uint message, KbdLlHookStruct key)
    {
        // Keys that software types, the agent's own included, aren't the user recording a shortcut.
        if ((key.Flags & LlkhfInjected) != 0)
            return false;
        var onKey = _onKey;
        var code = WindowsInput.CodeFor(key.VkCode, (key.Flags & LlkhfExtended) != 0);
        if (message is WmKeyDown or WmSysKeyDown)
        {
            if (_held.TryGetValue(key.VkCode, out var seen) && key.Time - seen < RepeatGap)
            {
                _held[key.VkCode] = key.Time;
                return true;
            }
            if (onKey is null)
            {
                if (_held.Remove(key.VkCode) && _held.Count == 0)
                    PostThreadMessageW(_threadId, WmCheckIdle, 0, 0);
                return false;
            }
            _held[key.VkCode] = key.Time;
            onKey(code, true);
            return true;
        }

        if (!_held.Remove(key.VkCode))
            return false;
        if (onKey is not null)
            onKey(code, false);
        else if (_held.Count == 0)
            PostThreadMessageW(_threadId, WmCheckIdle, 0, 0);
        return true;
    }
}
