# MX Controls

A cross-platform alternative to Logi Options+ for the MX Master 4. It talks to the mouse directly over HID++ 2.0, Logitech's vendor protocol.

The first goal is something Options+ can't do: buttons that behave as **held** keyboard keys. Options+ only sends a keystroke that is pressed and released at once.

## How it fits together

| Part | What it is |
| --- | --- |
| `agent/MxControls.Agent` | `mx-agent`, a background process built with NativeAOT. It owns the mouse, applies button mappings, reapplies them after reconnects and serves a local pipe. |
| `agent/MxControls.Core` | HID++ protocol, device features and held-key remapping. It makes no OS calls. |
| `agent/MxControls.Platform.Windows` | HID access through `hid.dll` and `cfgmgr32.dll`, and key injection through `SendInput`. |
| `agent/MxControls.Cli` | `mx`, for trying things against the mouse directly. |
| `desktop/` | The app: Tauri, Nuxt and Tailwind, styled with Arcon Sine. Its Rust side relays requests and events between the window and the agent's pipe. |

The agent keeps running when the window closes, so mappings keep working. The app starts it if it isn't running.

macOS needs `MxControls.Platform.MacOS`: HID through IOKit's `IOHIDManager`, keys through `CGEventPost` (which needs the Accessibility permission), and a login item. The rest, including the app's Unix-socket path to the agent, is already cross-platform.

## Build and run

Requires the .NET 10 SDK, Rust, Node 24 and pnpm 11. Sine comes from Arcon's registry, so sign in once:

```bash
npm login --registry https://packages.arcon.mobi/ --auth-type=legacy
```

```bash
cd desktop
pnpm install
pnpm app      # tauri dev
pnpm build    # installer in src-tauri/target/release/bundle
```

The agent ships inside the app as the Tauri sidecar `mx-agent`, compiled with .NET NativeAOT: one native executable of about 4 MB that needs no .NET runtime. `pnpm agent` publishes it to `src-tauri/binaries/mx-agent-<target triple>`. `pnpm app` and `pnpm build` run it first, and it skips the publish when no agent source changed. NativeAOT needs the platform's native toolchain: Visual Studio's C++ build tools on Windows, Xcode's command-line tools on macOS.

The window starts the sidecar beside it unless an agent is already running; set `MX_AGENT_PATH` to use another build. The Windows installer stops a running agent before replacing or removing it, and the app starts it again on its next launch.

To preview the window in a browser with sample values and no mouse, run `pnpm dev` and open `http://127.0.0.1:3000`. Outside Tauri the app uses `app/utils/preview.ts`.

```bash
pnpm typecheck
pnpm exec sine lint
```

## Agent protocol

One JSON object per line over the pipe `mx-controls-agent`: a named pipe on Windows, `$TMPDIR/CoreFxPipe_mx-controls-agent` elsewhere. Only the current user can connect.

| Request | Params |
| --- | --- |
| `getState` | — |
| `setMappings` | `{ mappings: [{ button, key }] }`; `button` is `middle`, `back`, `forward`, `gesture`, `smartshift` or `haptic`, and `key` is a `KeyboardEvent.code` such as `ShiftLeft` |
| `setDpi` | `{ dpi }` |
| `setSmartShift` | `{ ratchet, autoDisengage, torque }` |
| `setHapticLevel` | `{ level }`, 0 for off |
| `playHaptic` | `{ waveform }`, such as `Completed` |
| `setForce` | `{ force }` |

Requests carry an `id` and get `{ id, result }` or `{ id, error }`. The agent pushes `{ event: "state", data }` on connect and on every change, and `{ event: "button", data: { button, cid, down } }` for presses of diverted buttons. The types are in `agent/MxControls.Agent/Protocol.cs`, mirrored in `desktop/app/composables/useAgent.ts`.

Mappings are saved in `%APPDATA%\MxControls\settings.json`, or `~/Library/Application Support/MxControls` on macOS. The agent logs to `Logs/agent.log` in the same folder.

## The `mx` command

```bash
dotnet build agent/MxControls.Cli -c Release
agent/MxControls.Cli/bin/Release/net10.0/mx info
```

| Command | What it does |
| --- | --- |
| `mx info` | Feature table, firmware, battery, buttons and settings. Read-only. |
| `mx listen [seconds] [--all]` | Logs presses of buttons another app has diverted. `--all` diverts every button for the session to identify it, then restores them. |
| `mx haptic <waveform>` | Plays a haptic waveform. |
| `mx hold <button>=<key> ... [--seconds N]` | Holds keyboard keys while mouse buttons are held, then restores the buttons. |

Set `MX_TRACE=1` to print every event the mouse sends. `mx` and the agent can run at the same time.

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
2. Compare each report with the last one and send key-down and key-up.

None of the buttons are persistently divertable. Diversion is lost when the mouse reconnects, sleeps or changes host. The agent diverts again when it reconnects or when the mouse asks for reconfiguration.

Gestures, the Actions Ring and per-app profiles are host features in Options+ as well. Gestures come from raw XY reports while the gesture button is held, and the Actions Ring is a window the host draws.

The HAPTIC and FORCE_SENSING_BUTTON layouts come from [Solaar](https://github.com/pwr-Solaar/Solaar); Logitech doesn't publish them.

## Things to know

- **Logi Options+** diverts Back, Forward, Gesture, Smart Shift, Haptic and the thumb wheel. While it runs, it acts on those buttons too. The app flags mapped buttons Options+ also uses.
- **Elevated windows** don't receive injected keys unless the agent also runs as administrator.
- **Games** that ignore injected input won't see the keys. Keys are sent as scan codes, which most games accept.
- **Firmware updates** are left to Logitech's tools. The update features are hidden and undocumented.
- **Bolt receivers** aren't supported yet; only direct Bluetooth is.

The HID++ channel shares the vendor collection with Options+. Requests use software ID `0x0A`, so its replies are told apart from Options+'s.
