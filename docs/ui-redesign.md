# Profiles and gesture controls

The main workspace is **Profiles**. A profile owns button assignments and thumb-wheel behavior. **Gestures** and **Macros** are reusable items in a shared library. Editing a shared item updates every assignment that uses it.

The visual direction loosely follows Apple's [sidebar guidance](https://developer.apple.com/design/human-interface-guidelines/sidebars) and [label guidance](https://developer.apple.com/design/human-interface-guidelines/labels): system fonts, ordinary capitalization, short labels, restrained selection fills, and a stable sidebar → list → detail hierarchy. Section labels use normal spacing. The existing system appearance and native window effects remain supported.

Focus and scrolling also use Apple's [scroll-view guidance](https://developer.apple.com/design/human-interface-guidelines/scroll-views) and [menu guidance](https://developer.apple.com/design/human-interface-guidelines/menus) as loose references. Indicators are small, rounded and translucent, with stronger contrast on hover. Keyboard focus uses a contained two-pixel accent outline; menu items use their own highlighted fill and separate selection checkmark. Focus never changes a control's dimensions.

`UiScrollArea` composes Reka's ScrollArea primitives across settings panels, shared libraries, menus, inspectors, gesture breadcrumbs and the macro timeline. Scrollbar rails have a constant eight-pixel allocation, so overflow does not change the content width. The viewport remains a native scroll element for wheel, trackpad and keyboard behavior; Reka supplies the thumb and track. Select follows [Reka's custom-scrollbar composition](https://reka-ui.com/docs/components/select#with-custom-scrollbar) so option focus and scrolling share the same viewport. Scrollbar interactions are excluded from window dragging.

## Implemented

- A persistent sidebar replaces the centered Buttons / Gestures / Macros switch. Profiles is the workspace; Gestures and Macros sit under Shared library.
- Profile names remain visible, with an explicit edit button for the active profile. Device information stays in the title bar and Easy-Switch stays in the sidebar.
- The profile workspace keeps the mouse map and control inspector. It identifies pointer speed, wheel feel and haptics as settings shared by every profile.
- The gesture library uses a compact preset list, a labeled four-direction map, and one movement inspector. Each direction shows its assigned action and whether further steps exist.
- Selecting a sequence opens its breadcrumb. Selecting an earlier breadcrumb opens that step; selecting a next movement drills further in.
- Add another movement offers only valid Continue and Turn operations. Limits remain three steps in a run and two turns. Removing a step also removes its descendants and returns to its parent.
- Action controls explain when a movement runs, what holding or repeating does, and what releasing at a waypoint does. Hold and repeat remain available only on simple first movements.
- Direction cards say Move up/down/left/right with prominent arrows and a trigger summary. The movement inspector draws the selected path from an open start circle to an arrowhead and describes it in words. Its behavior panel distinguishes running during movement, repeating every 4 mm, held keys, and running on release.
- Keyboard shortcuts are one key combination stored inline on the assigned action (`press`), separate from the named shared macro library (`macro`). The picker always offers Keyboard shortcut and Hold keys under Keyboard, and saved sequences under Shared macros. New shortcut capture never creates a macro, including after an agent restart. A one-combination shared macro can be copied into one action with Use as shortcut; the shared item remains intact.
- Repeat with movement is an explicit option for shortcuts on simple gesture directions. Repeat while held remains available for eligible button actions. Action controls reserve the same space for the picker, detail control and repeat/help row so choosing an action or recording keys does not move the surrounding controls.
- Sequence spacing is under Movement settings. The UI explains that spacing affects continued movements, while the first movement and turns use the engine's existing 2 mm threshold.
- Valid edits save automatically. Unfinished actions stay in the draft, leaving the saved preset active. Duplicate is unavailable while the draft is incomplete. Existing undo and deletion behavior remain supported.
- Direction buttons support arrow keys. Adding or removing a step moves keyboard focus to the current action control. Incoming gesture events select the matching path for an assigned button in the active profile.

This changes the editor and navigation. The gesture recognizer, timing, haptics, settings format and agent protocol are unchanged.

## Follow-up scope for the complete gesture experience

### 1. Guided hardware rehearsal

Add a clearly entered rehearsal mode: choose an assigned button, hold it, perform a movement, and see the recognized path and intended action without executing that action. Explain movement, release, and cancellation using the actual recognizer.

This needs agent support. The current events report steps and fired paths; they do not provide a safe capture-only session, press lifecycle, or raw movement stream to the editor. The protocol should support a session with start, step, release, cancellation and end events, suppress only that session's actions, and restore normal behavior on timeout, disconnect, navigation or window close. Do not implement rehearsal by guessing pixel distances in the browser.

Acceptance: no keyboard shortcut or macro executes during rehearsal; the focused application stays unchanged; the session cannot leave a mouse button diverted or a key held; simple moves, sequences, backing out and reconnects all give correct feedback.

### 2. Assign from the library

Offer Use in profile from a preset. Show the destination profile and button, its current assignment, the retained click action, and any held-key or repeat behavior that must change. Prefer the active profile first. Applying the assignment must be explicit because switching the active profile also changes the live mouse configuration.

Acceptance: the chosen button follows the selected shared preset, other assignments stay intact, the destination is clear, and undo restores the prior mapping.

### 3. Optional reminder while using the mouse

After rehearsal is reliable, offer an opt-in reminder that appears after a brief pause while holding a gesture button. Show reachable next movements and the selected step. Keep the overlay on the pointer's monitor and never take focus.

This needs press lifecycle events and a decision about the overlay host. The current desktop process closes with its window; an always-available overlay needs a tray lifecycle or a native agent-hosted window. Treat this as separate work, including shutdown behavior and permission review for the relevant platform.

Acceptance: fast gestures show no overlay; release and cancellation dismiss it; shortcuts continue to reach the originally focused application; monitor scaling and disconnects are handled.

## Validation

Check the UI at the existing 1124 × 632 window size. Exercise preset creation, first-direction editing, runs, turns, deep breadcrumbs, incomplete drafts, step deletion, undo, shared-use information, built-in reset, and profile-to-preset navigation. Check keyboard navigation and system appearance. Run Nuxt typechecking and static generation.

Hardware qualification remains separate: verify recognition and live path highlighting on the actual mouse before claiming that the complete physical gesture experience has been tested.
