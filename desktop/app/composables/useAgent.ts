import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";

/** Mirrors `agent/MxControls.Agent/Protocol.cs`. */
export interface AgentState {
  connected: boolean;
  device: DeviceState | null;
  /** The active profile's bindings. */
  bindings: ButtonBinding[];
  /** The active profile's thumb wheel settings. */
  thumbWheel: ThumbWheelSettings;
  profiles: ProfileSummary[];
  activeProfile: string;
  startAtLogin: boolean;
  optionsPlusRunning: boolean;
  error: string | null;
  /** Goes up with every change the agent makes; a state with a lower one is out of date. */
  revision: number;
  /** How long a click waits for another, on buttons with a double click set. One setting for every button. */
  multiClickMs: number;
  /** Every preset: the built-in ones, as edited if they have been, then the user's own. */
  gesturePresets: GesturePreset[];
  /** The built-in presets as they come, to tell an edited one and reset it. */
  builtInPresets: GesturePreset[];
  /** Which buttons follow which preset, in every profile. */
  presetUses: { preset: string; profile: string; button: string }[];
}

export interface DeviceState {
  name: string;
  productId: string;
  firmware: string;
  battery: { percent: number; charging: boolean };
  dpi: { current: number; default: number; min: number; max: number; step: number };
  smartShift: SmartShift;
  haptics: { enabled: boolean; level: number; waveforms: string[] };
  forceButton: { current: number; default: number; min: number; max: number } | null;
  buttons: ButtonState[];
  /** Null for a mouse without one. `divertedElsewhere`: Logi Options+ or another app had taken it first. */
  thumbWheel: { divertedElsewhere: boolean } | null;
  /** The Easy-Switch channels, or null for a mouse that can't describe them. */
  hosts: HostsInfo | null;
}

export interface ProfileSummary {
  id: string;
  name: string;
  /** A key of `PROFILE_ICONS`, or null for the profile's initial. */
  icon: string | null;
}

export interface HostsInfo {
  /** The channel this computer is on, from 0. */
  current: number;
  canRename: boolean;
  maxNameLength: number;
  channels: { index: number; paired: boolean; name: string }[];
}

/** Mirrors `agent/MxControls.Core/Actions/ThumbWheel.cs`. */
export interface ThumbWheelSettings {
  mode: "scroll" | "actions";
  /** Percent, from 25 to 300. */
  speed: number;
  inverted: boolean;
  /** For `actions`: run once a step. */
  left?: ButtonAction | null;
  right?: ButtonAction | null;
}

export interface SmartShift {
  ratchet: boolean;
  autoDisengage: number;
  torque: number;
  tunableTorque: boolean;
  defaultAutoDisengage: number;
  defaultTorque: number;
}

export interface ButtonState {
  id: string;
  name: string;
  cid: number;
  divertedElsewhere: boolean;
}

/** Mirrors `agent/MxControls.Core/Actions/ButtonAction.cs`. */
export interface ButtonAction {
  type: "none" | "hold" | "press" | "system" | "click";
  /** `KeyboardEvent.code` names in press order, modifiers first. */
  keys?: string[] | null;
  system?: string | null;
  repeat?: boolean;
  /** For `click`: "back", "forward" or "middle", held as long as the button is. */
  mouse?: string | null;
}

export type Direction = "up" | "down" | "left" | "right";

/**
 * A step of a gesture: its action, and the steps beyond it, the same way to go further or at right angles to
 * turn. Not both, and never back the way it came.
 */
export interface GestureStep extends ButtonAction {
  up?: GestureStep | null;
  down?: GestureStep | null;
  left?: GestureStep | null;
  right?: GestureStep | null;
}

/** Saved moves buttons follow, so editing one changes every button that uses it. Clicks stay on each button. */
export interface GesturePreset extends Record<Direction, GestureStep> {
  id: string;
  name: string;
  /** Distance between steps in a run, 4 to 15 mm. */
  spacingMm: number;
  icon?: string | null;
}

/** A gesture carried whole, from before presets; the agent moves these into presets when it loads them. */
export type GestureActions = Record<Direction, GestureStep> & {
  click: ButtonAction;
  doubleClick?: ButtonAction | null;
  tripleClick?: ButtonAction | null;
  spacingMm?: number;
};

/** A gesture follows `preset`, with `action` and the double and triple clicks as its clicks. */
export interface ButtonBinding {
  button: string;
  mode: "action" | "gesture";
  action?: ButtonAction | null;
  gesture?: GestureActions | null;
  /** What two quick clicks run. */
  doubleClick?: ButtonAction | null;
  /** What three quick clicks run; needs a double click. */
  tripleClick?: ButtonAction | null;
  /** For `gesture`: the preset it follows. */
  preset?: string | null;
}

/** A diverted control going down or up; the agent only hears the controls it or another app diverted. */
export interface ButtonEvent {
  button: string | null;
  cid: number;
  down: boolean;
}

export interface GestureEvent {
  button: string | null;
  /** The last of `path`, or null once a path is backed out of. */
  direction: Direction | null;
  /** One direction per step. */
  path: Direction[];
  /** "fire" when it ran, "step" when the path changed without running. */
  phase: "fire" | "step";
}

/** A click that ran: 1, 2 or 3 for a single, double or triple click. */
export interface ClickEvent {
  button: string | null;
  count: number;
}

/** A key the agent captured while recording a shortcut; `code` is null for keys it can't send. */
export interface AgentKeyEvent {
  code: string | null;
  down: boolean;
}

interface KeyHandlers {
  key(event: AgentKeyEvent): void;
  /** The agent stopped recording on its own after a while without keys. */
  ended(): void;
}

/** The agent's latest state, as it last said. */
const agentState = shallowRef<AgentState | null>(null);
const pipeConnected = ref(false);
const lastGesture = shallowRef<GestureEvent | null>(null);
/** The last click a bound button ran, as the agent counted it. A new object each time. */
const lastClick = shallowRef<ClickEvent | null>(null);
/** The last diverted control pressed down. A new object each time, so pressing the same one again still notifies. */
const lastPress = shallowRef<ButtonEvent | null>(null);
/** What the last request that failed said, until it's dismissed or a later one goes through. */
const failure = shallowRef<{ id: number; message: string } | null>(null);
let failures = 0;
let started = false;
let keyHandlers: KeyHandlers | null = null;
// Revisions start again when the agent does, so the floor drops each time the pipe connects.
let revisionFloor = -1;

const inTauri = () => "__TAURI_INTERNALS__" in window;

/** Takes a state from the agent unless a newer one has already arrived. */
function receive(next: AgentState) {
  if (next.revision < revisionFloor) return;
  revisionFloor = next.revision;
  agentState.value = next;
}

/**
 * Settings changed optimistically: they show at once, as `previewCall` applies them, until the agent answers.
 * The keys are the methods, and changes to one setting within the debounce reach the agent as one request.
 */
const OPTIMISTIC = new Set([
  "setDpi",
  "setSmartShift",
  "setHapticLevel",
  "setForce",
  "setThumbWheel",
  "setBindings",
  "switchProfile",
  "setProfileIcon",
  "setStartAtLogin",
  "setMultiClickMs",
  "setHostName",
  "setGesturePreset",
]);

/** Each preset is its own setting, so changes to two presets in a row don't stand in for each other. */
const settingKey = (method: string, params: Record<string, unknown>) =>
  method === "setGesturePreset" ? `${method}:${(params.preset as { id: string }).id}` : method;
const DEBOUNCE_MS = 150;

interface Overlay {
  token: number;
  method: string;
  params: Record<string, unknown>;
}
const overlays = shallowRef(new Map<string, Overlay>());
let nextToken = 0;

function setOverlay(overlay: Overlay | null, key: string, token?: number) {
  const current = overlays.value.get(key);
  // A newer change to the same setting keeps showing until its own answer comes.
  if (!overlay && current?.token !== token) return;
  const next = new Map(overlays.value);
  if (overlay) next.set(key, overlay);
  else next.delete(key);
  overlays.value = next;
}

/** What the window shows: the agent's state with the changes it hasn't answered yet laid over it. */
const state = computed(() => {
  let shown = agentState.value;
  if (!shown) return shown;
  for (const { method, params } of overlays.value.values()) {
    try {
      shown = previewCall(shown, method, params);
    } catch {
      // A change that can't be shown yet is left to the agent's answer.
    }
  }
  return shown;
});

async function call<T = AgentState>(method: string, params: Record<string, unknown> = {}) {
  if (!inTauri()) {
    const current = agentState.value!;
    receive({ ...previewCall(current, method, params), revision: current.revision + 1 });
    return agentState.value as T;
  }
  return await invoke<T>("agent_call", { method, params });
}

/** Says something went wrong; the layout shows it above the page. */
export function reportFailure(message: string) {
  failure.value = { id: ++failures, message };
}

function dismissFailure() {
  failure.value = null;
}

async function send(method: string, params: Record<string, unknown>, failed: string) {
  try {
    const result = await call<AgentState | null>(method, params);
    // The reply is the state the request left, so whatever follows sees it without waiting for the event.
    if (result && typeof result === "object" && "revision" in result) receive(result);
    dismissFailure();
    return true;
  } catch (error) {
    reportFailure(`${failed}: ${String(error)}`);
    return false;
  }
}

interface Pending {
  params: Record<string, unknown>;
  failure: string;
  timer?: ReturnType<typeof setTimeout>;
  waiting: ((done: boolean) => void)[];
}
const pending = new Map<string, Pending>();

/**
 * Sends a request, reporting a failure that starts with `failure` if the agent or mouse refuses it. A setting
 * shows its new value at once and goes back to the agent's if it's refused. Resolves to whether it took.
 */
function attempt(method: string, params: Record<string, unknown>, failure: string): Promise<boolean> {
  if (!OPTIMISTIC.has(method)) return send(method, params, failure);
  const key = settingKey(method, params);
  const token = ++nextToken;
  setOverlay({ token, method, params }, key);
  return new Promise((resolve) => {
    const batch = pending.get(key) ?? { params, failure, waiting: [] };
    clearTimeout(batch.timer);
    Object.assign(batch, { params, failure });
    batch.waiting.push(resolve);
    batch.timer = setTimeout(async () => {
      pending.delete(key);
      const done = await send(method, batch.params, batch.failure);
      setOverlay(null, key, token);
      for (const settle of batch.waiting) settle(done);
    }, DEBOUNCE_MS);
    pending.set(key, batch);
  });
}

async function refresh() {
  try {
    receive(await call("getState"));
  } catch {
    // The connection event brings the state once the agent answers.
  }
}

/**
 * Has the agent take over the keyboard so shortcuts such as Win or Alt+Tab reach `handlers` instead of
 * Windows. Resolves to a function that hands the keyboard back, or null where the agent can't do it,
 * in which case the caller records keys in the page itself.
 */
async function captureKeys(handlers: KeyHandlers): Promise<(() => void) | null> {
  if (!inTauri()) return null;
  keyHandlers = handlers;
  const release = () => {
    if (keyHandlers !== handlers) return;
    keyHandlers = null;
    void call("stopKeyCapture").catch(() => {});
  };
  try {
    await call("startKeyCapture");
    return release;
  } catch {
    if (keyHandlers === handlers) keyHandlers = null;
    return null;
  }
}

function start() {
  if (started) return;
  started = true;
  if (!inTauri()) {
    receive(previewState());
    pipeConnected.value = true;
    return;
  }
  void listen<{ event: string; data: unknown }>("agent://event", ({ payload }) => {
    if (payload.event === "state") receive(payload.data as AgentState);
    if (payload.event === "key") keyHandlers?.key(payload.data as AgentKeyEvent);
    if (payload.event === "keyCapture" && !(payload.data as { active: boolean }).active) keyHandlers?.ended();
    if (payload.event === "gesture") lastGesture.value = { ...(payload.data as GestureEvent) };
    if (payload.event === "click") lastClick.value = { ...(payload.data as ClickEvent) };
    if (payload.event === "button" && (payload.data as ButtonEvent).down) lastPress.value = { ...(payload.data as ButtonEvent) };
  });
  void listen<boolean>("agent://connection", ({ payload }) => {
    pipeConnected.value = payload;
    revisionFloor = -1;
    if (payload) void refresh();
  });
  void invoke<boolean>("agent_connected").then((connected) => {
    pipeConnected.value = connected;
    if (connected) void refresh();
  });
}

/** The agent's state and requests, shared by every page. */
export function useAgent() {
  start();
  const device = computed(() => (state.value?.connected ? state.value.device : null));
  return { state, device, pipeConnected, lastGesture, lastClick, lastPress, failure, dismissFailure, call, attempt, captureKeys };
}
