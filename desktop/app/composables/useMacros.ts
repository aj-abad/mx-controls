import type { Macro, MacroStep } from "~/composables/useAgent";

/** A macro waits at most this long in all; the agent refuses more. */
export const MACRO_MAX_MS = 1000;

/** How long a macro waits in all. */
export const macroMs = (macro: { steps: MacroStep[] }) =>
  macro.steps.reduce((total, step) => total + (step.type === "wait" ? (step.ms ?? 0) : 0), 0);

/** What a step still needs before the agent would take it, or null. */
export function stepProblem(step: MacroStep): string | null {
  if (step.type === "keys" && !step.keys?.length) return "Record its keys";
  if (step.type === "text" && !step.text) return "Type its text";
  if (step.type === "wait" && !((step.ms ?? 0) >= 1)) return "Give it a length";
  return null;
}

/** The saved macros, where each runs, and changes to them that Ctrl+Z puts back. */
export function useMacros() {
  const { state, attempt } = useAgent();
  const presets = useGesturePresets();

  const macros = computed(() => state.value?.macros ?? []);
  const find = (id: string | null | undefined) => (id ? macros.value.find((m) => m.id === id) : undefined);

  /** "Back button · Default" for each button or thumb wheel running it, then "Window tiling" for each gesture preset. */
  function usedBy(id: string) {
    const profileName = (pid: string) => state.value?.profiles.find((p) => p.id === pid)?.name ?? pid;
    return (state.value?.macroUses ?? [])
      .filter((use) => use.macro === id)
      .map((use) =>
        use.preset
          ? `${presets.find(use.preset)?.name ?? "A gesture preset"} gesture`
          : `${use.button === "thumbwheel" ? "Thumb wheel" : presets.buttonLabel(use.button ?? "")} · ${profileName(use.profile ?? "")}`,
      );
  }

  const save = (macro: Macro) => attempt("setMacro", { macro }, `Couldn't save “${macro.name}”`);
  const remove = (macro: Macro) => attempt("deleteMacro", { id: macro.id }, `Couldn't delete “${macro.name}”`);

  /** Saves a macro, new or changed, and remembers how to put back what was there. */
  async function change(macro: Macro) {
    const before = find(macro.id);
    if (!(await save(macro))) return false;
    rememberUndo(() => (before ? save(before) : remove(macro)));
    return true;
  }

  /** Deletes a macro; what ran it does nothing until undo puts it back with the same ID. */
  async function discard(macro: Macro) {
    if (!(await remove(macro))) return false;
    rememberUndo(() => save(macro));
    return true;
  }

  const newId = () => Math.random().toString(36).slice(2, 10);

  /** Saves recorded keys as a macro named after them, for "New macro…" in an action picker; returns its ID. */
  async function createFromKeys(keys: string[]) {
    const macro: Macro = { id: newId(), name: chordLabel(keys), steps: [{ type: "keys", keys }] };
    return (await change(macro)) ? macro.id : null;
  }

  return { macros, find, usedBy, change, discard, createFromKeys, newId };
}
