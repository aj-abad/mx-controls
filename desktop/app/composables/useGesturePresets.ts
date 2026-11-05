import type { GesturePreset } from "~/composables/useAgent";

/** Names where the mouse's own differ from what Logitech prints on the box; shared with the controls list. */
export const BUTTON_LABELS: Record<string, string> = { smartshift: "Top button", haptic: "Haptic button" };

/** The gesture presets, who uses them, and changes to them that Ctrl+Z puts back. */
export function useGesturePresets() {
  const { state, device, attempt } = useAgent();

  const presets = computed(() => state.value?.gesturePresets ?? []);
  const find = (id: string | null | undefined) => (id ? presets.value.find((p) => p.id === id) : undefined);
  const builtIn = (id: string) => state.value?.builtInPresets.find((p) => p.id === id);
  /** A built-in preset that's been edited; reset takes it back. */
  const changed = (preset: GesturePreset) => {
    const original = builtIn(preset.id);
    return !!original && canonical(original) !== canonical(preset);
  };

  const buttonLabel = (id: string) => {
    const name = device.value?.buttons.find((b) => b.id === id)?.name ?? id;
    return BUTTON_LABELS[id] ?? `${name} button`;
  };
  const profileName = (id: string) => state.value?.profiles.find((p) => p.id === id)?.name ?? id;

  /** "Gesture button · Work" for each button following the preset, leaving out `except`. */
  function usedBy(id: string, except?: { profile: string; button: string; layer?: boolean }) {
    return (state.value?.presetUses ?? [])
      .filter(
        (use) =>
          use.preset === id &&
          !(except && use.profile === except.profile && use.button === except.button && !!use.layer === !!except.layer),
      )
      .map((use) => `${buttonLabel(use.button)} · ${profileName(use.profile)}${use.layer ? " (second layer)" : ""}`);
  }

  const save = (preset: GesturePreset) => attempt("setGesturePreset", { preset }, `Couldn't save “${preset.name}”`);
  const remove = (id: string, name: string) => attempt("deleteGesturePreset", { id }, `Couldn't delete “${name}”`);

  /** Saves a change, and remembers how to put back what was there. */
  async function change(preset: GesturePreset) {
    const before = find(preset.id);
    if (!(await save(preset))) return false;
    rememberUndo(() => (before ? save(before) : remove(preset.id, preset.name)), () => save(preset), "Edit gesture preset");
    return true;
  }

  /** Deletes an own preset, or takes a built-in one back to how it came. Buttons following it come back with undo. */
  async function discard(preset: GesturePreset) {
    if (!(await remove(preset.id, preset.name))) return false;
    rememberUndo(() => save(preset), () => remove(preset.id, preset.name), "Delete gesture preset");
    return true;
  }

  /** Adds a preset of the user's own and returns its ID, or null if the agent refused it. */
  async function create(from: Omit<GesturePreset, "id">) {
    const id = Math.random().toString(36).slice(2, 10);
    return (await change({ ...from, id, icon: null })) ? id : null;
  }

  const blank = (): Omit<GesturePreset, "id"> => ({
    name: "New preset",
    up: NO_STEP,
    down: NO_STEP,
    left: NO_STEP,
    right: NO_STEP,
    spacingMm: DEFAULT_SPACING_MM,
  });

  return { presets, find, builtIn, changed, usedBy, buttonLabel, change, discard, create, blank };
}
