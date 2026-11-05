using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MxControls.Platform;

namespace MxControls.Platform.Windows;

/// <summary>A low-level keyboard hook on a thread of its own, there for as long as a global shortcut is set.</summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class WindowsKeyWatch : IKeyWatch
{
    private const uint WmQuit = 0x0012;
    private const uint KeyEventKeyUp = 0x0002;
    // No key has this virtual key; Windows only sees that something came between a modifier's press and release.
    private const byte MaskKey = 0xFF;

    // The hook procedure is a static function pointer, so it finds the watch here; the agent has just one.
    private static WindowsKeyWatch? s_watching;

    private readonly object _lock = new();
    private volatile Func<string?, bool, bool>? _onKey;
    private Thread? _thread;
    private uint _threadId;
    private int _hookError;

    [LibraryImport("user32.dll")]
    private static partial void keybd_event(byte key, byte scan, uint flags, nuint extra);

    public void Start(Func<string?, bool, bool> onKey)
    {
        lock (_lock)
        {
            _onKey = onKey;
            if (_thread is not null)
                return;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Key watch" };
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
            if (_thread is null)
                return;
            WindowsKeyCapture.PostThreadMessageW(_threadId, WmQuit, 0, 0);
            _thread.Join();
            _thread = null;
        }
    }

    private void Run(ManualResetEventSlim ready)
    {
        // A thread gets its message queue on its first message call, and PostThreadMessage needs one.
        WindowsKeyCapture.PeekMessageW(out _, 0, 0, 0, 0);
        _threadId = WindowsKeyCapture.GetCurrentThreadId();
        s_watching = this;
        nint hook = WindowsKeyCapture.SetWindowsHookExW(WindowsKeyCapture.WhKeyboardLl, &HookProc, WindowsKeyCapture.GetModuleHandleW(null), 0);
        _hookError = hook == 0 ? Marshal.GetLastPInvokeError() : 0;
        ready.Set();
        if (hook == 0)
            return;

        // The hook procedure runs inside GetMessage, which returns 0 on WM_QUIT.
        while (WindowsKeyCapture.GetMessageW(out _, 0, 0, 0) > 0)
        {
        }
        WindowsKeyCapture.UnhookWindowsHookEx(hook);
    }

    [UnmanagedCallersOnly]
    private static nint HookProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && s_watching is { } watch)
        {
            try
            {
                if (watch.Hide((uint)wParam, *(WindowsKeyCapture.KbdLlHookStruct*)lParam))
                    return 1;
            }
            catch
            {
                // An exception can't cross into Windows; let the key through instead.
            }
        }
        return WindowsKeyCapture.CallNextHookEx(0, code, wParam, lParam);
    }

    private bool Hide(uint message, WindowsKeyCapture.KbdLlHookStruct key)
    {
        // Keys that software types, the agent's own included, aren't the user pressing a shortcut.
        if (_onKey is not { } onKey || (key.Flags & WindowsKeyCapture.LlkhfInjected) != 0)
            return false;
        bool down = message is WindowsKeyCapture.WmKeyDown or WindowsKeyCapture.WmSysKeyDown;
        var name = WindowsInput.CodeFor(key.VkCode, (key.Flags & WindowsKeyCapture.LlkhfExtended) != 0);
        if (!onKey(name, down))
            return false;
        // With the key hidden, Windows would see Win or Alt tapped alone, and open Start or focus the menu bar.
        if (down)
        {
            keybd_event(MaskKey, 0, 0, 0);
            keybd_event(MaskKey, 0, KeyEventKeyUp, 0);
        }
        return true;
    }
}
