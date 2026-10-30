using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MxControls.Actions;
using MxControls.Platform;

namespace MxControls.Platform.Windows;

/// <summary>
/// Sends key, mouse button and wheel events through SendInput. Keys with a scan code are sent as scan
/// codes, which games that read raw keyboard input also accept; media keys fall back to virtual keys.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsInput : IInputInjector
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventXDown = 0x0080;
    private const uint MouseEventXUp = 0x0100;
    private const uint MouseEventWheel = 0x0800;
    private const uint MouseEventHWheel = 0x1000;
    private const int XButton1 = 1;
    private const int XButton2 = 2;
    private const int WheelDelta = 120;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const uint KeyEventScanCode = 0x0008;
    private const uint MapVkToVscEx = 4;

    private static readonly Dictionary<string, ushort> VirtualKeys = BuildVirtualKeys();
    private static readonly Dictionary<uint, string> Codes = VirtualKeys.ToDictionary(pair => (uint)pair.Value, pair => pair.Key);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public int MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    /// <summary>INPUT. The union is sized for MOUSEINPUT, its largest member.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KeyboardInput Keyboard;
        [FieldOffset(8)] public MouseInput Mouse;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint count, ReadOnlySpan<Input> inputs, int size);

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static partial uint MapVirtualKey(uint code, uint mapType);

    public bool Supports(string code) => VirtualKeys.ContainsKey(code);

    /// <summary>The code for a virtual key from a keyboard hook, or null for keys this injector can't send.</summary>
    internal static string? CodeFor(uint virtualKey, bool extended) =>
        // The numpad's Enter is VK_RETURN with the extended flag, and we can't send it apart from Enter.
        virtualKey == 0x0D && extended ? null : Codes.GetValueOrDefault(virtualKey);

    public void Down(string code) => Send(code, up: false);

    public void Up(string code) => Send(code, up: true);

    // Each UTF-16 unit goes as its own Unicode key event, so surrogate pairs arrive as the two halves apps expect.
    public void Type(string text)
    {
        foreach (char c in text)
        {
            Dispatch(new Input { Type = InputKeyboard, Keyboard = new KeyboardInput { ScanCode = c, Flags = KeyEventUnicode } });
            Dispatch(new Input { Type = InputKeyboard, Keyboard = new KeyboardInput { ScanCode = c, Flags = KeyEventUnicode | KeyEventKeyUp } });
        }
    }

    public void Scroll(int notches) =>
        Dispatch(new Input { Type = InputMouse, Mouse = new MouseInput { MouseData = notches * WheelDelta, Flags = MouseEventWheel } });

    public void ScrollHorizontal(int units) =>
        Dispatch(new Input { Type = InputMouse, Mouse = new MouseInput { MouseData = units, Flags = MouseEventHWheel } });

    public bool SupportsMouse(string button) => button is MouseButtons.Middle or MouseButtons.Back or MouseButtons.Forward;

    public void MouseDown(string button) => SendMouse(button, up: false);

    public void MouseUp(string button) => SendMouse(button, up: true);

    // Back and forward are X buttons 1 and 2, which browsers, Explorer and most editors take as history.
    private static void SendMouse(string button, bool up)
    {
        var mouse = button switch
        {
            MouseButtons.Middle => new MouseInput { Flags = up ? MouseEventMiddleUp : MouseEventMiddleDown },
            MouseButtons.Back => new MouseInput { MouseData = XButton1, Flags = up ? MouseEventXUp : MouseEventXDown },
            MouseButtons.Forward => new MouseInput { MouseData = XButton2, Flags = up ? MouseEventXUp : MouseEventXDown },
            _ => throw new ArgumentException($"No Windows mouse button for '{button}'", nameof(button)),
        };
        Dispatch(new Input { Type = InputMouse, Mouse = mouse });
    }

    private static void Send(string code, bool up)
    {
        if (!VirtualKeys.TryGetValue(code, out var virtualKey))
            throw new ArgumentException($"No Windows key for '{code}'", nameof(code));
        uint scan = MapVirtualKey(virtualKey, MapVkToVscEx);
        var keyboard = new KeyboardInput { Flags = up ? KeyEventKeyUp : 0 };
        if (scan != 0)
        {
            keyboard.ScanCode = (ushort)(scan & 0xFF);
            keyboard.Flags |= KeyEventScanCode | ((scan & 0xFF00) is 0xE000 or 0xE100 ? KeyEventExtendedKey : 0);
        }
        else
        {
            keyboard.VirtualKey = virtualKey;
        }

        Dispatch(new Input { Type = InputKeyboard, Keyboard = keyboard });
    }

    private static void Dispatch(Input input)
    {
        ReadOnlySpan<Input> inputs = [input];
        // SendInput returns 0 when UIPI blocks injection into a more elevated window.
        if (SendInput(1, inputs, Marshal.SizeOf<Input>()) != 1)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    private static Dictionary<string, ushort> BuildVirtualKeys()
    {
        var keys = new Dictionary<string, ushort>
        {
            ["ShiftLeft"] = 0xA0, ["ShiftRight"] = 0xA1, ["ControlLeft"] = 0xA2, ["ControlRight"] = 0xA3,
            ["AltLeft"] = 0xA4, ["AltRight"] = 0xA5, ["MetaLeft"] = 0x5B, ["MetaRight"] = 0x5C, ["ContextMenu"] = 0x5D,
            ["Space"] = 0x20, ["Enter"] = 0x0D, ["Tab"] = 0x09, ["Escape"] = 0x1B, ["Backspace"] = 0x08,
            ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
            ["ArrowLeft"] = 0x25, ["ArrowUp"] = 0x26, ["ArrowRight"] = 0x27, ["ArrowDown"] = 0x28,
            ["CapsLock"] = 0x14, ["PrintScreen"] = 0x2C, ["ScrollLock"] = 0x91, ["Pause"] = 0x13,
            ["Minus"] = 0xBD, ["Equal"] = 0xBB, ["BracketLeft"] = 0xDB, ["BracketRight"] = 0xDD, ["Backslash"] = 0xDC,
            ["Semicolon"] = 0xBA, ["Quote"] = 0xDE, ["Backquote"] = 0xC0, ["Comma"] = 0xBC, ["Period"] = 0xBE, ["Slash"] = 0xBF,
            ["AudioVolumeUp"] = 0xAF, ["AudioVolumeDown"] = 0xAE, ["AudioVolumeMute"] = 0xAD,
            ["MediaPlayPause"] = 0xB3, ["MediaTrackNext"] = 0xB0, ["MediaTrackPrevious"] = 0xB1, ["MediaStop"] = 0xB2,
            ["NumpadAdd"] = 0x6B, ["NumpadSubtract"] = 0x6D, ["NumpadMultiply"] = 0x6A, ["NumpadDivide"] = 0x6F, ["NumpadDecimal"] = 0x6E,
        };
        for (char c = 'A'; c <= 'Z'; c++)
            keys[$"Key{c}"] = c;
        for (int d = 0; d <= 9; d++)
        {
            keys[$"Digit{d}"] = (ushort)('0' + d);
            keys[$"Numpad{d}"] = (ushort)(0x60 + d);
        }
        for (int f = 1; f <= 24; f++)
            keys[$"F{f}"] = (ushort)(0x70 + f - 1);
        return keys;
    }
}
