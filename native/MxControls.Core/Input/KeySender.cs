using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MxControls.Input;

/// <summary>
/// Sends separate key-down and key-up events through SendInput. Keys with a scan code are sent as scan
/// codes, which games that read raw keyboard input also accept; media keys fall back to virtual keys.
/// </summary>
public static partial class KeySender
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventScanCode = 0x0008;
    private const uint MapVkToVscEx = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
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
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint count, ReadOnlySpan<Input> inputs, int size);

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static partial uint MapVirtualKey(uint code, uint mapType);

    public static void Down(ushort virtualKey) => Send(virtualKey, up: false);

    public static void Up(ushort virtualKey) => Send(virtualKey, up: true);

    private static void Send(ushort virtualKey, bool up)
    {
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

        ReadOnlySpan<Input> input = [new Input { Type = InputKeyboard, Keyboard = keyboard }];
        // SendInput returns 0 when UIPI blocks injection into a more elevated window.
        if (SendInput(1, input, Marshal.SizeOf<Input>()) != 1)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
}
