import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { toast } from "@arcon.mobi/sine/composables/useToast";

/** Mirrors `agent/MxControls.Agent/Protocol.cs`. */
export interface AgentState {
  connected: boolean;
  device: DeviceState | null;
  mappings: ButtonMapping[];
  optionsPlusRunning: boolean;
  error: string | null;
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

export interface ButtonMapping {
  button: string;
  key: string;
}

interface ButtonEvent {
  button: string | null;
  cid: number;
  down: boolean;
}

const state = shallowRef<AgentState | null>(null);
const pipeConnected = ref(false);
const held = ref(new Set<string>());
let started = false;

const inTauri = () => "__TAURI_INTERNALS__" in window;

async function call<T = AgentState>(method: string, params: Record<string, unknown> = {}) {
  if (!inTauri()) {
    state.value = previewCall(state.value!, method, params);
    return state.value as T;
  }
  return await invoke<T>("agent_call", { method, params });
}

/** Sends a request, raising a toast that starts with `failure` if the agent or mouse refuses it. */
async function attempt(method: string, params: Record<string, unknown>, failure: string) {
  try {
    await call(method, params);
    return true;
  } catch (error) {
    toast.error(`${failure}: ${String(error)}`);
    return false;
  }
}

async function refresh() {
  try {
    state.value = await call("getState");
  } catch {
    // The connection event brings the state once the agent answers.
  }
}

function start() {
  if (started) return;
  started = true;
  if (!inTauri()) {
    state.value = previewState();
    pipeConnected.value = true;
    return;
  }
  void listen<{ event: string; data: unknown }>("agent://event", ({ payload }) => {
    if (payload.event === "state") state.value = payload.data as AgentState;
    if (payload.event === "button") {
      const { button, down } = payload.data as ButtonEvent;
      if (!button) return;
      const next = new Set(held.value);
      if (down) next.add(button);
      else next.delete(button);
      held.value = next;
    }
  });
  void listen<boolean>("agent://connection", ({ payload }) => {
    pipeConnected.value = payload;
    if (payload) void refresh();
    else held.value = new Set();
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
  return { state, device, pipeConnected, held, call, attempt };
}
