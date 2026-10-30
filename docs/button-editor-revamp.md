# Button editor revamp

Status: proposal, not built. The mockups are in `button-editor-revamp.html`, at the inspector's real size (470 × 540).

## Problems today

1. **Three switches deep.** In Gesture mode it's mode, then a pad cell, then Click / Double / Triple. The mode switch and the count switch look the same, so they read as peers.
2. **Double and triple clicks are hidden.** The pad shows only "2×". Their actions sit behind a tab in both modes.
3. **Triple can't be found.** Its tab appears only once a double is set. The tabs look like views of one thing, not slots to fill.
4. **Side effects happen out of sight.** Setting a double click turns a "Hold" single click into a press, and turns its repeat off. That happens on a tab you aren't looking at.
5. **A global setting sits inside each button.** The multi-click wait slider and its paragraph are the biggest thing in the click editor, and they repeat on every button.
6. **Nothing fits.** The gesture panel is taller than 540. Editing the click scrolls the pad and the mode switch out of view.
7. **Action and Gesture overlap.** Both have click, double and triple. They differ only in whether moving does anything. The draft also keeps two hidden sets of double and triple clicks, one per mode.
8. **The summaries say little.** The list says "Gestures", or "Left Shift key · 2×" without saying what 2× does.
9. **Paths have nowhere to go.** Adding Extend, Branch, an "Action | Path" toggle and spacing would make a fourth level.

## Directions

| | A · Trigger list (recommended) | B · Gesture map | C · Drill-in pad |
| --- | --- | --- | --- |
| Idea | Each trigger is a row: click, 2×, 3×, then each move or path. Each row has its own picker. No mode switch. | The pad grows into a map of every step. A node opens a popover to edit it. The click node lists 1×, 2× and 3×. | Today's cross. Every step of a path is the same cross, with a breadcrumb. The centre cell lists 1×, 2× and 3×, edited as three rows. |
| Switching levels | None | Node, then popover | Cell, then editor, plus depth |
| Paths | Indented rows whose trigger is the arrows, such as `→ → ↑`, each marked "instant" or "on release". Record a path with the arrow keys or on the mouse. | Drawn as they are. Extend and Branch sit in the popover or on "+" ghosts. On-release steps are dashed. | A "3 ›" badge on an arm. "Go further ›" drills in. Rule limits show as disabled arms. |
| Presets | Picked on the button and edited on their own page (see below), whichever direction is used | | |
| Main risk | Loses the cross. An optional compass and the arrow chips make up for it. | Doesn't fit at full depth, so it needs pan or zoom. Hardest to make accessible. Most code. | Depth is hidden, so you never see the whole gesture. |
| Cost | Medium: a new editor that reuses `ActionEditor` | High | Low: `GesturePad` grows |

**Recommendation: A.** It's the only direction that removes every nested switch, so everything a button does is on screen at once. Paths and presets become more rows rather than more modes, and it's the easiest to use from the keyboard and a screen reader. The spatial feel moves to the arrow chips, an optional compass and the overlay.

## On the "Action | Path" toggle

Drop it. In the model, a step has an action (possibly none) *and* optional steps beyond it, so "Action or Path" is a false choice. The toggle would hide the step's own action and add a fourth switching level. Show that a step leads further with the row's note (A), the node's arms (B) or a badge (C). State on that step that its timing changes from instant to on release.

## Fixes for any direction

- **One model.** Default means nothing is set. With no moves, a button acts on press, so a key can stay down and an action can repeat. Adding a move makes the click run on release. The binding's `mode` follows from what's set, and there's one set of double and triple clicks.
- **Wait as one line.** "A single click now waits **300 ms** for a second one." The value opens a popover with the slider and "Applies to every button".
- **Consequences on their own row.** A key that becomes a macro, or a repeat that gets turned off, shows a warning on the row it affects. Ctrl+Z undoes it.
- **Summaries.** Gesture: "Copy · Window tiling", or "Copy · 2× · Window tiling". Action: "Left Shift key · 2× Task View", or the counts alone if that doesn't fit.

## Presets on their own page

Moves are only edited as presets, on a Gesture presets page. A button picks one, the way it picks an action. The button editor then holds just the click rows and a Preset picker, so it stays the same size however deep the paths go. The page opens in the inspector, from "Edit preset ›" on a button or from a row at the end of All controls.

- **A button follows its preset.** Editing a preset changes every button that uses it, in every profile, straight away. This answers the open question in [gestures.md](gestures.md#open-questions): presets stop being copies. Each preset says which buttons use it, and so does the button ("Shared with Gesture button · Gaming").
- **Clicks stay on the button.** A preset holds only moves and paths, plus spacing. 1×, 2× and 3× belong to the button, so one preset works on buttons with different clicks.
- **Different moves mean a different preset.** There are no overrides on a button. "Duplicate" on a preset makes the variant.
- **Built-ins can be edited.** Desktops, Browser and Media show "Built-in, changed" once edited, with "Reset to built-in". They can't be renamed or deleted.
- **Deleting a preset in use asks first.** It names the buttons that keep their clicks and lose their moves. Ctrl+Z brings the preset back, along with its buttons.
- **Drafts wait.** A preset with an unset step isn't saved, and its buttons keep the last saved version until it is.
- **"New preset…" in the picker** opens a blank preset on the page, and the button uses it once it's saved.
- **Editor choice moves with it.** The page has the same width as the inspector, so A's rows are still the pick. B's map has the whole panel to itself there rather than sharing it with the clicks, but it still needs pan or zoom at full depth.

### What changes in the gestures plan

- A gesture binding stores its clicks and a `presetId`, not its own `GestureActions`. The agent resolves the preset when the binding is used.
- Built-ins move from `GESTURE_PRESETS` in `actions.ts` into the agent's settings, seeded on first run, because a binding now points at them.
- `deleteGesturePreset` clears the moves of the bindings that use it, in one undoable change.
- Migration: each distinct gesture already in `settings.json` becomes a preset named after its button, such as "Gesture button (Work)". Gestures that match a built-in point at it instead.
- The settings test becomes "editing a preset changes every binding that uses it", in place of "applying one copies it".

## Key and Macro

Two of the custom actions are renamed. The agent's `type` values stay as they are for now.

| Today | New name | `type` | What it does |
| --- | --- | --- | --- |
| Hold keys… | Key… | `hold` | Holds its keys down for as long as the button is held. |
| Keyboard shortcut… | Macro… | `press` | Sends its keys once, or repeatedly while held. Today a macro is one chord; a full macro editor comes later. |

- **The built-in actions stay as they are.** Copy, Paste and the rest are still listed by name, even though they're macros underneath.
- **A key that can't stay down becomes a macro.** A double click or a move makes the click wait or run on release. The row then changes to Macro and says so: "The key became a macro that taps Left Shift once."
- **Summaries.** A key reads "Left Shift key", and a macro shows its keys, "Ctrl + C", as now.
- **Renaming `type` waits for the macro editor.** When a macro grows into steps, `press` becomes `macro` with a list of steps, and the settings are migrated in the same change. Renaming it now would mean migrating twice.
