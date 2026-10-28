# MX Controls

A Windows alternative to Logi Options+ for the MX Master 4. It talks to the mouse directly over HID++ 2.0, Logitech's vendor protocol, and is planned as a WinUI 3 app with a background agent.

The first goal is something Options+ can't do: buttons that behave as **held** keyboard keys. Options+ only sends a keystroke that is pressed and released at once.

## Status

`MxControls.Core` reads and writes the mouse's settings, and `mx` is a command-line tool for trying it on hardware. The WinUI app comes next.

Only direct Bluetooth connections are supported. A Bolt receiver uses a different collection and device indexes.

## Build and run

Requires Windows x64 and the .NET 10 SDK. There are no NuGet dependencies; HID access goes through `hid.dll` and `cfgmgr32.dll`.

```powershell
dotnet build native/MxControls.Cli -c Release
.\native\MxControls.Cli\bin\Release\net10.0-windows10.0.19041.0\mx.exe info
```

| Command | What it does |
| --- | --- |
| `mx info` | Feature table, firmware, battery, buttons and settings. Read-only. |
| `mx listen [seconds] [--all]` | Logs presses and releases of buttons another app has diverted. Read-only unless `--all`, which diverts every button for the session to identify it and restores them afterwards. |
| `mx haptic <waveform>` | Plays a haptic waveform, such as `Completed` or `Knock`. |
| `mx hold <button>=<key> ... [--seconds N]` | Holds keyboard keys while mouse buttons are held, then restores the buttons. |

Buttons are `middle`, `back`, `forward`, `gesture`, `smartshift` and `haptic`. Keys are `A`–`Z`, `0`–`9`, `F1`–`F24`, modifiers such as `Shift`, names such as `Space` or `PageUp`, or a virtual-key code such as `0x7C`.

```powershell
mx hold middle=Shift back=F13 --seconds 120
```

Set `MX_TRACE=1` to print every event the mouse sends.

Held keys are verified on hardware: the wheel click mapped to Shift sent key-down on press and key-up on release, including a 0.62 s hold and quick taps.

## What the MX Master 4 supports

Read from an MX Master 4 for Mac (PID `B042`, firmware RBM 27.03 build 0019) over Bluetooth.

| Area | Feature | Notes |
| --- | --- | --- |
| Buttons | `REPROG_CONTROLS_V4` (`1B04`) v6 | Middle, Back, Forward, Gesture, Smart Shift and Haptic can be diverted. Left and Right can't. |
| DPI | `ADJUSTABLE_DPI` (`2201`) | 200–8000 in steps of 50. |
| Smart Shift | `SMART_SHIFT_ENHANCED` (`2111`) | Ratchet or free spin, the speed that switches to free spin, and ratchet torque. |
| Main wheel | `HIRES_WHEEL` (`2121`) | High resolution (15× multiplier), invert and diversion. |
| Thumb wheel | `THUMB_WHEEL` (`2150`) | Diversion and invert; 120 counts per turn when diverted. |
| Haptics | `HAPTIC` (`19B0`) | Level 0–100 or off, and 16 waveforms the host can play. |
| Haptic button force | `FORCE_SENSING_BUTTON` (`19C0`) | Actuation force, 3913–6505. |
| Easy-Switch | `CHANGE_HOST` (`1814`), `HOSTS_INFO` (`1815`) | Not wrapped yet. |
| Battery | `UNIFIED_BATTERY` (`1004`) | Percentage and charging state. |

There is no `ONBOARD_PROFILES` (`8100`) feature. The firmware can't store "this button is a keyboard key", so held keys need software on the computer:

1. Divert the button. It stops acting natively, and the mouse reports which diverted buttons are held each time that changes.
2. Compare each report with the last one and send key-down and key-up through `SendInput`.

None of the buttons are persistently divertable. Diversion is lost when the mouse reconnects, sleeps or changes host, so the agent has to divert again each time.

Gestures, the Actions Ring and per-app profiles are host features in Options+ as well. Gestures come from raw XY reports while the gesture button is held, and the Actions Ring is a window the host draws.

The HAPTIC and FORCE_SENSING_BUTTON layouts come from [Solaar](https://github.com/pwr-Solaar/Solaar); Logitech doesn't publish them.

## Things to know

- **Logi Options+** diverts Back, Forward, Gesture, Smart Shift, Haptic and the thumb wheel. While it runs, it acts on those buttons too. `mx hold` warns when a button was already diverted.
- **Elevated windows** don't receive injected keys unless MX Controls also runs as administrator.
- **Games** that ignore injected input won't see the keys. Keys are sent as scan codes, which most games accept.
- **Firmware updates** are left to Logitech's tools. The update features are hidden and undocumented.

## Source layout

- `native/MxControls.Core/Hid/`: HID collection enumeration and overlapped I/O.
- `native/MxControls.Core/Hidpp/`: the HID++ 2.0 channel, which matches replies to requests and raises device events, and feature IDs.
- `native/MxControls.Core/Features/`: buttons, DPI, wheels, haptics and device information.
- `native/MxControls.Core/Input/`: key names and `SendInput`.
- `native/MxControls.Core/Remap/HoldRemapper.cs`: held-key remapping and restoring the original diversion.
- `native/MxControls.Cli/`: the `mx` tool.

The HID++ channel shares the vendor collection with Options+. Requests use software ID `0x0A`, so its replies are told apart from Options+'s.
