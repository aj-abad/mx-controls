/**
 * The main screen is drawn at a fixed design size and scaled to fit the window, so the line from the
 * mouse to the list can be laid out by hand. Only one line shows at a time, for the control under the
 * pointer or open; it runs from a spot on the drawing at 75° to its row's height, then straight right to its row.
 *
 * Where the drawing and the list sit is a `Frame`. Opening a control moves from `OVERVIEW` to `FOCUS`, and
 * everything placed against the drawing is worked out from the frame, so it can be blended between the two.
 */

import type { AgentState, ThumbWheelSettings } from "~/composables/useAgent";

export type ControlId = "wheel" | "middle" | "thumbwheel" | "smartshift" | "back" | "forward" | "gesture" | "haptic" | "sensor";

export interface Point {
  x: number;
  y: number;
}

export type GroupId = "scrolling" | "buttons" | "pointer";

export const GROUPS: { id: GroupId; label: string }[] = [
  { id: "scrolling", label: "Scrolling" },
  { id: "buttons", label: "Buttons" },
  { id: "pointer", label: "Pointer" },
];

export interface Control {
  id: ControlId;
  group: GroupId;
  /** Where the control sits on `MouseDrawing` (296 × 454), in its units. */
  spot: Point;
}

const DRAWING = { width: 296, height: 454 };

export const STAGE = { width: 1060, height: 540 };
export const LIST = { x: 720, width: 340, padding: 6, header: 30, row: 44 };
/** A control's settings, wider than the list so editors like the gesture pad have room. */
export const INSPECTOR = { x: 590, width: 470 };

export interface Frame {
  image: { x: number; y: number; scale: number };
  /** Where lines end: the left edge of the list, or of the settings. */
  edge: number;
}

export const OVERVIEW: Frame = { image: { x: 240, y: 86, scale: 0.94 }, edge: LIST.x };
/** With a control open, the drawing steps back to make room for its settings. */
export const FOCUS: Frame = { image: { x: 173, y: 113, scale: 0.82 }, edge: INSPECTOR.x };

const mix = (from: number, to: number, t: number) => from + (to - from) * t;
export const mixFrames = (from: Frame, to: Frame, t: number): Frame => ({
  image: {
    x: mix(from.image.x, to.image.x, t),
    y: mix(from.image.y, to.image.y, t),
    scale: mix(from.image.scale, to.image.scale, t),
  },
  edge: mix(from.edge, to.edge, t),
});

/** Lines stop short of their row, so the round cap doesn't poke into the card. */
const LINE_GAP = 4;
const STEEP = 1 / Math.tan((75 * Math.PI) / 180);

/** In list order, top to bottom, grouped as `GROUPS`. */
export const CONTROLS: Control[] = [
  // The wheel carries two dots: turning it, and clicking it.
  { id: "wheel", group: "scrolling", spot: { x: 184, y: 118 } },
  { id: "thumbwheel", group: "scrolling", spot: { x: 80, y: 215 } },
  { id: "middle", group: "buttons", spot: { x: 184, y: 85 } },
  { id: "smartshift", group: "buttons", spot: { x: 184, y: 202 } },
  { id: "back", group: "buttons", spot: { x: 80, y: 325 } },
  { id: "forward", group: "buttons", spot: { x: 80, y: 295 } },
  { id: "gesture", group: "buttons", spot: { x: 80, y: 265 } },
  { id: "haptic", group: "buttons", spot: { x: 43.5, y: 258 } },
  // The sensor is underneath, so its spot sits on the ring that marks it on the palm rest.
  { id: "sensor", group: "pointer", spot: { x: 186, y: 333 } },
];

/** Where each group's heading and each control's row sit in the list, for the controls this mouse has. */
export function layoutList(present: readonly Control[]) {
  const headers: { id: GroupId; label: string; top: number }[] = [];
  const rows = new Map<ControlId, { top: number; first: boolean }>();
  let y = LIST.padding;
  for (const group of GROUPS) {
    const members = present.filter((control) => control.group === group.id);
    if (!members.length) continue;
    headers.push({ ...group, top: y });
    y += LIST.header;
    members.forEach((control, i) => {
      rows.set(control.id, { top: y, first: i === 0 });
      y += LIST.row;
    });
  }
  return { headers, rows, height: y + LIST.padding };
}

export const imageSize = ({ image }: Frame) => ({ width: DRAWING.width * image.scale, height: DRAWING.height * image.scale });

export const dotAt = (control: Control, { image }: Frame): Point => ({
  x: image.x + control.spot.x * image.scale,
  y: image.y + control.spot.y * image.scale,
});

/** Where the line meets the list: the left edge of the row, level with its middle. */
export const rowAnchor = (top: number, frame: Frame): Point => ({ x: frame.edge - LINE_GAP, y: top + LIST.row / 2 });

export function route(control: Control, top: number, frame: Frame): Point[] {
  const dot = dotAt(control, frame);
  const end = rowAnchor(top, frame);
  return [dot, { x: dot.x + Math.abs(dot.y - end.y) * STEEP, y: end.y }, end];
}

const ELBOW_RADIUS = 8;

/** The route as an SVG path, with its elbow rounded off. */
export function routePath(control: Control, top: number, frame: Frame) {
  const [from, elbow, to] = route(control, top, frame) as [Point, Point, Point];
  const into = Math.hypot(elbow.x - from.x, elbow.y - from.y);
  const out = Math.hypot(to.x - elbow.x, to.y - elbow.y);
  const radius = Math.min(ELBOW_RADIUS, into / 2, out / 2);
  if (radius < 0.5) return `M${from.x},${from.y} L${to.x},${to.y}`;
  const before = { x: elbow.x - ((elbow.x - from.x) / into) * radius, y: elbow.y - ((elbow.y - from.y) / into) * radius };
  const after = { x: elbow.x + ((to.x - elbow.x) / out) * radius, y: elbow.y + ((to.y - elbow.y) / out) * radius };
  return `M${from.x},${from.y} L${before.x},${before.y} Q${elbow.x},${elbow.y} ${after.x},${after.y} L${to.x},${to.y}`;
}

export const isDefaultThumbWheel = (wheel: ThumbWheelSettings | undefined) =>
  !wheel || (wheel.mode === "scroll" && wheel.speed === 100 && !wheel.inverted);

/** Controls another app has diverted and MX Studio leaves at their default, so the other app acts on them. */
export function sharedControlCount(state: AgentState) {
  const device = state.device;
  if (!device) return 0;
  const bound = new Set(state.bindings.map((b) => b.button));
  const buttons = device.buttons.filter((b) => b.divertedElsewhere && !bound.has(b.id)).length;
  return buttons + (device.thumbWheel?.divertedElsewhere && isDefaultThumbWheel(state.thumbWheel) ? 1 : 0);
}
