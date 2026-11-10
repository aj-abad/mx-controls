import type { ButtonAction, Direction, GesturePreset, GestureStep } from "~/composables/useAgent";

/** Mirrors the rules in `GestureActions.MovesProblem` in the agent. */
export const MAX_RUN = 3;
export const MAX_TURNS = 2;
export const MIN_SPACING_MM = 4;
export const MAX_SPACING_MM = 15;
export const DEFAULT_SPACING_MM = 6;

export const DIRECTIONS: Direction[] = ["up", "down", "left", "right"];
export const GLYPHS: Record<Direction, string> = { up: "↑", down: "↓", left: "←", right: "→" };
export const DIRECTION_LABELS: Record<Direction, string> = { up: "Up", down: "Down", left: "Left", right: "Right" };
const OPPOSITE: Record<Direction, Direction> = { up: "down", down: "up", left: "right", right: "left" };
const VECTORS: Record<Direction, [number, number]> = { up: [0, -1], down: [0, 1], left: [-1, 0], right: [1, 0] };

export const NO_STEP: GestureStep = { type: "none" };

export const across = (d: Direction): Direction[] => (d === "up" || d === "down" ? ["left", "right"] : ["up", "down"]);
export const pathText = (path: Direction[]) => path.map((d) => GLYPHS[d]).join(" ");
export const pathKey = (path: Direction[]) => path.join(",");

const next = (step: GestureStep, d: Direction) => step[d] ?? null;
const hasNext = (step: GestureStep) => DIRECTIONS.some((d) => !!step[d]);

/** A step's own action, without the steps beyond it. */
export function actionOf({ type, keys, system, repeat, mouse, macro, layer, profile }: GestureStep): ButtonAction {
  return { type, keys, system, repeat, mouse, macro, layer, profile };
}

/** A step with a new action, keeping the steps beyond it. */
export const withAction = (step: GestureStep, action: ButtonAction): GestureStep => ({
  ...action,
  up: step.up,
  down: step.down,
  left: step.left,
  right: step.right,
});

export interface PathRow {
  path: Direction[];
  step: GestureStep;
  /** Steps lie beyond it. */
  leadsOn: boolean;
  /** One of the first four directions with nothing beyond: it runs as it's reached, and may hold or repeat. */
  instant: boolean;
  /** Nothing goes further its own way, so it may repeat: it then runs as it's reached and again as the movement goes on. */
  repeatable: boolean;
}

/** Every step, in pad order with each step's further steps right after it. */
export function pathRows(preset: GesturePreset): PathRow[] {
  const rows: PathRow[] = [];
  const visit = (step: GestureStep, path: Direction[]) => {
    rows.push({
      path,
      step,
      leadsOn: hasNext(step),
      instant: path.length === 1 && !hasNext(step),
      repeatable: !next(step, path.at(-1)!),
    });
    for (const d of DIRECTIONS) {
      const beyond = next(step, d);
      if (beyond) visit(beyond, [...path, d]);
    }
  };
  for (const d of DIRECTIONS) visit(preset[d], [d]);
  return rows;
}

export function stepAt(preset: GesturePreset, path: Direction[]): GestureStep | null {
  let step: GestureStep | null = preset[path[0]!];
  for (const d of path.slice(1)) step = step ? next(step, d) : null;
  return step;
}

/** The preset with the step at `path` changed; null removes it, and every step beyond, leaving a first direction blank. */
export function withStep(preset: GesturePreset, path: Direction[], change: (step: GestureStep) => GestureStep | null): GesturePreset {
  const rebuild = (step: GestureStep, rest: Direction[]): GestureStep | null => {
    if (!rest.length) return change(step);
    const [d, ...tail] = rest as [Direction, ...Direction[]];
    const beyond = next(step, d);
    return beyond ? { ...step, [d]: rebuild(beyond, tail) } : step;
  };
  const [first, ...rest] = path as [Direction, ...Direction[]];
  return { ...preset, [first]: rebuild(preset[first], rest) ?? NO_STEP };
}

/** What's wrong with a path's shape on its own, or null. */
export function shapeProblem(path: Direction[]): string | null {
  let run = 1;
  let turns = 0;
  for (let i = 1; i < path.length; i++) {
    if (path[i] === OPPOSITE[path[i - 1]!]) return "A path can't turn back the way it came.";
    if (path[i] === path[i - 1]) {
      if (++run > MAX_RUN) return `A run is at most ${MAX_RUN} steps.`;
    } else {
      run = 1;
      if (++turns > MAX_TURNS) return `A path turns at most ${MAX_TURNS} times.`;
    }
  }
  return null;
}

/** What stops a path from being added to this preset, or null: it's there already, or a step on it can't take it. */
export function fitProblem(preset: GesturePreset, path: Direction[]): string | null {
  if (stepAt(preset, path)) return "That path is already there.";
  for (let k = 1; k < path.length; k++) {
    const at = stepAt(preset, path.slice(0, k));
    if (!at) break;
    const came = path[k - 1]!;
    const goes = path[k]!;
    const turns = across(came).some((d) => !!at[d]);
    if (goes === came && turns) return `${pathText(path.slice(0, k))} already turns, so it can't go further.`;
    if (goes !== came && at[came]) return `${pathText(path.slice(0, k))} already goes further, so it can't turn.`;
  }
  return null;
}

/**
 * The preset with a path added: steps on the way that aren't there yet do nothing, and the last waits for an
 * action. A first direction that now leads on can't hold keys any more, since it runs on release, and a step that
 * now goes further its own way stops repeating, since going on would repeat it.
 */
export function addPath(preset: GesturePreset, path: Direction[]): GesturePreset {
  const grow = (step: GestureStep, rest: Direction[], came: Direction): GestureStep => {
    if (!rest.length) return step;
    const [d, ...tail] = rest as [Direction, ...Direction[]];
    const beyond = next(step, d) ?? (tail.length ? NO_STEP : { type: "press", keys: [] });
    return { ...outgrown(step, d === came), [d]: grow(beyond, tail, d) };
  };
  const [first, ...rest] = path as [Direction, ...Direction[]];
  return { ...preset, [first]: grow(preset[first], rest, first) };
}

/** A step that now leads on: held keys become an inline shortcut, and it stops repeating if it now goes further. */
const outgrown = (step: GestureStep, further: boolean): GestureStep => ({
  ...step,
  ...(step.type === "hold" && { type: "press" as const }),
  ...(further && step.repeat && { repeat: false }),
});

/** Whether adding `path` would change a step on the way, as `addPath` does. */
export function addingChanges(preset: GesturePreset, path: Direction[]): boolean {
  return path.slice(0, -1).some((came, k) => {
    const step = stepAt(preset, path.slice(0, k + 1));
    return !!step && (step.type === "hold" || (!!step.repeat && path[k + 1] === came));
  });
}

/** An SVG path drawing every path from the centre, one `step` apart. */
export function drawPaths(preset: GesturePreset, centre: number, step: number) {
  return pathRows(preset)
    .filter((row) => !row.leadsOn && (row.step.type !== "none" || row.path.length > 1))
    .map((row) => drawPath(row.path, centre, step))
    .join("");
}

export function drawPath(path: Direction[], centre: number, step: number) {
  let x = centre;
  let y = centre;
  return (
    `M${centre} ${centre}` +
    path
      .map((d) => {
        x += VECTORS[d][0] * step;
        y += VECTORS[d][1] * step;
        return `L${x} ${y}`;
      })
      .join("")
  );
}

export function endOf(path: Direction[], centre: number, step: number) {
  let x = centre;
  let y = centre;
  for (const d of path) {
    x += VECTORS[d][0] * step;
    y += VECTORS[d][1] * step;
  }
  return { x, y };
}

/** "4 moves · 2 paths": first directions that do something, and paths beyond them. */
export function countsText(preset: GesturePreset) {
  const rows = pathRows(preset);
  const moves = rows.filter((r) => r.path.length === 1 && (r.step.type !== "none" || r.leadsOn)).length;
  const paths = rows.filter((r) => r.path.length > 1 && !r.leadsOn).length;
  const parts = [moves && `${moves} move${moves === 1 ? "" : "s"}`, paths && `${paths} path${paths === 1 ? "" : "s"}`];
  return parts.filter(Boolean).join(" · ") || "No moves";
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
