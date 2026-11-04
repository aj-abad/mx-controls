# MX Studio

A cross-platform alternative to Logi Options+ for the MX Master 4. It talks to the mouse directly over HID++ 2.0, Logitech's vendor protocol.

The first goal is something Options+ can't do: buttons that behave as **held** keyboard keys. Options+ only sends a keystroke that is pressed and released at once.

## How it fits together

| Part | What it is |
| --- | --- |
| `agent/MxControls.Agent` | `mx-agent`, a background process built with NativeAOT. It owns the mouse, applies button mappings, reapplies them after reconnects and serves a local pipe. |
| `agent/MxControls.Core` | HID++ protocol, device features, and the button engine that runs actions and gestures. It makes no OS calls. |
| `agent/MxControls.Platform.Windows` | HID access through `hid.dll` and `cfgmgr32.dll`, key and wheel input through `SendInput`, system actions and the sign-in Run key. |
| `agent/MxControls.Tests` | Unit tests for the engine, bindings, thumb wheel and settings. |
| `agent/MxControls.Cli` | `mx`, for trying things against the mouse directly. |
| `desktop/` | The app: Tauri, Nuxt, Tailwind and reka-ui, with a Profiles workspace and shared gesture and macro libraries. It follows the system appearance. Its Rust side relays requests and events between the window and the agent's pipe. |

The agent keeps running when the window closes, so mappings keep working. The app starts it if it isn't running.

macOS needs `MxControls.Platform.MacOS`: HID through IOKit's `IOHIDManager`, keys through `CGEventPost` (which needs the Accessibility permission), and a login item. The rest, including the app's Unix-socket path to the agent, is already cross-platform.

## Build and run

Requires the .NET 10 SDK, Rust, Node 24 and pnpm 11.

```bash
cd desktop
pnpm install
pnpm app      # tauri dev
pnpm build    # installer in src-tauri/target/release/bundle
```

The agent ships inside the app as the Tauri sidecar `mx-agent`, compiled with .NET NativeAOT: one native executable of about 4 MB that needs no .NET runtime. `pnpm agent` publishes it to `src-tauri/binaries/mx-agent-<target triple>`. `pnpm app` and `pnpm build` run it first, and it skips the publish when no agent source changed. NativeAOT needs the platform's native toolchain: Visual Studio's C++ build tools on Windows, Xcode's command-line tools on macOS.

The window starts the sidecar beside it unless an agent is already running; set `MX_AGENT_PATH` to use another build. The Windows installer stops a running agent before replacing or removing it, and the app starts it again on its next launch.

### Deploying a portable copy

```bat
deploy.cmd
```

It builds MX Studio as one portable exe, closes the open window, copies the exe to `D:\Programs\MX Studio.exe` and starts it again. Pass `-Destination <path>` for another location or `-SkipBuild` to deploy the last build. A failed build leaves the running copy alone.

The portable exe is a release build with the `embedded-agent` feature, so it carries the agent inside it. On launch it unpacks the agent to `%LOCALAPPDATA%\MxControls\mx-agent.exe` and starts it from there, so the sign-in task points there too. When the exe carries a different agent from the one already unpacked, it stops the old one with `--stop` and replaces it before connecting. The agent keeps the buttons working through the build, until the new window takes over. The installer's `MX Studio` folder is a different place, so the two don't mix.

### Icons

`desktop/src-tauri/icons/app-icon.png` is the Windows icon; `pnpm tauri icon src-tauri/icons/app-icon.png` regenerates `icon.ico` and the PNGs from it. That command also writes `icon.icns` and Android and iOS folders: keep only the files already there, and make `icon.icns` from a copy of the image shrunk to 824 px on a 1024 px transparent canvas instead, since macOS before 26 expects that margin.

macOS 26 uses the Icon Composer icon `icons/AppIcon.icon`. `pnpm build` on a Mac compiles it into `Contents/Resources/Assets.car` with `pnpm mac-icon` (Xcode 26's `actool`), and `src-tauri/Info.plist` names it with `CFBundleIconName`. Elsewhere that step does nothing.

To preview the window in a browser with sample values and no mouse, run `pnpm dev` and open `http://127.0.0.1:3000`. Outside Tauri the app uses `app/utils/preview.ts`.

The Profiles page draws hotspots over `public/mx-master-4.png`, Logitech's product photo, cropped to the mouse. Their positions are defined against the photo in `app/utils/controlMap.ts` and drawn by `app/components/ControlMap.vue`. The photo is Logitech's, so keep it out of anything published.

The current UI direction and follow-up scope for guided gesture rehearsal and reminders are in [Profiles and gesture controls](docs/ui-redesign.md).

The macro recorder's editing commands, timeline gestures, shortcuts and layout contract are in [Macro editor](docs/macro-editor.md).

For automated checks of the actual Windows desktop shell, run `pnpm test:native` from `desktop`. It launches a hidden Tauri/WebView2 instance with an isolated simulated agent, without desktop automation or real-device changes. Setup, artifacts, and coverage are in [Native UI tests](docs/native-testing.md).

```bash
pnpm typecheck
dotnet test ../agent/MxControls.Tests
```

The tests cover gesture direction, multi-click counting, action timing and key order, binding validation, thumb wheel scaling, profiles, settings migration and the JSON shape. Gestures, the thumb wheel, Easy-Switch and injected input on real hardware need checking by hand.

## Agent protocol

One JSON object per line over the pipe `mx-controls-agent`: a named pipe on Windows, `$TMPDIR/CoreFxPipe_mx-controls-agent` elsewhere. Only the current user can connect.

| Request | Params |
| --- | --- |
| `getState` | — |
| `setBindings` | `{ bindings: [binding], profile?, layer? }`; `profile` defaults to the running one, and `layer: true` sets the second layer; see below |
| `setThumbWheel` | `{ thumbWheel, profile?, layer? }`; in the second layer, `thumbWheel: null` keeps the main layer's settings |
| `switchProfile` | `{ id }` of a profile without programs; it becomes the chosen profile |
| `setProfilePrograms` | `{ id, programs }`, executable file names such as `code.exe`, replacing the profile's |
| `listPrograms` | —; answers `[{ id, name }]` for the programs with a window open |
| `createProfile` | `{ name, copyFrom?, icon? }`; the new profile becomes the chosen one, a copy of `copyFrom` without its programs, or with every control at its default |
| `renameProfile` | `{ id, name }` |
| `setProfileIcon` | `{ id, icon }`; a Phosphor icon name in kebab case, or null for the profile's initial |
| `deleteProfile` | `{ id }`; the last one stays |
| `setStartAtLogin` | `{ enabled }` |
| `setMultiClickMs` | `{ ms }`, 150 to 600; how long a click waits for another, on every button with a double click |
| `setGesturePreset` | `{ preset }`; adds a gesture preset or replaces the one with its `id`. A built-in one keeps its name |
| `setMacro` | `{ macro }`; adds a macro or replaces the one with its `id` |
| `deleteMacro` | `{ id }`. Actions that run it do nothing until it's set again with the same `id` |
| `deleteGesturePreset` | `{ id }`; a built-in one goes back to how it came. Buttons that follow a deleted one keep only their clicks, and get their moves back if it's set again with the same `id` |
| `shutdown` | —; the agent hands the buttons back and exits |
| `setDpi` | `{ dpi }` |
| `setSmartShift` | `{ ratchet, autoDisengage, torque }` |
| `setHapticLevel` | `{ level }`, 0 for off |
| `playHaptic` | `{ waveform }`, such as `Completed` |
| `setForce` | `{ force }` |
| `switchHost` | `{ host }`, an Easy-Switch channel from 0; the mouse leaves for that computer |
| `setHostName` | `{ name }` for this computer's channel |

Requests carry an `id` and get `{ id, result }` or `{ id, error }`. The agent pushes these events:
- `{ event: "state", data }` on connect and on every change.
- `{ event: "button", data: { button, cid, down } }` for presses of diverted buttons.
- `{ event: "gesture", data: { button, direction, path, phase } }` when a gesture's path changes (`phase: "step"`) or it runs (`"fire"`). `path` has one direction per step and `direction` is its last.

The types are in `agent/MxControls.Agent/Protocol.cs`, mirrored in `desktop/app/composables/useAgent.ts`.

Every state carries a `revision`, which goes up with each change. A client keeps the highest one it has seen and ignores older replies and events. Revisions start again when the agent restarts.

The agent runs device work one request at a time. A request that sets one setting, such as `setDpi`, `setThumbWheel`, `setBindings` or `switchProfile`, is replaced by a newer request for the same setting while it waits its turn. Everyone who sent one gets the state left by the change that ran, so a burst of toggles reaches the mouse as at most two writes. Changes to bindings, the thumb wheel or the active profile only write the controls that actually change, and the agent updates the state it already holds instead of reading the mouse again. A change runs on the mouse before it's saved. If the mouse refuses it, the agent puts back what ran before, keeps the old settings and answers with the error. A call that gets no reply in 2 s is sent once more, and the error names the feature and function when the second try fails too.

The app shows a setting's new value as soon as it changes, by applying the request to its copy of the state with `app/utils/preview.ts`. It sends the request 150 ms after the last change to that setting. When the agent answers, its state takes over. A refused change goes back to the agent's value, with a toast.

A binding gives one button, `middle`, `back`, `forward`, `gesture`, `smartshift` or `haptic`, either one action or a gesture that follows a preset, with `action` as its click:

```json
{ "button": "smartshift", "mode": "action", "action": { "type": "press", "keys": ["MediaPlayPause"] } }
{ "button": "gesture", "mode": "gesture", "action": { "type": "system", "system": "taskView" }, "preset": "desktops" }
```

A gesture preset holds the moves, and every button that follows it changes when it does, in every profile. The state lists them as `gesturePresets` (the built-in `desktops`, `browser` and `media`, as edited, then the user's own), the built-ins as they come as `builtInPresets`, and which buttons follow which as `presetUses`:

```json
{ "id": "tiling", "name": "Window tiling", "up": …, "down": …, "left": …, "right": { "type": "none", "right": { "type": "press", "keys": ["MetaLeft", "ArrowRight"] } }, "spacingMm": 6 }
```

Settings from before presets carried a whole `gesture` on the binding, its clicks included. The agent moves each into a preset when it loads them: gestures with the same moves share one, and moves matching a built-in follow it.

An action's `type` is one of these:
- `hold`, a key in the app: keeps `keys` down for as long as the button is held.
- `press`, a macro in the app: taps them.
- `system`: runs `taskView`, `showDesktop`, `zoomIn` or `zoomOut`.
- `click`: holds the mouse button `mouse`, `back`, `forward` or `middle`, for as long as the button is held. Back and forward are the side buttons apps take as history.
- `macro`: runs the saved macro whose ID is `macro`.
- `layer`: `layer: "momentary"` keeps the profile's second layer on while the button is held; `"toggle"` turns it on or off with each press.
- `profile`: chooses the profile whose ID is `profile`, which must have no programs.
- `none`: does nothing.

Holding the layer needs a press that lasts, so it's only for a button's single click with no double click set. A gesture's click and its steps can toggle the layer, and the thumb wheel can't switch layers or profiles. Neither repeats.

A macro is named and kept in the settings, belonging to no profile, so every action that runs it changes when it does. The state lists them as `macros`, and where each runs as `macroUses`:

```json
{ "id": "signoff", "name": "Sign off", "steps": [
  { "type": "keys", "keys": ["ControlLeft", "End"] }, { "type": "wait", "ms": 50 }, { "type": "text", "text": "Best,\nAJ" } ] }
```

A step is `keys`, tapped together; `text`, typed as written whatever the keyboard layout; `wait`, for `ms`; or `click`, a tap of the mouse button `mouse`. A macro waits at most a second in all. One without waits runs at once, like `press`; one that waits runs in the background, one macro at a time, and a repeat that comes while it's still running is skipped. `repeat: true` on the action repeats it while the button is held.

Keyboard shortcuts use `press` with one key combination saved directly on the assigned action. They stay independent of the shared macro library, including after settings are reloaded. The picker groups Keyboard shortcut and Hold keys separately from Shared macros. Existing macros remain shared; Use as shortcut explicitly copies a one-combination macro into the current action without changing the saved macro.

Keys are `KeyboardEvent.code` names, modifiers first. `repeat: true` repeats a `press` or `system` action while the button is held: after 300 ms, then every 200 ms.

A gesture button is diverted with raw XY, so the pointer stays still while it's held. Moving 2 mm in one direction, measured at the mouse's current DPI, fires that direction with a haptic tap the moment the movement gets there; a quick flick fires as readily as a slow drag. A movement more than about 31 degrees off an axis waits until it straightens out. Letting go before 2 mm fires `click`. The first raw XY report of each press carries stale movement from before it and is ignored.

While the button stays down, every direction can fire again, measured from where the movement turned: moving back 2 mm fires the opposite direction, and turning fires the new one. A direction with `repeat: true` fires again for every 4 mm it keeps going, so volume up keeps raising the volume and moving back lowers it at once; it taps its action each time instead of repeating on a timer. Once it fires, the rest of the press is locked to its axis, so drifting sideways while changing the volume never skips a track. A direction without it fires once per stroke, and again after a 150 ms pause.

Any direction can lead further: the same way again, up to 3 steps in a run, or at right angles, up to 2 turns. A step does one or the other. Each further step in a run is `spacingMm` on (6 mm by default, 4 to 15), and a turn is 2 mm at right angles. A first direction that leads on doesn't fire as it's reached: the press follows the path, with a haptic tap at each step, a different one at a step that turns, and a softer one backing off. Movement across a run, and onwards at a step that turns, is dropped. Letting go runs the step the path ends on, as a tap; a step of type `none` is a junction that runs nothing, and backing all the way out cancels. Only a first direction with nothing beyond can `hold`. Any step can `repeat` if it's the last in its direction: it runs as it's reached, again for every 4 mm the movement keeps going, and not again on release. Backing off it stops it, and it can still turn. Adding a step further in its direction turns its repeat off.

Both modes take optional double and triple clicks, `doubleClick` and `tripleClick` beside `action`. For a gesture, a click is a press that picks no direction; for an action button, every press is one.

- **No double click set.** Nothing changes: an action button's action lasts as long as the press, and a gesture's click runs on release.
- **A double click set.** Each click runs once, as a tap, and the agent refuses `hold` or `repeat` on any of them. A release that could still become a higher count waits up to `multiClickMs` (300 ms by default) for the next press. The highest count set runs as soon as it's reached, and a press that turns into a gesture direction first runs the clicks waiting before it.
- **Triple click.** It needs a double click.

Each click that runs sends a `click` event, `{ button, count }`; gesture directions and paths send `gesture` events.

The thumb wheel's settings are `{ mode, speed, inverted, left, right }`. `mode` is `scroll`, which scrolls sideways, or `actions`, which runs `left` or `right` once for each step, as a tap. `speed` is a percentage from 25 to 300. At its default, `scroll` at 100% and not inverted, the agent leaves the wheel alone, and plain inverted scrolling is a setting on the mouse. Anything else diverts the wheel, and the agent sends the scrolling itself through `MOUSEEVENTF_HWHEEL`. At 100%, one count of the wheel's native resolution scrolls one notch or runs one step. Which way the diverted wheel counts as right hasn't been checked on hardware yet; if it comes out backwards, flip the sign in `ThumbWheelMapper`.

A profile holds bindings and thumb wheel settings, and a second layer of each. DPI, SmartShift and haptics are stored on the mouse and stay the same across profiles. The state carries `profiles`, each with everything it holds (`{ id, name, icon, programs, bindings, thumbWheel, layer: { bindings, thumbWheel } }`), so the app can edit one that isn't running; `activeProfile`, the one running, with its main `bindings` and `thumbWheel` repeated at the top; `selectedProfile`; `layerActive`; and `followsPrograms`, false where the platform can't tell which program is in front.

**Layers.** The second layer runs over the main one while a button holding it is down, or since a toggle turned it on. A button it leaves unbound runs its main binding, and a null thumb wheel keeps the main layer's. Every button bound in either layer stays diverted, so switching layers writes nothing to the mouse, except to change raw XY for a button that's a gesture in one layer and not the other. A press runs the binding it went down with until its release, whatever the layer does meanwhile. A button bound only in the second layer still does its own job in the main one: back, forward and middle are clicked for it, and Smart Shift switches the wheel's ratchet. A profile that starts running starts on its main layer.

**Programs.** A profile can follow programs, by executable file name. While one of them is in front, that profile runs; otherwise the chosen profile, `selectedProfile`, does. Only a profile without programs can be chosen, by `switchProfile` or a `profile` action, and one profile always stays without programs; if the chosen one gains programs or is deleted, the first profile without programs takes over. A program follows at most one profile. On Windows the agent follows the foreground window with a `SetWinEventHook` for `EVENT_SYSTEM_FOREGROUND`, so a switch takes effect as it happens.

Easy-Switch comes from CHANGE_HOST and HOSTS_INFO, read with Solaar's layouts. `device.hosts` lists each channel, whether a computer is paired on it, and the name that computer gave the mouse. Switching sends the request without waiting for an answer, because the mouse leaves at once. The agent notices it has gone and connects again when it comes back.

Settings are saved in `%APPDATA%\MxControls\settings.json`, or `~/Library/Application Support/MxControls` on macOS. Older held-key `mappings` and bindings from before profiles become the `Default` profile on first load. The agent logs to `Logs/agent.log` in the same folder.

With start at sign-in on, which is the default, the agent registers itself as the scheduled task `MX Studio Agent` each time it starts, so the task always points at the copy that last ran. The task runs it at sign-in, in your session, and restarts it a minute after it crashes. A clean exit isn't a failure, so a stopped agent stays stopped. Only instances the task started are restarted. While the window is open, the app also restarts an agent it can't reach.

The agent isn't a Windows service: a service runs in session 0, outside the desktop, so it couldn't send keys. On macOS the same role belongs to a per-user LaunchAgent in `~/Library/LaunchAgents`, with `RunAtLoad`, and `KeepAlive` set to restart only after an unsuccessful exit. That belongs with the macOS backend.

`mx-agent --stop` asks a running agent to exit cleanly. `pnpm app` and `pnpm build` use it when an agent is running from the build folder, which would stop the sidecar from being replaced; an agent running from anywhere else keeps running. The portable exe uses it before replacing the agent it unpacked. The installer uses it before replacing or removing the agent, and on uninstall it deletes the task and the Run-key entry older builds used.

## The `mx` command

```bash
dotnet build agent/MxControls.Cli -c Release
agent/MxControls.Cli/bin/Release/net10.0/mx info
```

| Command | What it does |
| --- | --- |
| `mx info` | Feature table, firmware, battery, buttons, settings and Easy-Switch channels. Read-only. |
| `mx listen [seconds] [--all]` | Logs presses of buttons another app has diverted. `--all` diverts every button for the session to identify it, then restores them. |
| `mx gestures [seconds] [--repeat] [--trace <file>]` | Diverts the gesture button and logs each swipe's reports, travel and what it would fire, without running anything. `--repeat` acts as if every direction repeats; `--trace` saves the swipes as JSON lines for tuning. |
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
