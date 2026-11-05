using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MxControls.Actions;

namespace MxControls.Platform.MacOS;

/// <summary>
/// Sends key, mouse button and wheel events through CGEventPost, which macOS drops silently without the Accessibility
/// permission; sending checks for it, so a missing permission is an error in the log and not a dead button.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed unsafe partial class MacInput : IInputInjector
{
    private const string Graphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string Services = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string Runtime = "/usr/lib/libobjc.A.dylib";

    private const int HidEventTap = 0;
    private const int SourceStateHidSystem = 1;
    private const uint EventFlagsChanged = 12;
    private const uint EventOtherMouseDown = 25;
    private const uint EventOtherMouseUp = 26;
    private const int MouseClickState = 1;
    private const int ScrollUnitPixel = 0;
    private const int ScrollUnitLine = 1;
    private const int MiddleButton = 2;
    private const int BackButton = 3;
    private const int ForwardButton = 4;
    private const int PixelsPerNotchDivisor = 3;
    private const long TrustRecheckMs = 2000;

    private const ulong FlagNonCoalesced = 0x100;
    private const ulong FlagNumericPad = 0x200000;
    private const ulong FlagSecondaryFn = 0x800000;

    // NSEventTypeSystemDefined, its subtype for auxiliary keys, and the states it packs into data1.
    private const ulong SystemDefined = 14;
    private const short AuxiliaryKeySubtype = 8;
    private const int AuxKeyDown = 0xA;
    private const int AuxKeyUp = 0xB;

    private static readonly Dictionary<string, ushort> Keys = BuildKeys();
    private static readonly Dictionary<ushort, string> Codes = BuildCodes();

    // NX_KEYTYPE_*: these keys have no virtual key code, and go as system-defined events.
    private static readonly Dictionary<string, int> AuxiliaryKeys = new()
    {
        ["AudioVolumeUp"] = 0, ["AudioVolumeDown"] = 1, ["AudioVolumeMute"] = 7,
        ["MediaPlayPause"] = 16, ["MediaTrackNext"] = 17, ["MediaTrackPrevious"] = 18,
    };

    // Each modifier's generic flag with the bit that says which side, as real keyboards send them.
    private static readonly Dictionary<string, ulong> Modifiers = new()
    {
        ["ShiftLeft"] = 0x20000 | 0x2, ["ShiftRight"] = 0x20000 | 0x4,
        ["ControlLeft"] = 0x40000 | 0x1, ["ControlRight"] = 0x40000 | 0x2000,
        ["AltLeft"] = 0x80000 | 0x20, ["AltRight"] = 0x80000 | 0x40,
        ["MetaLeft"] = 0x100000 | 0x8, ["MetaRight"] = 0x100000 | 0x10,
    };

    private readonly object _lock = new();
    private readonly nint _source = CGEventSourceCreate(SourceStateHidSystem);
    private readonly HashSet<string> _held = [];
    private long _trustedAt = -TrustRecheckMs;
    private bool _asked;
    private int _horizontalRemainder;

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;
    }

    [LibraryImport(Graphics)] private static partial nint CGEventSourceCreate(int state);
    [LibraryImport(Graphics)] private static partial nint CGEventCreate(nint source);
    [LibraryImport(Graphics)] private static partial CGPoint CGEventGetLocation(nint e);
    [LibraryImport(Graphics)] private static partial nint CGEventCreateKeyboardEvent(nint source, ushort key, byte down);
    [LibraryImport(Graphics)] private static partial nint CGEventCreateMouseEvent(nint source, uint type, CGPoint at, int button);
    [LibraryImport(Graphics)] private static partial nint CGEventCreateScrollWheelEvent2(nint source, int units, uint wheels, int wheel1, int wheel2, int wheel3);
    [LibraryImport(Graphics)] private static partial void CGEventSetFlags(nint e, ulong flags);
    [LibraryImport(Graphics)] private static partial void CGEventSetType(nint e, uint type);
    [LibraryImport(Graphics)] private static partial void CGEventSetIntegerValueField(nint e, int field, long value);
    [LibraryImport(Graphics)] private static partial void CGEventKeyboardSetUnicodeString(nint e, nuint length, char* text);
    [LibraryImport(Graphics)] private static partial void CGEventPost(int tap, nint e);

    [LibraryImport(Services)] private static partial byte AXIsProcessTrusted();
    [LibraryImport(Services)] private static partial byte AXIsProcessTrustedWithOptions(nint options);

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)] private static partial nint objc_getClass(string name);
    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)] private static partial nint sel_registerName(string name);
    [LibraryImport(Runtime)] private static partial nint objc_autoreleasePoolPush();
    [LibraryImport(Runtime)] private static partial void objc_autoreleasePoolPop(nint pool);

    public bool Supports(string code) => Keys.ContainsKey(code) || AuxiliaryKeys.ContainsKey(code);

    /// <summary>The code for a key from an event tap, or null for keys this injector can't send.</summary>
    internal static string? CodeFor(ushort key) => Codes.GetValueOrDefault(key);

    public void Down(string code) => Send(code, down: true);

    public void Up(string code) => Send(code, down: false);

    private void Send(string code, bool down)
    {
        RequireTrust();
        if (AuxiliaryKeys.TryGetValue(code, out var auxiliary))
        {
            SendAuxiliary(auxiliary, down);
            return;
        }
        if (!Keys.TryGetValue(code, out var key))
            throw new ArgumentException($"No Mac key for '{code}'", nameof(code));

        lock (_lock)
        {
            nint e = CGEventCreateKeyboardEvent(_source, key, down ? (byte)1 : (byte)0);
            bool modifier = Modifiers.ContainsKey(code);
            if (modifier)
            {
                if (down)
                    _held.Add(code);
                else
                    _held.Remove(code);
                CGEventSetType(e, EventFlagsChanged);
            }
            // Set by hand: a later event only carries a modifier that was posted before it if its own flags say so.
            CGEventSetFlags(e, HeldFlags() | KeyFlags(code));
            CGEventPost(HidEventTap, e);
            Cf.CFRelease(e);
        }
    }

    // Each character goes as its own key press with no key behind it, so the layout and held modifiers don't matter.
    public void Type(string text)
    {
        RequireTrust();
        Span<char> units = stackalloc char[2];
        foreach (var rune in text.EnumerateRunes())
        {
            int length = rune.EncodeToUtf16(units);
            fixed (char* start = units)
            {
                foreach (bool down in new[] { true, false })
                {
                    nint e = CGEventCreateKeyboardEvent(_source, 0, down ? (byte)1 : (byte)0);
                    CGEventSetFlags(e, 0);
                    CGEventKeyboardSetUnicodeString(e, (nuint)length, start);
                    CGEventPost(HidEventTap, e);
                    Cf.CFRelease(e);
                }
            }
        }
    }

    public void Scroll(int notches)
    {
        RequireTrust();
        PostScroll(ScrollUnitLine, notches, 0);
    }

    // A notch is 120 units, and about 40 pixels. A positive second wheel scrolls left.
    public void ScrollHorizontal(int units)
    {
        RequireTrust();
        int pixels;
        lock (_lock)
        {
            int total = _horizontalRemainder + units;
            pixels = total / PixelsPerNotchDivisor;
            _horizontalRemainder = total % PixelsPerNotchDivisor;
        }
        if (pixels != 0)
            PostScroll(ScrollUnitPixel, 0, -pixels);
    }

    private void PostScroll(int unit, int vertical, int horizontal)
    {
        nint e = CGEventCreateScrollWheelEvent2(_source, unit, 2, vertical, horizontal, 0);
        lock (_lock)
            CGEventSetFlags(e, HeldFlags());
        CGEventPost(HidEventTap, e);
        Cf.CFRelease(e);
    }

    public bool SupportsMouse(string button) => button is MouseButtons.Middle or MouseButtons.Back or MouseButtons.Forward;

    public void MouseDown(string button) => SendMouse(button, down: true);

    public void MouseUp(string button) => SendMouse(button, down: false);

    private void SendMouse(string button, bool down)
    {
        RequireTrust();
        int number = button switch
        {
            MouseButtons.Middle => MiddleButton,
            MouseButtons.Back => BackButton,
            MouseButtons.Forward => ForwardButton,
            _ => throw new ArgumentException($"No Mac mouse button for '{button}'", nameof(button)),
        };
        nint here = CGEventCreate(0);
        var location = CGEventGetLocation(here);
        Cf.CFRelease(here);

        nint e = CGEventCreateMouseEvent(_source, down ? EventOtherMouseDown : EventOtherMouseUp, location, number);
        CGEventSetIntegerValueField(e, MouseClickState, 1);
        lock (_lock)
            CGEventSetFlags(e, HeldFlags());
        CGEventPost(HidEventTap, e);
        Cf.CFRelease(e);
    }

    private ulong HeldFlags()
    {
        ulong flags = FlagNonCoalesced;
        foreach (var code in _held)
            flags |= Modifiers[code];
        return flags;
    }

    // The flags a real keyboard puts on these keys; some apps tell the keys apart by them.
    private static ulong KeyFlags(string code) => code switch
    {
        "ArrowLeft" or "ArrowRight" or "ArrowUp" or "ArrowDown" => FlagNumericPad | FlagSecondaryFn,
        "Home" or "End" or "PageUp" or "PageDown" or "Delete" or "Insert" => FlagSecondaryFn,
        _ when code.StartsWith("Numpad", StringComparison.Ordinal) => FlagNumericPad,
        _ when code.Length is 2 or 3 && code[0] == 'F' && char.IsAsciiDigit(code[1]) => FlagSecondaryFn,
        _ => 0,
    };

    private static void SendAuxiliary(int key, bool down)
    {
        nint pool = objc_autoreleasePoolPush();
        try
        {
            NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
            int state = down ? AuxKeyDown : AuxKeyUp;
            var create = (delegate* unmanaged<nint, nint, ulong, CGPoint, ulong, double, nint, nint, short, nint, nint, nint>)MessageSend;
            nint ns = create(objc_getClass("NSEvent"),
                sel_registerName("otherEventWithType:location:modifierFlags:timestamp:windowNumber:context:subtype:data1:data2:"),
                SystemDefined, default, (ulong)(state << 8), 0, 0, 0, AuxiliaryKeySubtype, (key << 16) | (state << 8), -1);
            nint e = ((delegate* unmanaged<nint, nint, nint>)MessageSend)(ns, sel_registerName("CGEvent"));
            CGEventPost(HidEventTap, e);
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    private static readonly nint MessageSend = NativeLibrary.GetExport(NativeLibrary.Load(Runtime), "objc_msgSend");

    private void RequireTrust()
    {
        long now = Environment.TickCount64;
        if (now - Volatile.Read(ref _trustedAt) < TrustRecheckMs)
            return;
        if (AXIsProcessTrusted() != 0)
        {
            Volatile.Write(ref _trustedAt, now);
            return;
        }
        lock (_lock)
        {
            if (!_asked)
            {
                _asked = true;
                AskForTrust();
            }
        }
        throw new InvalidOperationException(MissingTrust);
    }

    internal const string MissingTrust =
        "MX Studio needs the Accessibility permission to send keys. Allow it under System Settings > Privacy & Security > Accessibility.";

    /// <summary>Shows the system's dialog that leads to the Accessibility setting, if it hasn't been answered yet.</summary>
    internal static void AskForTrust()
    {
        nint services = NativeLibrary.Load(Services);
        nint key = Cf.Constant(services, "kAXTrustedCheckOptionPrompt");
        nint value = Cf.True;
        nint options = Cf.CFDictionaryCreate(0, &key, &value, 1, Cf.DictionaryKeyCallbacks, Cf.DictionaryValueCallbacks);
        AXIsProcessTrustedWithOptions(options);
        Cf.CFRelease(options);
    }

    private static Dictionary<string, ushort> BuildKeys()
    {
        return new Dictionary<string, ushort>
        {
            ["KeyA"] = 0, ["KeyS"] = 1, ["KeyD"] = 2, ["KeyF"] = 3, ["KeyH"] = 4, ["KeyG"] = 5, ["KeyZ"] = 6, ["KeyX"] = 7,
            ["KeyC"] = 8, ["KeyV"] = 9, ["KeyB"] = 11, ["KeyQ"] = 12, ["KeyW"] = 13, ["KeyE"] = 14, ["KeyR"] = 15,
            ["KeyY"] = 16, ["KeyT"] = 17, ["KeyO"] = 31, ["KeyU"] = 32, ["KeyI"] = 34, ["KeyP"] = 35, ["KeyL"] = 37,
            ["KeyJ"] = 38, ["KeyK"] = 40, ["KeyN"] = 45, ["KeyM"] = 46,
            ["Digit1"] = 18, ["Digit2"] = 19, ["Digit3"] = 20, ["Digit4"] = 21, ["Digit6"] = 22, ["Digit5"] = 23,
            ["Digit9"] = 25, ["Digit7"] = 26, ["Digit8"] = 28, ["Digit0"] = 29,
            ["Equal"] = 24, ["Minus"] = 27, ["BracketRight"] = 30, ["BracketLeft"] = 33, ["Quote"] = 39, ["Semicolon"] = 41,
            ["Backslash"] = 42, ["Comma"] = 43, ["Slash"] = 44, ["Period"] = 47, ["Backquote"] = 50, ["IntlBackslash"] = 10,
            ["Enter"] = 36, ["Tab"] = 48, ["Space"] = 49, ["Backspace"] = 51, ["Escape"] = 53, ["CapsLock"] = 57,
            ["MetaRight"] = 54, ["MetaLeft"] = 55, ["ShiftLeft"] = 56, ["AltLeft"] = 58, ["ControlLeft"] = 59,
            ["ShiftRight"] = 60, ["AltRight"] = 61, ["ControlRight"] = 62, ["ContextMenu"] = 110,
            ["Insert"] = 114, ["Home"] = 115, ["PageUp"] = 116, ["Delete"] = 117, ["End"] = 119, ["PageDown"] = 121,
            ["ArrowLeft"] = 123, ["ArrowRight"] = 124, ["ArrowDown"] = 125, ["ArrowUp"] = 126,
            ["F1"] = 122, ["F2"] = 120, ["F3"] = 99, ["F4"] = 118, ["F5"] = 96, ["F6"] = 97, ["F7"] = 98, ["F8"] = 100,
            ["F9"] = 101, ["F10"] = 109, ["F11"] = 103, ["F12"] = 111, ["F13"] = 105, ["F14"] = 107, ["F15"] = 113,
            ["F16"] = 106, ["F17"] = 64, ["F18"] = 79, ["F19"] = 80, ["F20"] = 90,
            // A Mac keyboard has F13 to F15 where a PC keyboard has these three.
            ["PrintScreen"] = 105, ["ScrollLock"] = 107, ["Pause"] = 113,
            ["NumpadDecimal"] = 65, ["NumpadMultiply"] = 67, ["NumpadAdd"] = 69, ["NumpadDivide"] = 75, ["NumpadEnter"] = 76,
            ["NumpadSubtract"] = 78, ["NumpadEqual"] = 81, ["Numpad0"] = 82, ["Numpad1"] = 83, ["Numpad2"] = 84, ["Numpad3"] = 85,
            ["Numpad4"] = 86, ["Numpad5"] = 87, ["Numpad6"] = 88, ["Numpad7"] = 89, ["Numpad8"] = 91, ["Numpad9"] = 92,
        };
    }

    // F13 to F15 stay themselves when recorded; the first name in the table wins.
    private static Dictionary<ushort, string> BuildCodes()
    {
        var codes = new Dictionary<ushort, string>();
        foreach (var (code, key) in Keys)
            codes.TryAdd(key, code);
        return codes;
    }
}
