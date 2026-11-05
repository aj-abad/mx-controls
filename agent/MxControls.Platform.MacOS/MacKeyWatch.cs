using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MxControls.Platform.MacOS;

/// <summary>
/// A session event tap on a thread of its own, there for as long as a global shortcut is set. It needs the
/// Accessibility permission, and macOS keeps keys typed into password fields from it.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed unsafe class MacKeyWatch : IKeyWatch
{
    // The tap's callback is a static function pointer, so it finds the watch here; the agent has just one.
    private static MacKeyWatch? s_watching;

    private readonly object _lock = new();
    private volatile Func<string?, bool, bool>? _onKey;
    private volatile bool _finish;
    private Thread? _thread;
    private nint _loop;
    private nint _port;
    private bool _asked;

    public void Start(Func<string?, bool, bool> onKey)
    {
        lock (_lock)
        {
            _onKey = onKey;
            if (_thread is not null)
                return;
            _finish = false;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Key watch" };
            thread.Start();
            ready.Wait();
            if (_port == 0)
            {
                _onKey = null;
                // Once: the agent tries again until the permission comes, and the dialog shouldn't come each time.
                if (!_asked)
                {
                    _asked = true;
                    MacInput.AskForTrust();
                }
                throw new InvalidOperationException(MacInput.MissingTrust);
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
            _finish = true;
            Cf.CFRunLoopStop(_loop);
            _thread.Join();
            _thread = null;
        }
    }

    private void Run(ManualResetEventSlim ready)
    {
        s_watching = this;
        ulong mask = (1UL << (int)MacKeyCapture.KeyDown) | (1UL << (int)MacKeyCapture.KeyUp) | (1UL << (int)MacKeyCapture.FlagsChanged);
        _port = MacKeyCapture.CGEventTapCreate(MacKeyCapture.SessionEventTap, MacKeyCapture.HeadInsert, MacKeyCapture.DefaultTap, mask, &OnEvent, 0);
        if (_port == 0)
        {
            ready.Set();
            return;
        }
        _loop = Cf.CFRunLoopGetCurrent();
        nint source = Cf.CFMachPortCreateRunLoopSource(0, _port, 0);
        Cf.CFRunLoopAddSource(_loop, source, Cf.CommonModes);
        MacKeyCapture.CGEventTapEnable(_port, 1);
        ready.Set();

        while (!_finish)
            Cf.CFRunLoopRunInMode(Cf.DefaultMode, 1, 0);

        MacKeyCapture.CGEventTapEnable(_port, 0);
        Cf.CFRunLoopRemoveSource(_loop, source, Cf.CommonModes);
        Cf.CFRelease(source);
        Cf.CFRelease(_port);
        _port = 0;
        _loop = 0;
    }

    [UnmanagedCallersOnly]
    private static nint OnEvent(nint proxy, uint type, nint e, nint userInfo)
    {
        try
        {
            if (s_watching is { } watch)
            {
                // macOS turns off a tap it thinks is too slow, or during secure input.
                if (type is MacKeyCapture.TapDisabledByTimeout or MacKeyCapture.TapDisabledByUserInput)
                    MacKeyCapture.CGEventTapEnable(watch._port, 1);
                else if (watch.Hide(type, e))
                    return 0;
            }
        }
        catch
        {
            // An exception can't cross into macOS; let the key through instead.
        }
        return e;
    }

    private bool Hide(uint type, nint e)
    {
        // Keys that software types, the agent's own included, aren't the user pressing a shortcut.
        if (_onKey is not { } onKey || MacKeyCapture.CGEventGetIntegerValueField(e, MacKeyCapture.FieldSourcePid) != 0)
            return false;
        var key = (ushort)MacKeyCapture.CGEventGetIntegerValueField(e, MacKeyCapture.FieldKeycode);
        bool down = type switch
        {
            MacKeyCapture.KeyDown => true,
            MacKeyCapture.KeyUp => false,
            _ => MacKeyCapture.ModifierBits.TryGetValue(key, out var bit) && (MacKeyCapture.CGEventGetFlags(e) & bit) != 0,
        };
        return onKey(MacInput.CodeFor(key), down);
    }
}
