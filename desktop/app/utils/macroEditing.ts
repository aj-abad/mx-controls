import type { Macro, MacroStep } from "../composables/useAgent";

export const MACRO_WAIT_LIMIT = 1000;
export const MACRO_STEP_LIMIT = 200;
export const MACRO_TEXT_LIMIT = 2000;
export const cloneMacro = <T>(value: T): T => JSON.parse(JSON.stringify(value));

export function macroProblem(macro: Macro): string | null {
  if (!macro.name.trim()) return "Give the macro a name.";
  if (!macro.steps.length) return "Add a step or record some keys.";
  if (macro.steps.length > MACRO_STEP_LIMIT) return `A macro can contain up to ${MACRO_STEP_LIMIT} steps.`;
  let wait = 0;
  for (const [i, step] of macro.steps.entries()) {
    if (step.type === "keys" && !step.keys?.length) return `Step ${i + 1}: record its keys.`;
    if (step.type === "text" && !step.text) return `Step ${i + 1}: enter some text.`;
    if (step.type === "text" && step.text!.length > MACRO_TEXT_LIMIT)
      return `Step ${i + 1}: text can contain up to ${MACRO_TEXT_LIMIT} characters.`;
    if (step.type === "wait") {
      if (!Number.isInteger(step.ms) || step.ms! < 1)
        return `Step ${i + 1}: use a whole number of milliseconds.`;
      wait += step.ms!;
    }
  }
  return wait > MACRO_WAIT_LIMIT
    ? `Total delays exceed ${MACRO_WAIT_LIMIT} ms. Shorten a delay to save.`
    : null;
}

export function macroEntries(steps: MacroStep[]) {
  let at = 0;
  return steps.map((step, index) => {
    const ms = step.type === "wait" ? Math.max(0, step.ms ?? 0) : 0;
    const entry = { step, index, at, ms, end: at + ms };
    at += ms;
    return entry;
  });
}

/** Move the selected block to an insertion boundary, preserving the order of both sets. */
export function reorderMacroSteps(steps: MacroStep[], selected: number[], boundary: number) {
  const indices = [...new Set(selected)].filter((i) => i >= 0 && i < steps.length).sort((a, b) => a - b);
  const chosen = new Set(indices);
  const at = Math.max(0, Math.min(steps.length, boundary));
  const insert = at - indices.filter((i) => i < at).length;
  const rest = steps.filter((_, i) => !chosen.has(i));
  const moved = indices.map((i) => steps[i]!);
  return {
    steps: [...rest.slice(0, insert), ...moved, ...rest.slice(insert)],
    selection: moved.map((_, i) => insert + i),
  };
}

/** A bounded, versioned clipboard format; arbitrary objects are never used as macro steps. */
export function encodeMacroSteps(steps: MacroStep[]) {
  return JSON.stringify({ format: "mx-controls/steps", version: 1, steps });
}

export function decodeMacroSteps(text: string): MacroStep[] | null {
  try {
    const value = JSON.parse(text);
    if (
      value?.format !== "mx-controls/steps" ||
      value.version !== 1 ||
      !Array.isArray(value.steps) ||
      !value.steps.length ||
      value.steps.length > MACRO_STEP_LIMIT
    )
      return null;
    return value.steps.map((step: MacroStep) => {
      if (
        step.type === "keys" &&
        Array.isArray(step.keys) &&
        step.keys.length <= 16 &&
        step.keys.every((key) => typeof key === "string" && /^[A-Za-z][A-Za-z0-9]{0,39}$/.test(key))
      )
        return { type: "keys", keys: [...step.keys] };
      if (step.type === "text" && typeof step.text === "string" && step.text.length <= MACRO_TEXT_LIMIT)
        return { type: "text", text: step.text };
      if (step.type === "wait" && Number.isInteger(step.ms) && step.ms! >= 1 && step.ms! <= MACRO_WAIT_LIMIT)
        return { type: "wait", ms: step.ms };
      if (step.type === "click" && ["middle", "back", "forward"].includes(step.mouse ?? ""))
        return { type: "click", mouse: step.mouse };
      throw new Error("Invalid step");
    });
  } catch {
    return null;
  }
}

export interface MacroEdit {
  label: string;
  before: Macro;
  after: Macro;
  beforeSelection: number[];
  afterSelection: number[];
}

export function macroStepText(step: MacroStep, keyName: (key: string) => string = (key) => key) {
  if (step.type === "keys") return step.keys?.map(keyName).join(" + ") || "Record keys…";
  if (step.type === "text") return step.text?.replace(/\n/g, " ↵ ") || "Enter text…";
  if (step.type === "wait") return `${step.ms ?? 0} ms delay`;
  return (
    ({ middle: "Middle click", back: "Back click", forward: "Forward click" } as Record<string, string>)[
      step.mouse ?? ""
    ] ?? "Mouse click"
  );
}
