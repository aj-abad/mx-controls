import type { ButtonAction, Direction, GesturePreset } from "~/composables/useAgent";

export const DIRECTIONS: Direction[] = ["up", "down", "left", "right"];
export const GLYPHS: Record<Direction, string> = { up: "↑", down: "↓", left: "←", right: "→" };
export const DIRECTION_LABELS: Record<Direction, string> = { up: "Up", down: "Down", left: "Left", right: "Right" };

/** "4 moves": the directions that do something. */
export function countsText(preset: GesturePreset) {
  const moves = DIRECTIONS.filter((d) => preset[d].type !== "none").length;
  return moves ? `${moves} move${moves === 1 ? "" : "s"}` : "No moves";
}

/** Whether the agent would take the action: keys need a combination, opening needs a target, a Shortcut a choice. */
export const complete = (action: ButtonAction) => {
  if (action.type === "press" || action.type === "hold") return !!action.keys?.length;
  if (action.type === "open") return openComplete(action.open);
  if (action.type === "appleShortcut") return !!action.shortcut;
  return true;
};

// The agent sends back nulls and `repeat: false` that the editors leave out, so compare without them.
export const canonical = (value: unknown) =>
  JSON.stringify(value, (_, v) =>
    v && typeof v === "object" && !Array.isArray(v)
      ? Object.fromEntries(Object.entries(v).filter(([, x]) => x != null && x !== false).sort(([a], [b]) => a.localeCompare(b)))
      : v,
  );
