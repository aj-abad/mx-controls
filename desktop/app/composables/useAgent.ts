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

export interface GestureActions {
  click: ButtonAction;
  up: ButtonAction;
  down: ButtonAction;
  left: ButtonAction;
  right: ButtonAction;
}

export interface ButtonBinding {
  button: string;
  mode: "action" | "gesture";
  action?: ButtonAction | null;
  gesture?: GestureActions | null;
}

/** A diverted control going down or up; the agent only hears the controls it or another app diverted. */
export interface ButtonEvent {
  button: string | null;
  cid: number;
  down: boolean;
}

export interface GestureEvent {
  button: string | null;
  direction: keyof GestureActions;
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
/** The last diverted control pressed down. A new object each time, so pressing the same one again still notifies. */
const lastPress = shallowRef<ButtonEvent | null>(null);
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
  "setHostName",
]);
const DEBOUNCE_MS = 150;

interface Overlay {
  token: number;
  method: string;
  params: Record<string, unknown>;
}
const overlays = shallowRef(new Map<string, Overlay>());
let nextToken = 0;

function setOverlay(overlay: Overlay | null, method: string, token?: number) {
  const current = overlays.value.get(method);
  // A newer change to the same setting keeps showing until its own answer comes.
  if (!overlay && current?.token !== token) return;
  const next = new Map(overlays.value);
  if (overlay) next.set(method, overlay);
  else next.delete(method);
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

async function send(method: string, params: Record<string, unknown>, failure: string) {
  try {
    const result = await call<AgentState | null>(method, params);
    // The reply is the state the request left, so whatever follows sees it without waiting for the event.
    if (result && typeof result === "object" && "revision" in result) receive(result);
    return true;
  } catch (error) {
    showToast(`${failure}: ${String(error)}`, "error");
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
 * Sends a request, raising a toast that starts with `failure` if the agent or mouse refuses it. A setting
 * shows its new value at once and goes back to the agent's if it's refused. Resolves to whether it took.
 */
function attempt(method: string, params: Record<string, unknown>, failure: string): Promise<boolean> {
  if (!OPTIMISTIC.has(method)) return send(method, params, failure);
  const token = ++nextToken;
  setOverlay({ token, method, params }, method);
  return new Promise((resolve) => {
    const batch = pending.get(method) ?? { params, failure, waiting: [] };
    clearTimeout(batch.timer);
    Object.assign(batch, { params, failure });
    batch.waiting.push(resolve);
    batch.timer = setTimeout(async () => {
      pending.delete(method);
      const done = await send(method, batch.params, batch.failure);
      setOverlay(null, method, token);
      for (const settle of batch.waiting) settle(done);
    }, DEBOUNCE_MS);
    pending.set(method, batch);
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
  return { state, device, pipeConnected, lastGesture, lastPress, call, attempt, captureKeys };
}
