using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MxControls.Platform.MacOS;

/// <summary>
/// A session event tap on a thread of its own, installed only while recording. A key dropped by the tap is hidden from
/// every app, which keeps Cmd+Tab and the like from acting while a shortcut is recorded. It needs the Accessibility permission.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed unsafe partial class MacKeyCapture : IKeyCapture
{
    private const string Graphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const int SessionEventTap = 1;
    private const int HeadInsert = 0;
    private const int DefaultTap = 0;
    private const uint KeyDown = 10;
    private const uint KeyUp = 11;
    private const uint FlagsChanged = 12;
    private const uint TapDisabledByTimeout = 0xFFFFFFFE;
    private const uint TapDisabledByUserInput = 0xFFFFFFFF;
    private const int FieldAutorepeat = 8;
    private const int FieldKeycode = 9;
    private const int FieldSourcePid = 41;
    private const ushort CapsLock = 57;

    // The flag that says whether each modifier key is down, by virtual key.
    private static readonly Dictionary<ushort, ulong> ModifierBits = new()
    {
        [56] = 0x2, [60] = 0x4, [59] = 0x1, [62] = 0x2000, [58] = 0x20, [61] = 0x40, [55] = 0x8, [54] = 0x10, [63] = 0x800000,
    };

    // The tap's callback is a static function pointer, so it finds the capture here; the agent has just one.
    private static MacKeyCapture? s_tapped;

    private readonly object _lock = new();
    private volatile Action<string?, bool>? _onKey;
    private volatile bool _finish;
    private Thread? _thread;
    private nint _loop;
    private nint _port;
    private bool _failed;

    /// <summary>Keys whose key-down we hid, so their key-up is hidden too. Tap thread only.</summary>
    private readonly HashSet<ushort> _held = [];

    [LibraryImport(Graphics)] private static partial nint CGEventTapCreate(int tap, int place, int options, ulong mask,
        delegate* unmanaged<nint, uint, nint, nint, nint> callback, nint userInfo);
    [LibraryImport(Graphics)] private static partial void CGEventTapEnable(nint port, byte enable);
    [LibraryImport(Graphics)] private static partial long CGEventGetIntegerValueField(nint e, int field);
    [LibraryImport(Graphics)] private static partial ulong CGEventGetFlags(nint e);

    public void Start(Action<string?, bool> onKey)
    {
        lock (_lock)
        {
            _onKey = onKey;
            // The thread may still be hiding the key-ups of the last recording; it carries on with this one.
            if (_thread is not null)
                return;
            _finish = false;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Key capture" };
            thread.Start();
            ready.Wait();
            if (_failed)
            {
                _onKey = null;
                _failed = false;
                MacInput.AskForTrust();
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
            if (_thread is not null)
                FinishIfIdle();
        }
    }

    /// <summary>Ends the thread once recording has stopped and every key it hid has come back up.</summary>
    private void FinishIfIdle()
    {
        _finish = true;
        if (_loop != 0)
            Cf.CFRunLoopStop(_loop);
    }

    private void Run(ManualResetEventSlim ready)
    {
        s_tapped = this;
        ulong mask = (1UL << (int)KeyDown) | (1UL << (int)KeyUp) | (1UL << (int)FlagsChanged);
        _port = CGEventTapCreate(SessionEventTap, HeadInsert, DefaultTap, mask, &OnEvent, 0);
        if (_port == 0)
        {
            _failed = true;
            ready.Set();
            return;
        }
        _loop = Cf.CFRunLoopGetCurrent();
        nint source = Cf.CFMachPortCreateRunLoopSource(0, _port, 0);
        Cf.CFRunLoopAddSource(_loop, source, Cf.CommonModes);
        CGEventTapEnable(_port, 1);
        ready.Set();

        while (true)
        {
            Cf.CFRunLoopRunInMode(Cf.DefaultMode, 1, 0);
            if (!_finish)
                continue;
            // Under the lock, so a new Start can't race a second tap in.
            lock (_lock)
            {
                if (_onKey is not null)
                {
                    _finish = false;
                    continue;
                }
                if (_held.Count > 0)
                    continue;
                CGEventTapEnable(_port, 0);
                Cf.CFRunLoopRemoveSource(_loop, source, Cf.CommonModes);
                Cf.CFRelease(source);
                Cf.CFRelease(_port);
                _port = 0;
                _loop = 0;
                _thread = null;
                return;
            }
        }
    }

    [UnmanagedCallersOnly]
    private static nint OnEvent(nint proxy, uint type, nint e, nint userInfo)
    {
        try
        {
            if (s_tapped is { } capture)
            {
                // macOS turns off a tap it thinks is too slow, or during secure input.
                if (type is TapDisabledByTimeout or TapDisabledByUserInput)
                    CGEventTapEnable(capture._port, 1);
                else if (capture.Hide(type, e))
                    return 0;
            }
        }
        catch
        {
            // An exception can't cross into macOS; let the key through instead.
        }
        return e;
    }

    /// <summary>Whether to keep one key event from every app and the system.</summary>
    private bool Hide(uint type, nint e)
    {
        // Keys that software types, the agent's own included, aren't the user recording a shortcut.
        if (CGEventGetIntegerValueField(e, FieldSourcePid) != 0)
            return false;
        var onKey = _onKey;
        var key = (ushort)CGEventGetIntegerValueField(e, FieldKeycode);
        var code = MacInput.CodeFor(key);

        bool down = type switch
        {
            KeyDown => true,
            KeyUp => false,
            // Caps Lock reports its light rather than the key, so each change is a press and a release in turn.
            _ when key == CapsLock => !_held.Contains(key),
            _ when ModifierBits.TryGetValue(key, out var bit) => (CGEventGetFlags(e) & bit) != 0,
            _ => false,
        };

        if (down)
        {
            if (type == KeyDown && CGEventGetIntegerValueField(e, FieldAutorepeat) != 0)
                return _held.Contains(key);
            if (onKey is null)
            {
                // A new press of a key whose release we never saw, as when the screen locked mid-recording.
                if (_held.Remove(key) && _held.Count == 0)
                    FinishIfIdle();
                return false;
            }
            _held.Add(key);
            onKey(code, true);
            return true;
        }

        if (!_held.Remove(key))
            return false;
        if (onKey is not null)
            onKey(code, false);
        else if (_held.Count == 0)
            FinishIfIdle();
        return true;
    }
}
