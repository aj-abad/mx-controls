# Gesture menus

A replacement for the Actions Ring. Hold a button and move the mouse: the direction picks an arm, how far you move picks a stop on it, and a stop can be a branch that opens more arms, like a folder in the Actions Ring. Haptic ticks mark each stop, so it can be used without looking. An overlay appears only when you hesitate.

Status: draft, not built.

## Terms

| Term | Meaning |
| --- | --- |
| Menu | What a button runs in the new `menu` binding mode: a root level, a click action and a stop spacing. |
| Level | Up to four arms: up, down, left, right. The root is a level, and so is each branch. |
| Arm | A direction on a level, holding 1–3 stops. |
| Stop | A point at a set distance along an arm. It runs an action or opens a branch. |
| Branch | A stop that opens a new level. It is always the last stop on its arm. |
| Origin | Where distances on a level are measured from: the press point for the root, the branch stop for a branch. |
| Path | Where you are, such as `right 2 → up 1`. |

## Interaction

1. **Press.** The pointer freezes, as gestures already do, and movement is tracked from the origin.
2. **Lock an arm.** Moving 3 mm in a clear direction locks that arm. While an arm is locked, movement across it is dropped, so drift can't pick anything else.
3. **Reach stops.** Stop *k* starts at *k* × spacing from the origin. Between the lock and the first stop, nothing is selected.
4. **Go back.** Moving back along the arm lowers the stop. Back within 3 mm of the origin, the arm unlocks and another can be picked.
5. **Enter a branch.** Reaching a branch stop enters its level straight away, with the origin at the stop. Its arms point forward and to either side. Moving the other way, back toward the parent's origin, locks no arm in the branch. It goes back to the parent level instead, with the parent's arm locked again.
6. **Release.**
   - On an action stop, its action runs once as a tap.
   - If no arm was ever locked during the press, the menu's click action runs.
   - Anything else runs nothing. Moving back to the origin before releasing is how you cancel.

### Instant arms

A root arm with exactly one stop that runs an action fires when the arm locks, just as the current gestures do. Its action is held until the button comes up, so hold, repeat and click actions keep working. The rest of the press is then ignored. This keeps today's four-direction gestures exactly as they behave now (see Migration).

## Geometry

| Setting | Value | Notes |
| --- | --- | --- |
| Dead zone | 3 mm | Locks an arm, unlocks it near the origin, and separates a click from a gesture. Same as `GestureTracker.DefaultThresholdMm`. |
| Direction lock | Other axis ≤ 0.5 × travel | Same as `GestureTracker.CrossRatio`, about 27° each side of the arm. |
| Stop spacing | 8 mm default, 5–15 mm | Per menu, not per stop, so muscle memory carries across arms. Needs tuning on the mouse. |
| Hysteresis | 1.5 mm | A stop is entered at *k* × spacing and left below *k* × spacing − 1.5 mm, so hovering on an edge doesn't flicker or spam haptics. |
| Stops per arm | 1–3 | |
| Depth | Root plus one level of branches | The model allows more; validation refuses it in v1. A path is at most 2 × 3 × spacing, 48 mm at the default spacing. |

Distances are raw sensor counts converted to mm with the mouse's DPI (`ButtonEngine.Dpi`). Raw XY skips Windows pointer acceleration, so a distance means the same physical movement every time.

Capacity: the root has 4 arms × 3 stops. A branch has 3 arms × 3 stops, so 9 items, against an Actions Ring folder's 8.

## Haptics

| Moment | Waveform | |
| --- | --- | --- |
| Entering an action stop | `SubtleCollision` | Today's default for gestures; follows `ButtonEngine.GestureHaptic` |
| Entering a branch | `DampStateChange` | |
| Going back to the parent level | `WhisperCollision` | |
| An instant arm firing | `SubtleCollision` | As now |
| Release | none | The action itself is the feedback |

- All taps go through `HapticSettings.TapAsync`, which doesn't wait for the mouse to reply.
- Taps are at least 40 ms apart. A tap that comes too soon replaces the pending one, so a fast sweep plays where you ended up instead of a backlog of where you've been.
- No taps play when haptics are off on the mouse.

## Which buttons

Any button that can report movement while diverted. `mx info` on 2026-10-30 shows raw XY on Middle, Back, Forward, Gesture, Smart Shift and Haptic. Haptic (`0x01A0`) is the default home for a menu because it's where Options+ puts the Actions Ring.

## Data model

Core, beside `ButtonBinding`:

```csharp
/// <param name="SpacingMm">Distance between stops, 5–15.</param>
/// <param name="Click">Runs when the button is released without locking an arm.</param>
public sealed record GestureMenu(GestureLevel Root, ButtonAction? Click = null, double SpacingMm = 8);

public sealed record GestureLevel(
    IReadOnlyList<GestureStop>? Up = null, IReadOnlyList<GestureStop>? Down = null,
    IReadOnlyList<GestureStop>? Left = null, IReadOnlyList<GestureStop>? Right = null);

/// <summary>Runs <see cref="Action"/> or opens <see cref="Branch"/>, exactly one of them.</summary>
/// <param name="Label">Shown in the overlay and editor; defaults to a description of the action.</param>
public sealed record GestureStop(string? Label = null, ButtonAction? Action = null, GestureLevel? Branch = null);
```

`ButtonBinding` gains `Mode = "menu"` and a `Menu` property. A branch's arms are absolute directions, matching how they're drawn.

`ButtonBinding.Problem` refuses:
- an arm with no stops, or more than 3
- a stop with both or neither of action and branch
- a branch that isn't the last stop on its arm
- a branch level with an arm pointing back toward its parent
- a branch inside a branch
- a spacing outside 5–15 mm
- any action that `ButtonAction.Problem` refuses

The TypeScript types in `useAgent.ts` mirror these records.

## Agent

### Tracker

`GestureMenuTracker` in `MxControls.Core/Actions` is pure logic like `GestureTracker` and is unit tested the same way. It holds the menu, the DPI, the current level stack, the locked arm and a position that only moves along the locked arm.

- `Add(dx, dy)` returns what changed: an arm locked, a stop entered or left, a branch entered, going back to the parent, or an instant arm firing.
- `Release()` returns the action to run, if any.
- `Path` says where it is now.

### Engine

`ButtonEngine` uses `GestureMenuTracker` for menu bindings and `ActionRunner` to run their actions:
- release stops use `Run`
- instant arms use `Begin` and `End`

It turns what the tracker reports into haptic taps and a `MenuChanged` event. The current `gesture` mode and `GestureTracker` go away once migration lands.

### Events

`GestureEvent` is replaced by:

```csharp
/// <param name="Phase">"start" on press, "change" when the path changes, "end" on release.</param>
/// <param name="Fired">On "end": whether an action ran.</param>
public sealed record MenuEvent(string? Button, string Phase, IReadOnlyList<MenuStep> Path, bool Fired);

/// <param name="Stop">1-based; 0 while the arm is locked short of its first stop.</param>
public sealed record MenuStep(string Direction, int Stop);
```

Events go out only when something changes, not on every movement report, so the pipe stays quiet. A live dot following the movement is out of scope for v1.

## Overlay

- **Window.** A second Tauri window: transparent, undecorated, always on top, out of the taskbar and click-through. It must never take focus, because keys the agent sends go to the focused window. That rules out any window that can be activated. Check that Tauri's options can guarantee this on Windows, and fall back to `WS_EX_NOACTIVATE` if they can't.
- **Placement.** Centred on the cursor, on the cursor's monitor, at that monitor's scale. The pointer is frozen while the button is held, so the position doesn't move.
- **Timing.** It appears 350 ms after "start", or after the last "change" while held, and hides at once on "end". A confident movement never shows it.
- **Content.** The current level's arms with a tick for each stop, labels, the selected stop highlighted, and a breadcrumb inside a branch. Labels come from the bindings already in `AgentState`.
- **Lifetime problem.** Today the app quits when its window closes, while the agent keeps running. The overlay needs a process that's always there:
  - **(a) Recommended:** the app stays in the tray with its main window closed and owns the overlay. It works on both platforms and the overlay is written in Vue.
  - **(b)** A native overlay in each platform project of the agent. It always runs, but the overlay gets written once per OS.

## App

- `GesturePad.vue` becomes a menu editor:
  - The root is laid out as a cross, like today.
  - Each arm is a row of up to three stops, with a button to add one.
  - A stop is set to an action with the existing `ActionPicker`, or made into a branch.
  - Opening a branch drills into its level, with a breadcrumb back. The arm pointing back is shown disabled and labelled "Back".
- Stop spacing is a slider in the button's settings.
- The editor lights up the stop at the end of each `MenuEvent` path as it happens, as `GesturePad` does with `lastGesture` now, so you can practise on the real mouse while editing.
- Presets fill a menu in one go, as `GESTURE_PRESETS` does now.

## Migration

`AgentSettings.Migrated()` turns each `gesture` binding into a `menu` binding:
- Each direction becomes a root arm with one stop holding that direction's action, which makes it an instant arm.
- The gesture's click becomes the menu's click.
- An arm whose action is `none` is dropped.
- The spacing is the default.

Behaviour is unchanged. Settings still read `gesture` but are saved as `menu`.

## Tests

`GestureMenuTrackerTests`, with movement in mm converted at a fixed DPI:
- a click under 3 mm, and a drift that stays a click
- locking each arm, and a diagonal that locks nothing
- stops 1–3 by distance, and hysteresis at each boundary
- dropping movement across a locked arm
- unlocking near the origin and picking another arm
- entering a branch, overshooting into its forward arm, its side arms, going back to the parent, and the back arm locking nothing in the branch
- release on an action stop, on a branch with nothing selected, and on an arm short of its first stop
- instant arms firing at lock and ignoring the rest of the press
- DPI changes

Engine tests with `FakePlatform`: release taps, held instant actions, a binding swapped mid-press, haptic tap limits.

On the mouse: tune the spacing and hysteresis, check haptic latency over Bolt and over Bluetooth, and make sure the overlay never takes focus from a game or a full-screen app.

## Build order

1. Data model, validation and migration, with `BindingTests`.
2. `GestureMenuTracker` and its tests.
3. Engine wiring, haptics and `MenuEvent`.
4. Editor in the app.
5. Tray mode and the overlay.

Steps 1–3 already replace today's gestures without any visible change. Step 4 makes branches usable, and step 5 makes them easy to learn.

## Open questions

- **Instant arms:** should they stay automatic, or be a setting per arm?
- **Releasing on a branch with nothing selected:** should it run an optional default action for the branch?
- **Overlay host:** tray app or native per platform?
- **Default spacing:** is 8 mm right, or should it be closer to 6 mm for thumb-held buttons?
- **Hold to reopen:** should holding still on a stop for a while repeat its action (useful for volume), or does that belong only to instant arms?

## Out of scope for v1

- Eight directions
- Branches inside branches
- Menus per app, which come with per-app profiles
- Opening a branch by holding still on it
- A live dot following the movement in the overlay
- The macOS overlay
