# Gesture paths

An extension of the gesture engine that replaces the Actions Ring. Today a gesture is a click plus one action for each direction. With paths, any direction can lead to further steps: keep going the same way to reach the next step, or turn to branch off. Haptic ticks mark each step, so it can be used without looking. An overlay appears only when you hesitate. Gestures can be saved as presets and reused on any button and in any profile.

Status: built, except the overlay and the CLI's `--gesture` (steps 3 and 6 of the build order). Presets follow [button-editor-revamp.md](button-editor-revamp.md): buttons follow them rather than copying them.

## Model

A gesture starts as just a click. You add directions to it, and from a direction you can add more:

- **Extend:** another step in the same direction, further along.
- **Branch:** steps at right angles to it. A step that branches can't be extended any further.

So every step is either a leaf, the end of a run of extensions, or a branch point. Its next steps never point back the way it came.

```
            ┌─ up ── "Snap top"
 click ── right ── right ──┤
   │                       └─ down ── "Snap bottom"
   ├─ up ── up ── up            (a run of three)
   └─ left                      (a leaf, as today)
```

A gesture with no steps beyond its first four directions is exactly today's gesture, and it behaves exactly as it does today.

| Term | Meaning |
| --- | --- |
| Step | A point reached by moving in a direction. It holds an action, which can be "none". |
| Run | Steps in one direction, one after another: an extension. |
| Turn | A step at right angles to the one before: a branch. |
| Leaf | A step with nothing beyond it. Every direction in today's gestures is a leaf. |
| Path | The steps reached so far in this press, such as `right, right, up`. |

## When a step's action runs

| Step | Runs | Why |
| --- | --- | --- |
| One of the first four directions, with nothing beyond it | As soon as it's reached, like today's directions. Hold and repeat work as they do now. | This is today's gesture, and it stays instant. |
| Any other step, including a leaf further along a path | On release, as a tap, if the path ends there. | You may be passing through on the way to a further step. And even at the end of a long path, you can still back off and cancel. |
| The click | On release, if no direction was ever picked. | As today. |

A step whose action is "none" is just a junction: releasing on it runs nothing. Moving back to where the press started and releasing cancels.

The editor says which kind a step is, such as "Runs as you reach it" or "Runs when you let go here". Adding a step beyond one of the first four directions changes when its action runs.

## Double and triple clicks

A gesture's click can also have a double-click and a triple-click action, and so can a button in `action` mode. For a gesture, a click is a press that picks no direction, as today. For an action button, every press is a click.

An action button with no double-click action keeps today's behaviour: its action starts on press and ends on release, so holding keys and repeating while held still work. Once it has a double-click action, its action runs once on release, as a tap, like a gesture's click.

**A single click only waits when it has to.** With no double-click action, a single click runs on release, exactly as today. Waiting only starts once a double-click action exists, because then a single click can't run until the next press has had its chance to come.

| Configured | Release 1 | Release 2 | Release 3 |
| --- | --- | --- | --- |
| Click only | Click runs | Click runs again | Click runs again |
| Click and double | Wait | Double runs | A new count starts |
| Click, double and triple | Wait | Wait | Triple runs |

- **Waiting.** After a release that has to wait, the next press must start within the multi-click window. If it doesn't, the count so far runs: a single click, or a double click while waiting for a triple.
- **Window length.** 300 ms by default, adjustable from 150 to 600 ms. It's one setting for the whole app, because click speed belongs to the person, not the button. It isn't taken from Windows' double-click time, whose 500 ms default makes the wait for a single click very noticeable.
- **A press that moves.** If a press within the window picks a direction or starts a path, the waiting clicks run first and the movement is handled as usual. A click followed by a swipe is never lost.
- **Triple needs double.** Validation refuses a triple-click action without a double-click one. Otherwise two clicks would have nothing to run.
- **Taps only.** Double and triple clicks run once, as taps. Hold and repeat aren't offered for them. Once a double click is set, they're no longer offered for the single click either.
- **Binding changes.** If the binding changes while clicks are waiting, they're dropped, the same way a held action ends when its binding changes.

The counting is its own small class, `ClickCounter` in `MxControls.Core/Actions`. It takes times like `GestureTracker` does, so it can be tested without a clock. `ButtonEngine` gives it each click's press and release, and runs a timer for the window under its lock, as `ActionRunner` does for repeats.

**Model.**
- `ButtonBinding` gains `DoubleClick` and `TripleClick` for `action` mode, and `GestureActions` gains the same pair for gestures. Both are null when unset; an action of type `none` counts as unset.
- `ButtonBinding.Clicks` gathers whichever pair applies, so the engine treats both modes alike.

**Agent.**
- `AgentSettings.MultiClickMs` holds the window, set with `setMultiClickMs { ms }` and reported as `multiClickMs` in the state.
- Each click that runs raises a `click` event, `{ button, count }`, for both modes. The `gesture` event now covers directions only.

**App.** The window's slider sits in the click editor, shown once a double click is set, since that's where the wait is felt. It notes that it applies to every button. The editor switches to the count each burst of clicks was read as, which doubles as a way to test the speed.

## Movement

Today's rules carry over unchanged:
- The first movement report of a press is dropped, because it carries stale movement from before the press.
- 2 mm picks a direction, measured at the mouse's DPI.
- A movement more than about 31° off an axis picks nothing (`CrossRatio` 0.6).

Paths add the rest:

1. **First step.** Picking a direction reaches its first step, at 2 mm, as today.
2. **Extending.** Each further step in a run is one spacing further on: 2 mm + (*k* − 1) × spacing.
3. **Staying on course.** While moving along a run, movement across it is dropped, so drift can't turn.
4. **Turning.** At a step that branches, movement onward is dropped, because nothing lies that way. Moving 2 mm at right angles, within the same 31°, takes the turn and reaches that side's first step.
5. **Backing off.** Moving back along a run returns to the step before, with hysteresis. Within 2 mm of where the run began, the run is undone and you're back on the step it left from, free to pick again.
6. **Strokes between instant directions stay as they are.** Once one of the first four directions fires as you reach it, today's stroke rules apply:
   - moving back fires the opposite direction
   - turning fires the new one
   - a 150 ms pause starts a new stroke

   If the new direction leads further, a path starts from where the movement turned.
7. **A path never fires mid-press.** Nothing on a path runs until release, so a pause on a step to read the overlay changes nothing, and backing all the way out always cancels.

| Setting | Value | Notes |
| --- | --- | --- |
| Pick a direction | 2 mm, 31° | `GestureTracker.DefaultThresholdMm` and `CrossRatio`, unchanged |
| Spacing between steps in a run | 6 mm default, 4–15 mm | Per gesture. Larger than the 4 mm repeat step, so they don't get confused. Tune it with traces. |
| Hysteresis | 1.5 mm | When backing off along a run |
| Steps per run | up to 3 | |
| Turns per path | up to 2 | |
| Repeat step | 4 mm | `DefaultRepeatStepMm`, unchanged; only on the first four directions with nothing beyond them |

At the default spacing, the longest run is 14 mm, and a path with both turns stays under about 40 mm.

## Haptics

| Moment | Waveform |
| --- | --- |
| An instant direction firing, or a repeat | `GestureHaptic` (`SubtleCollision`), as today |
| Reaching any other step | `SubtleCollision` |
| Reaching a step that branches | `DampStateChange`, so you can feel that it turns |
| Backing off a step | `WhisperCollision` |

Every tap goes through the existing guard in `ButtonEngine.Fire`: one tap in flight, others skipped while it goes out. Factor the guard into a `Tap(waveform)` helper so steps use it too.

## Data model

`GestureActions` keeps its shape. Its four directions become `GestureStep`: today's action fields plus optional further steps. Today's settings files, the app's types and the built-in presets therefore read unchanged, and nothing needs migrating.

```csharp
/// <param name="DoubleClick">Null when unset, so a single click runs at once.</param>
/// <param name="TripleClick">Null when unset; needs <see cref="DoubleClick"/>.</param>
/// <param name="SpacingMm">Distance between steps in a run, 4–15.</param>
public sealed record GestureActions(
    ButtonAction Click, GestureStep Up, GestureStep Down, GestureStep Left, GestureStep Right,
    ButtonAction? DoubleClick = null, ButtonAction? TripleClick = null, double SpacingMm = 6);

/// <summary>
/// A step's action, in the same fields as <see cref="ButtonAction"/> so today's gestures read unchanged, and the
/// steps beyond it: the same direction to extend, or the two at right angles to branch.
/// </summary>
public sealed record GestureStep(
    string Type, IReadOnlyList<string>? Keys = null, string? System = null, bool Repeat = false, string? Mouse = null,
    GestureStep? Up = null, GestureStep? Down = null, GestureStep? Left = null, GestureStep? Right = null)
{
    [JsonIgnore]
    public ButtonAction Action => new(Type, Keys, System, Repeat, Mouse);

    [JsonIgnore]
    public bool IsLeaf => Up is null && Down is null && Left is null && Right is null;
}
```

The JSON for a gesture in a binding:

```json
{
  "click": { "type": "system", "system": "taskView" },
  "right": {
    "type": "none",
    "right": { "type": "none", "up": { "type": "press", "keys": ["MetaLeft", "ArrowUp"] } }
  },
  "up": { "type": "press", "keys": ["AudioVolumeUp"], "repeat": true },
  "down": { "type": "none" },
  "left": { "type": "none" },
  "spacingMm": 6
}
```

`ButtonBinding.Problem` refuses, beyond what `ButtonAction.Problem` refuses already:
- a step with a next step pointing back the way it came
- a step that both extends and branches
- a run longer than 3, or a path with more than 2 turns
- `repeat` or `hold` on any step except one of the first four directions with nothing beyond it, since every other step taps on release
- a spacing outside 4–15 mm
- a triple-click action without a double-click one
- `hold` or `repeat` on a double or triple click

The TypeScript `GestureActions` in `useAgent.ts` mirrors these types.

## Presets

Saved gestures, kept in the agent's settings so every button and profile can use them, and so they back up with `settings.json`.

```csharp
/// <param name="Icon">A Phosphor icon name, as profiles have, or null.</param>
public sealed record GesturePreset(string Id, string Name, GestureActions Gesture, string? Icon = null);
```

- **Storage.** `AgentSettings` gains `GesturePresets`. They belong to no profile.
- **State.** `AgentState` carries `gesturePresets`.
- **Protocol.**

  | Method | Params |
  | --- | --- |
  | `saveGesturePreset` | `{ name, gesture, icon? }`; returns the new preset's `id` |
  | `updateGesturePreset` | `{ id, name?, gesture?, icon? }` |
  | `deleteGesturePreset` | `{ id }` |

  The icon name is checked the same way as `setProfileIcon` checks it.
- **Validation.** A gesture is checked with the binding rules above when it's saved. A preset that uses something this platform can't send is kept, but refused when applied.
- **Buttons follow their preset.** A binding names its `preset` and keeps its own clicks; editing the preset changes every button that follows it. This replaces copying, which was the first plan.
- **Built-ins.** `GESTURE_PRESETS` in `actions.ts` (Desktops, Browser, Media) stays in the app and shows first. Saving a built-in, or the gesture being edited, makes a preset of your own.

## Agent

### Tracker

`GestureTracker` grows to follow paths rather than being replaced.

- `Reset(GestureActions gesture)` replaces `Reset(repeats)`. Which directions repeat now comes from the gesture itself.
- `Add(dx, dy, now)` returns what happened: an instant direction to fire now, or a repeat; a step reached or backed off; or nothing.
- `Release()` returns the step whose action should run, the click, or nothing.
- `Path` says where it is now.

Acceptance: every existing `GestureTrackerTests` case, including the replay of `Traces/swipes-1000dpi.jsonl`, passes with the flat gesture the test built before, changing only how it calls `Reset`.

### Engine

- Instant directions fire as today: `ActionRunner.Begin`, or `Run` when they repeat.
- Release runs what `Release()` returns with `ActionRunner.Run`.
- Haptics go through the shared `Tap` helper.

### Events

`GestureEvent(Button, Direction)` becomes:

```csharp
/// <param name="Phase">"start" on press, "step" when the path changes, "fire" when a step or the click runs, "end" on release.</param>
/// <param name="Path">The directions taken, one per step, so ["right", "right", "up"] is two steps right then a turn up.</param>
/// <param name="Clicks">On a "fire" with an empty path: 1, 2 or 3 for a single, double or triple click.</param>
public sealed record GestureEvent(string? Button, string Phase, IReadOnlyList<string> Path, int Clicks = 0);
```

`index.vue` and `GesturePad` read the last direction of a "fire" event where they read `direction` now.

### CLI

`mx gestures` gains `--gesture <file>` to follow a gesture loaded from JSON. It logs path changes and what would fire. `--repeat` stays as a shortcut for a flat gesture where every direction repeats.

### README

Update the README's gesture and protocol sections for paths, presets and the new event.

## App

- **Editor.** `GesturePad` grows from today's cross:
  - Selecting a direction shows its action, as now, plus **Extend** and **Branch**.
  - Branch adds the two right-angle steps.
  - Once a step branches, Extend is turned off, with a tooltip saying why.
  - A run shows as a row of steps along its arm. Opening a branch drills in, with a breadcrumb back.
- **Action editor.** `ActionEditor` offers hold and repeat only on the first four directions while nothing lies beyond them, through its existing `allow-hold` and `allow-repeat`. A direction that gains steps beyond it loses hold and repeat; the editor says so, and Ctrl+Z puts them back.
- **Clicks.** The centre cell stays "Click".
  - Selecting it shows a 1× / 2× / 3× switch above its action editor. 3× is offered once 2× has an action.
  - The cell shows small "2×" and "3×" marks when those are set.
  - Once 2× is set, a line under the switch says what it costs: "A single click now waits 300 ms in case a second one follows."
  - `GESTURE_PARTS` gains `doubleClick` and `tripleClick`.
- **Multi-click speed.** A `UiSlider` in the click editor, shown once a double click is set (see Double and triple clicks).
- **Spacing.** A `UiSlider` beside the gesture.
- **Presets.** The existing "Presets" chips list the built-ins, then saved presets.
  - **Save as preset** names the current gesture.
  - A saved chip's context menu (`UiContextMenu`) renames, changes the icon or deletes it. Deleting goes on the undo history (`rememberUndo`).
- **Conventions, as now.** Changes apply as they're made, unfinished drafts wait and say what's missing, and refusals show through `reportFailure`.
- **Live highlight.** The editor highlights the step at the end of each event's path, so you can practise on the mouse while editing.

## Overlay

- **Window.** A second Tauri window: transparent, undecorated, always on top, out of the taskbar and click-through. The main window is already transparent with `macOSPrivateApi` on, so no new permissions are needed. It follows the light or dark theme.
- **Focus.** It must never take focus, because keys the agent sends go to the focused window. Check that Tauri's options guarantee this on Windows, and fall back to `WS_EX_NOACTIVATE` if they don't.
- **Placement.** Centred on the frozen cursor, on that monitor, at its scale.
- **Timing.** It appears 350 ms after "start" or the last "step" while held, and hides on "end". A confident movement never shows it.
- **Content.** The steps reachable from where you are, with labels and the current step highlighted. It works for flat gestures too.
- **Lifetime problem.** The app quits when its window closes, so there's no process to host the overlay:
  - **(a) Recommended:** keep the app running in the tray.
  - **(b)** A native overlay in each platform project of the agent.

## Tests

`GestureTrackerTests`, beyond the existing cases:
- reaching each step of a run, and hysteresis when backing off
- dropping movement across a run, and dropping movement onward at a branch
- turns at 2 mm, and a diagonal turn that picks nothing
- backing out of a run to its start, then picking another direction
- the first four directions firing when reached only while nothing lies beyond them
- every other step running on release, deep leaves included, junctions running nothing, and cancelling from the end of a long path
- strokes among the first four directions, as today, starting a path when the new direction leads further
- DPI changes

`ClickCounterTests`:
- a single click running at once when no double is set
- double and triple counts within the window
- the count so far running when the window runs out
- a moving press running the waiting clicks first
- a binding change dropping waiting clicks

`BindingTests`:
- today's JSON loads unchanged
- every validation rule above

Settings tests: presets round-trip, and applying one copies it.

Engine tests with `FakePlatform`: release taps, leaves held, a binding swapped mid-press, and the shared haptic guard.

On the mouse: tune the spacing from `--trace` recordings, check haptic latency over Bolt and over Bluetooth, and make sure the overlay never takes focus.

## Build order

0. Double and triple clicks: `ClickCounter`, the engine timer, the setting and the centre cell's switch. This doesn't depend on paths, so it can ship first.
1. `GestureStep`, spacing and validation, with JSON compatibility tests.
2. Tracker paths, with every existing test still passing.
3. Engine, haptic helper, event, CLI and README.
4. Presets in settings, the protocol and the state.
5. Editor: steps, branches and presets.
6. Tray mode and the overlay.

Steps 1–3 can be tried from the CLI and the settings file. Step 5 is where paths become usable.

## Open questions

- **Multi-click window:** is 300 ms right? Measure real double clicks on the gesture button, which is pressed with the thumb.
- **Default spacing:** is 6 mm right?
- **Limits:** are 3 steps per run and 2 turns per path right?
- **Presets:** should they stay copies, or should a button be able to follow a preset so editing it updates every button that uses it?
- **Overlay host:** tray app or native per platform?
- **Haptic guard:** skip, or latest wins?

## Out of scope for v1

- Eight directions
- Presets exported to or imported from a file
- Gestures per app, which come with per-app profiles
- Opening a branch by holding still on it
- A live dot following the movement in the overlay
- The macOS overlay
