import type { ButtonAction } from "~/composables/useAgent";

const IS_MAC = navigator.userAgent.includes("Mac");
/** The modifier for app shortcuts: Ctrl, or ⌘ on a Mac. */
const MOD = IS_MAC ? "MetaLeft" : "ControlLeft";

/** Each named system action as this platform calls it. */
export const SYSTEM_ACTIONS: Record<string, string> = {
  taskView: IS_MAC ? "Mission Control" : "Task View",
  appExpose: "App Exposé",
  showDesktop: "Show desktop",
  launchpad: "Launchpad",
  switchApps: "Switch to last app",
  zoomIn: "Zoom in",
  zoomOut: "Zoom out",
  search: IS_MAC ? "Spotlight" : "Search",
  screenshot: "Screen capture",
  emoji: "Emoji",
  dictation: "Dictation",
  notifications: "Notification center",
  lockScreen: "Lock screen",
  fileManager: IS_MAC ? "Finder" : "File Explorer",
  settings: IS_MAC ? "System Settings" : "Settings",
  calculator: "Calculator",
  minimizeWindow: "Minimize window",
  maximizeWindow: "Maximize window",
  fullScreen: "Full screen",
  closeWindow: "Close window",
  quitApp: "Quit app",
  micMute: "Mute microphone",
  brightnessUp: "Brightness up",
  brightnessDown: "Brightness down",
  lookUp: "Look up",
};

/** What the mouse's own back, forward and middle buttons do, by button ID; also the `click` action's buttons. */
export const MOUSE_BUTTONS: Record<string, string> = {
  back: "Back",
  forward: "Forward",
  middle: "Middle click",
};

export const NOTHING: ButtonAction = { type: "none" };

/** Whether an optional action, such as a double click, is set; "none" counts as unset. */
export const hasAction = (action: ButtonAction | null | undefined): action is ButtonAction => !!action && action.type !== "none";

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
/** A system action named as this platform calls it. */
const named = (id: string, group: string, keywords: string): Preset => ({
  id,
  group,
  label: SYSTEM_ACTIONS[id]!,
  action: system(id),
  keywords,
});

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

  named("taskView", "Windows & desktops", "mission control task view overview expose windows"),
  named("appExpose", "Windows & desktops", "app windows expose mission control"),
  named("showDesktop", "Windows & desktops", "minimize hide"),
  named("launchpad", "Windows & desktops", "apps grid launcher"),
  named("switchApps", "Windows & desktops", "alt tab cmd tab previous application"),
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
  named("minimizeWindow", "Windows & desktops", "hide dock taskbar"),
  named("maximizeWindow", "Windows & desktops", "restore bigger"),
  named("fullScreen", "Windows & desktops", "fullscreen f11"),
  named("closeWindow", "Windows & desktops", "alt f4 cmd w"),
  named("quitApp", "Windows & desktops", "exit close application cmd q"),
  named("zoomIn", "Windows & desktops", "bigger magnify"),
  named("zoomOut", "Windows & desktops", "smaller"),

  { id: "playPause", group: "Media", label: "Play or pause", action: press("MediaPlayPause"), keywords: "music video" },
  { id: "nextTrack", group: "Media", label: "Next track", action: press("MediaTrackNext"), keywords: "music skip" },
  { id: "previousTrack", group: "Media", label: "Previous track", action: press("MediaTrackPrevious"), keywords: "music" },
  { id: "volumeUp", group: "Media", label: "Volume up", action: press("AudioVolumeUp"), keywords: "louder sound" },
  { id: "volumeDown", group: "Media", label: "Volume down", action: press("AudioVolumeDown"), keywords: "quieter sound" },
  { id: "mute", group: "Media", label: "Mute", action: press("AudioVolumeMute"), keywords: "sound silence" },
  named("micMute", "Media", "mic microphone call meeting teams zoom"),
  named("brightnessUp", "Media", "screen display brighter"),
  named("brightnessDown", "Media", "screen display dimmer"),

  named("search", "System", "spotlight find search start"),
  named("screenshot", "System", "screenshot snip snipping print screen capture"),
  named("emoji", "System", "emoji picker symbols character"),
  named("dictation", "System", "voice typing speech"),
  named("notifications", "System", "action center alerts notification"),
  named("lockScreen", "System", "lock away security"),
  named("fileManager", "System", "finder explorer files folders"),
  named("settings", "System", "preferences control panel"),
  named("calculator", "System", "calc math"),
  named("lookUp", "System", "dictionary define word"),
];

const PRESETS_BY_ID = new Map(PRESETS.map((preset) => [preset.id, preset]));
export const presetById = (id: string) => PRESETS_BY_ID.get(id);

/** Whether this platform runs a preset; the agent says which system actions it has. */
export function presetAvailable(preset: Preset) {
  if (preset.action.type !== "system") return true;
  const supported = useAgent().state.value?.systemActions;
  return !supported || supported.includes(preset.action.system ?? "");
}

// Repeating doesn't change what an action is, so it's left out when matching one to a preset.
const identity = (action: ButtonAction) =>
  [action.type, action.keys?.join("+") ?? "", action.system ?? "", action.mouse ?? "", action.layer ?? "", action.profile ?? ""].join("|");
const PRESETS_BY_IDENTITY = new Map(PRESETS.map((preset) => [identity(preset.action), preset]));

export const presetFor = (action: ButtonAction) => PRESETS_BY_IDENTITY.get(identity(action));

export const APP_PREFIX = "app:";
const SCHEME = /^[a-z][a-z0-9+.-]+:/i;

/** What an `open` action's target is, as the agent reads it: an app by `app:`, a link by its scheme, or a path. */
export function openKind(target: string | null | undefined): "app" | "url" | "path" {
  const value = target ?? "";
  if (value.startsWith(APP_PREFIX)) return "app";
  return SCHEME.test(value) ? "url" : "path";
}

/** Whether an `open` action has something to open; a new one starts as just `app:` or `https://`. */
export const openComplete = (target: string | null | undefined) =>
  !!target?.trim() && target !== APP_PREFIX && !/^[a-z][a-z0-9+.-]+:\/\/$/i.test(target);

/** A short name for what an `open` action opens: the app's name, a site's host, or a file or folder's name. */
export function openLabel(action: ButtonAction) {
  const target = action.open ?? "";
  const kind = openKind(target);
  if (kind === "app") return action.name || "an app";
  if (kind === "url") {
    try {
      const url = new URL(target);
      return url.host || `${url.protocol.replace(/:$/, "")} link`;
    } catch {
      return target;
    }
  }
  return target.replace(/[\\/]+$/, "").split(/[\\/]/).pop() || target;
}

export function chordLabel(keys: readonly string[] | null | undefined) {
  return keys?.length ? keys.map(keyLabel).join(" + ") : "No keys";
}

export function describeAction(action: ButtonAction) {
  const repeat = action.repeat ? ", repeating" : "";
  const preset = presetFor(action);
  if (preset) return preset.label + repeat;
  switch (action.type) {
    case "hold":
      return `${chordLabel(action.keys)} key`;
    case "press":
      return chordLabel(action.keys) + repeat;
    case "system":
      return (SYSTEM_ACTIONS[action.system ?? ""] ?? action.system ?? "System action") + repeat;
    case "click":
      return MOUSE_BUTTONS[action.mouse ?? ""] ?? "Mouse button";
    case "macro":
      return (useAgent().state.value?.macros.find((m) => m.id === action.macro)?.name ?? "Deleted macro") + repeat;
    case "layer":
      return action.layer === "momentary" ? "Hold second layer" : "Toggle second layer";
    case "profile": {
      const name = useAgent().state.value?.profiles.find((p) => p.id === action.profile)?.name;
      return name ? `Switch to ${name}` : "Switch to a deleted profile";
    }
    case "open":
      return openComplete(action.open) ? `Open ${openLabel(action)}` : "Open…";
    case "appleShortcut":
      return action.name ? `Run “${action.name}”` : "Run a Shortcut";
    default:
      return "Nothing";
  }
}
