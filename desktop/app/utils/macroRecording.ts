import type { MacroStep } from "~/composables/useAgent";

/** A key going down or up while recording, with when, in milliseconds on any steady clock. */
export interface RecordedKey {
  code: string;
  down: boolean;
  at: number;
}

const MODIFIERS = new Set(["ShiftLeft", "ShiftRight", "ControlLeft", "ControlRight", "AltLeft", "AltRight", "MetaLeft", "MetaRight"]);

/** Pauses shorter than this are how fast hands move, not something to play back. */
const SHORTEST_PAUSE_MS = 30;

/**
 * Turns recorded keys into macro steps. Each key that isn't a modifier becomes one keys step, with the modifiers
 * held at the time; a modifier pressed and let go on its own becomes a step by itself. With `pauses`, the time
 * between steps becomes waits, rounded to 10 ms and cut so the macro stays within `budgetMs`.
 */
export function stepsFrom(keys: RecordedKey[], pauses: boolean, budgetMs: number): { steps: MacroStep[]; trimmed: boolean } {
  const steps: MacroStep[] = [];
  const held: string[] = [];
  const used = new Set<string>();
  let lastAt: number | null = null;
  let left = budgetMs;
  let trimmed = false;

  function emit(chord: string[], at: number) {
    if (pauses && lastAt !== null) {
      const gap = Math.round((at - lastAt) / 10) * 10;
      if (gap >= SHORTEST_PAUSE_MS) {
        const ms = Math.min(gap, left);
        if (ms < gap) trimmed = true;
        if (ms > 0) {
          steps.push({ type: "wait", ms });
          left -= ms;
        }
      }
    }
    steps.push({ type: "keys", keys: chord });
    lastAt = at;
  }

  // A step starts when its key goes down, so a long press doesn't stretch the pause before it.
  const downAt = new Map<string, number>();
  for (const { code, down, at } of keys) {
    if (down) {
      if (held.includes(code)) continue;
      held.push(code);
      downAt.set(code, at);
      if (!MODIFIERS.has(code)) {
        const modifiers = held.filter((k) => MODIFIERS.has(k));
        modifiers.forEach((k) => used.add(k));
        emit([...modifiers, code], at);
      }
      continue;
    }
    const index = held.indexOf(code);
    if (index < 0) continue;
    held.splice(index, 1);
    if (MODIFIERS.has(code)) {
      if (!used.has(code)) emit([code], downAt.get(code) ?? at);
      used.delete(code);
    }
  }
  return { steps, trimmed };
}
