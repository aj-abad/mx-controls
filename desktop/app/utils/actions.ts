import type { ButtonAction, GestureActions } from "~/composables/useAgent";

const IS_MAC = navigator.userAgent.includes("Mac");
/** The modifier for app shortcuts: Ctrl, or ⌘ on a Mac. */
const MOD = IS_MAC ? "MetaLeft" : "ControlLeft";

export const SYSTEM_ACTIONS: Record<string, string> = {
  taskView: "Task View",
  showDesktop: "Show desktop",
  zoomIn: "Zoom in",
  zoomOut: "Zoom out",
};

/** What the mouse's own back, forward and middle buttons do, by button ID; also the `click` action's buttons. */
export const MOUSE_BUTTONS: Record<string, string> = {
  back: "Back",
  forward: "Forward",
  middle: "Middle click",
};

export const GESTURE_PARTS: { id: keyof GestureActions; label: string; glyph: string }[] = [
  { id: "click", label: "Click", glyph: "•" },
  { id: "up", label: "Up", glyph: "↑" },
  { id: "down", label: "Down", glyph: "↓" },
  { id: "left", label: "Left", glyph: "←" },
  { id: "right", label: "Right", glyph: "→" },
];

export const NOTHING: ButtonAction = { type: "none" };

/** A named action the picker offers, so people choose "Copy" rather than build Ctrl + C. */
export interface Preset {
  id: string;
  group: string;
  label: string;
  action: ButtonAction;
  /** Other words to find it by. */
  keywords?: string;
}

const press = (...keys: string[]): ButtonAction => ({ type: "press", keys });
const click = (mouse: string): ButtonAction => ({ type: "click", mouse });
const system = (name: string): ButtonAction => ({ type: "system", system: name });

/** In picker order; the groups appear in the order their first preset does. */
export const PRESETS: Preset[] = [
  { id: "back", group: "Navigate", label: "Back", action: click("back"), keywords: "history previous page browser" },
  { id: "forward", group: "Navigate", label: "Forward", action: click("forward"), keywords: "history next page browser" },
  { id: "middle", group: "Navigate", label: "Middle click", action: click("middle"), keywords: "mouse wheel open link" },
  { id: "newTab", group: "Navigate", label: "New tab", action: press(MOD, "KeyT"), keywords: "browser" },
  { id: "closeTab", group: "Navigate", label: "Close tab", action: press(MOD, "KeyW"), keywords: "browser" },
  { id: "reopenTab", group: "Navigate", label: "Reopen closed tab", action: press(MOD, "ShiftLeft", "KeyT"), keywords: "browser restore" },
  { id: "nextTab", group: "Navigate", label: "Next tab", action: press("ControlLeft", "Tab"), keywords: "browser switch" },
  { id: "previousTab", group: "Navigate", label: "Previous tab", action: press("ControlLeft", "ShiftLeft", "Tab"), keywords: "browser switch" },

  { id: "copy", group: "Edit", label: "Copy", action: press(MOD, "KeyC"), keywords: "clipboard" },
  { id: "cut", group: "Edit", label: "Cut", action: press(MOD, "KeyX"), keywords: "clipboard" },
  { id: "paste", group: "Edit", label: "Paste", action: press(MOD, "KeyV"), keywords: "clipboard" },
  { id: "undo", group: "Edit", label: "Undo", action: press(MOD, "KeyZ") },
  { id: "redo", group: "Edit", label: "Redo", action: IS_MAC ? press(MOD, "ShiftLeft", "KeyZ") : press(MOD, "KeyY") },
  { id: "selectAll", group: "Edit", label: "Select all", action: press(MOD, "KeyA") },

  { id: "taskView", group: "Windows & desktops", label: "Task View", action: system("taskView"), keywords: "mission control overview" },
  { id: "showDesktop", group: "Windows & desktops", label: "Show desktop", action: system("showDesktop"), keywords: "minimize hide" },
  {
    id: "desktopLeft",
    group: "Windows & desktops",
    label: "Desktop to the left",
    action: IS_MAC ? press("ControlLeft", "ArrowLeft") : press("ControlLeft", "MetaLeft", "ArrowLeft"),
    keywords: "virtual desktop space switch previous",
  },
  {
    id: "desktopRight",
    group: "Windows & desktops",
    label: "Desktop to the right",
    action: IS_MAC ? press("ControlLeft", "ArrowRight") : press("ControlLeft", "MetaLeft", "ArrowRight"),
    keywords: "virtual desktop space switch next",
  },
  { id: "zoomIn", group: "Windows & desktops", label: "Zoom in", action: system("zoomIn"), keywords: "bigger magnify" },
  { id: "zoomOut", group: "Windows & desktops", label: "Zoom out", action: system("zoomOut"), keywords: "smaller" },

  { id: "playPause", group: "Media", label: "Play or pause", action: press("MediaPlayPause"), keywords: "music video" },
  { id: "nextTrack", group: "Media", label: "Next track", action: press("MediaTrackNext"), keywords: "music skip" },
  { id: "previousTrack", group: "Media", label: "Previous track", action: press("MediaTrackPrevious"), keywords: "music" },
  { id: "volumeUp", group: "Media", label: "Volume up", action: press("AudioVolumeUp"), keywords: "louder sound" },
  { id: "volumeDown", group: "Media", label: "Volume down", action: press("AudioVolumeDown"), keywords: "quieter sound" },
  { id: "mute", group: "Media", label: "Mute", action: press("AudioVolumeMute"), keywords: "sound silence" },
];

const PRESETS_BY_ID = new Map(PRESETS.map((preset) => [preset.id, preset]));
export const presetById = (id: string) => PRESETS_BY_ID.get(id);

// Repeating doesn't change what an action is, so it's left out when matching one to a preset.
const identity = (action: ButtonAction) =>
  [action.type, action.keys?.join("+") ?? "", action.system ?? "", action.mouse ?? ""].join("|");
const PRESETS_BY_IDENTITY = new Map(PRESETS.map((preset) => [identity(preset.action), preset]));

export const presetFor = (action: ButtonAction) => PRESETS_BY_IDENTITY.get(identity(action));

/** Gestures filled in one go, by preset ID for each part. */
export const GESTURE_PRESETS: { id: string; label: string; parts: Record<keyof GestureActions, string> }[] = [
  {
    id: "desktops",
    label: "Desktops",
    parts: { click: "taskView", up: "taskView", down: "showDesktop", left: "desktopLeft", right: "desktopRight" },
  },
  {
    id: "browser",
    label: "Browser",
    parts: { click: "reopenTab", up: "newTab", down: "closeTab", left: "back", right: "forward" },
  },
  {
    id: "media",
    label: "Media",
    parts: { click: "playPause", up: "volumeUp", down: "volumeDown", left: "previousTrack", right: "nextTrack" },
  },
];

export function chordLabel(keys: readonly string[] | null | undefined) {
  return keys?.length ? keys.map(keyLabel).join(" + ") : "No keys";
}

export function describeAction(action: ButtonAction) {
  const repeat = action.repeat ? ", repeating" : "";
  const preset = presetFor(action);
  if (preset) return preset.label + repeat;
  switch (action.type) {
    case "hold":
      return `Hold ${chordLabel(action.keys)}`;
    case "press":
      return chordLabel(action.keys) + repeat;
    case "system":
      return (SYSTEM_ACTIONS[action.system ?? ""] ?? action.system ?? "System action") + repeat;
    case "click":
      return MOUSE_BUTTONS[action.mouse ?? ""] ?? "Mouse button";
    default:
      return "Nothing";
  }
}
