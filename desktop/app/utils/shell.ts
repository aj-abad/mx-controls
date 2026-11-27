import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { getCurrentWindow } from "@tauri-apps/api/window";

/**
 * What hosts the page: the Tauri app, which relays to the agent over its pipe; the agent's own Photino window,
 * which takes the pipe's wire format over the webview's message bridge; or a plain browser, for previews.
 */
export type Shell = "tauri" | "photino" | null;

/** The bridge Photino puts on `window.external`. */
interface PhotinoBridge {
  sendMessage(message: string): void;
  receiveMessage(callback: (message: string) => void): void;
}

function photinoBridge(): PhotinoBridge | null {
  const external = (window as { external?: Partial<PhotinoBridge> }).external;
  return typeof external?.sendMessage === "function" && typeof external.receiveMessage === "function"
    ? (external as PhotinoBridge)
    : null;
}

export const shell: Shell = "__TAURI_INTERNALS__" in window ? "tauri" : photinoBridge() ? "photino" : null;

export interface AgentEvent {
  event: string;
  data: unknown;
}

// As long as the Tauri relay waits for a reply.
const REQUEST_TIMEOUT_MS = 10_000;

const pending = new Map<number, { resolve: (value: unknown) => void; reject: (reason: unknown) => void }>();
const eventHandlers = new Set<(event: AgentEvent) => void>();
let nextId = 0;
let photino: PhotinoBridge | null = null;

/** The bridge, listening for replies and events from the first use on. */
function bridge(): PhotinoBridge {
  if (photino) return photino;
  photino = photinoBridge()!;
  photino.receiveMessage((text) => {
    const message = JSON.parse(text) as { id?: number; result?: unknown; error?: string; event?: string; data?: unknown };
    if (message.event === "automation.eval") {
      void runAutomation(message.data as { id: number; script: string });
      return;
    }
    if (message.event !== undefined) {
      for (const handler of eventHandlers) handler(message as AgentEvent);
      return;
    }
    const call = pending.get(message.id!);
    if (!call) return;
    pending.delete(message.id!);
    // Rejected with the message alone, as Tauri rejects.
    if (message.error !== undefined) call.reject(message.error);
    else call.resolve(message.result);
  });
  return photino;
}

/**
 * Runs a script a tool sent through the agent's `ui.eval`, which the agent only sends when it started with
 * automation on, and answers with what it returned as JSON. The script is the body of an async function.
 */
async function runAutomation({ id, script }: { id: number; script: string }) {
  let params: { id: number; result?: string; error?: string };
  try {
    // eslint-disable-next-line @typescript-eslint/no-implied-eval
    const run = new Function(`return (async () => {\n${script}\n})();`) as () => Promise<unknown>;
    params = { id, result: JSON.stringify((await run()) ?? null) };
  } catch (error) {
    params = { id, error: error instanceof Error ? `${error.name}: ${error.message}` : String(error) };
  }
  bridge().sendMessage(JSON.stringify({ id: 0, method: "ui.evalResult", params }));
}

function photinoCall<T>(method: string, params: Record<string, unknown> = {}): Promise<T> {
  const id = ++nextId;
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => {
      pending.delete(id);
      reject("The agent didn't answer.");
    }, REQUEST_TIMEOUT_MS);
    pending.set(id, {
      resolve: (value) => (clearTimeout(timer), resolve(value as T)),
      reject: (reason) => (clearTimeout(timer), reject(reason)),
    });
    bridge().sendMessage(JSON.stringify({ id, method, params }));
  });
}

/** Asks the agent; rejects with the agent's message when it refuses. */
export function agentCall<T>(method: string, params: Record<string, unknown> = {}): Promise<T> {
  return shell === "photino" ? photinoCall<T>(method, params) : invoke<T>("agent_call", { method, params });
}

/** What the agent pushes: state, keys, gestures and the rest. */
export function onAgentEvent(handler: (event: AgentEvent) => void) {
  if (shell === "photino") {
    bridge();
    eventHandlers.add(handler);
  } else void listen<AgentEvent>("agent://event", ({ payload }) => handler(payload));
}

/** The relay to the agent coming and going. The agent's own window is the agent, so it's never apart from it. */
export function onAgentConnection(handler: (connected: boolean) => void) {
  if (shell === "tauri") void listen<boolean>("agent://connection", ({ payload }) => handler(payload));
}

export function agentConnected(): Promise<boolean> {
  return shell === "photino" ? Promise.resolve(true) : invoke<boolean>("agent_connected");
}

export function startWindowDrag() {
  if (shell === "photino") void photinoCall("window.drag");
  else if (shell === "tauri") void getCurrentWindow().startDragging();
}

export function minimizeWindow() {
  if (shell === "photino") void photinoCall("window.minimize");
  else if (shell === "tauri") void getCurrentWindow().minimize();
}

export function closeWindow() {
  if (shell === "photino") void photinoCall("window.close");
  else if (shell === "tauri") void getCurrentWindow().close();
}
