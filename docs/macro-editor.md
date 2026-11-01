# Macro editor

The editor uses a fixed toolbar, a fixed-height timeline, and a split step list and inspector. Recording, pausing, stopping, selection changes, validation and save status do not insert new regions into the workspace.

A macro is a named, shared sequence. A keyboard shortcut is a single key combination stored directly on an action, with no entry in this library. Shortcuts run once per activation; eligible gesture directions can repeat the combination with continued movement. Hold keys is a third choice that keeps keys down until release. Existing shared macros, including those with only one combination, stay shared unless an assignment explicitly uses Use as shortcut.

## Interaction references

- [Macronyx](https://github.com/DefinitelyN0tMe/Macronyx): its documented timeline editor provides multiselection, copy/paste, drag editing, a property inspector, visual preview, and undo/redo. These established conventions informed this editor's interaction model.
- [Pulover's Macro Creator](https://github.com/Pulover/PuloversMacroCreator): a reference for a command-oriented recorder and editor. Its AutoHotkey implementation and Macronyx's React/Electron implementation are different from this app's Vue/Tauri stack.

These are interaction references. No source from either project or external editor runtime is embedded here. The implementation builds on the existing macro engine, key-capture protocol and Reka UI controls.

## Editing

- Click selects a step. Ctrl/Command-click toggles individual steps; Shift-click selects a range. Ctrl/Command-A selects all.
- Drag a row or selection to an insertion boundary. The selected steps keep their relative order. The list scrolls near its edges while dragging.
- Right-click steps for cut, copy, paste after selection, duplicate, move, insert delay, and delete. Macro library items have rename, duplicate and delete menus.
- The inspector edits the selected step. Multiple selected steps can have their delays scaled together.
- Each document keeps its own undo and redo history, including unfinished drafts. A completed drag or recording is one history operation. A new edit clears the redo branch.
- Valid edits save automatically. Invalid edits remain drafts, retaining the last valid saved macro. Drafts and history survive navigation within the running app; they are not persisted across an app restart.
- Clipboard steps use a versioned format and are validated before insertion. The in-app clipboard works when platform clipboard access is unavailable.

## Recording and timeline

- Record inserts captured key combinations after the selection, or at the end when nothing is selected. New macro creates an empty draft; capture starts only when Record is pressed.
- Pause hands the keyboard back; Resume continues the same recording without including paused time. Stop accepts the recording; Discard leaves the macro unchanged.
- Keep delays controls conversion of captured gaps to delay steps. The existing 1,000 ms delay budget still applies.
- While capturing, Escape or F10 stops and accepts; F9 pauses. F9 resumes a paused recording when this editor has keyboard focus. These are editor controls, not newly registered global system shortcuts.
- The timeline has key, text and mouse lanes. Delays appear as gaps in time across those lanes. A gap reveals its duration and resize edge when hovered, selected or keyboard focused; delay steps remain in the list and inspector for numeric editing. Immediate actions at the same timestamp are grouped to avoid overlap; each individual step remains available in the list.
- Dragging an action adjusts its preceding delay, inserting a delay when needed. Select a gap and drag its right edge to resize it, or use arrow keys on its resize handle. Edits commit when the drag ends and cancel on pointer cancellation.
- Zoom buttons and Ctrl/Command-scroll zoom the timeline around the viewing position. Fit restores the full time range; the horizontal Reka ScrollArea scrollbar pans it. The ruler supports pointer seeking, left/right arrows, Home and End.
- Preview highlights the sequence without sending input. Play/pause, previous/next step and preview speed live in the toolbar. Seeking into a delay resumes from that time; switching macros resets the transport even when their steps are identical. Instant actions get a brief visual dwell so their sequence can be inspected; timeline timestamps still represent the macro's delays.

The recorder continues to capture keyboard input through the existing agent hook, with a browser fallback. Mouse click steps can be inserted in the editor. Global mouse-motion recording, application/window automation and new playback commands are separate engine features.

## Keyboard commands

With focus in the editor, outside a text or numeric input:

| Command | Shortcut |
| --- | --- |
| Undo | Ctrl/Command-Z |
| Redo | Ctrl/Command-Shift-Z; Ctrl-Y |
| Select all | Ctrl/Command-A |
| Cut / copy / paste | Ctrl/Command-X / C / V |
| Duplicate selection | Ctrl/Command-D |
| Delete selection | Delete or Backspace |
| Select previous / next | Up / Down |
| Extend selection | Shift-Up / Shift-Down |
| Move earlier / later | Alt-Up / Alt-Down |
| Visual preview | Space |
| Rename macro | F2 |

Text fields retain their normal text-editing shortcuts. Recording and key-combination capture take precedence over editor commands.

## Stable layouts throughout the app

Action feedback belongs in reserved status areas, popovers or dialogs. Error messages overlay the workspace instead of pushing it. Profile conflict warnings have a reserved slot. Optional settings remain in place and become disabled when unavailable. Button click counts share a tabbed editor, gesture breadcrumbs scroll horizontally, and sequence settings and deletion confirmations use overlays.

Scrollable lists and inspectors use the shared Reka ScrollArea with constant rail space, so recording enough steps to overflow does not narrow their rows. Keyboard focus stays within control bounds; menu highlighting and selection remain distinct. Long shortcut chords stay on one line with their full value available in the accessible label and tooltip. Error alerts sit above the editor so Retry save stays clickable. The sidebar and page heading keep their space during device/helper reconnects. Gesture directions use fixed card dimensions, and the map and movement editor scroll independently.

Capture cancellation releases a pending native capture without waiting for its startup reply. A helper or device disconnect ends recording and retains the captured steps. If the helper cannot save them, the draft offers save retry after reconnecting.

Adding, removing and reordering steps intentionally changes list content inside its bounded scroll region. It does not resize the toolbar, timeline, inspector or footer. New controls should follow the same rule.

## Validation

`pnpm test` covers reordering, clipboard validation, engine limits, timing, immutable snapshots and recorded-key conversion. `pnpm typecheck` and `pnpm generate` validate the frontend build.

`pnpm test:native` exercises the actual hidden Tauri/WebView2 app and Rust IPC with an isolated simulated agent. The suite covers recording, capture lifecycle and failure recovery, undo/redo, clipboard and context menus, pointer reordering, delay-edge dragging, zoom, preview timing, deletion undo/redo, and draft retention across navigation. Continuous geometry checks cover the fixed editor regions and transport controls, scrollbar transitions, profile actions, and gesture sequence editing. Recorder controls are also checked in light and dark appearances with standard motion. See [Native UI tests](native-testing.md) for commands and artifacts.

Native key-hook behavior and injected input on actual hardware still need separate qualification. The frontend work does not change the agent protocol or recognizer.
