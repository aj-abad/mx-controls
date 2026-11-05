import net from "node:net";
import { once } from "node:events";
import { initialState } from "./state";
import type { Macro } from "../../app/composables/useAgent";

export class MockAgent {
  state = initialState();
  requests: { method: string; params: Record<string, unknown> }[] = [];
  capturing = false;
  private sockets = new Set<net.Socket>();
  private failures = new Map<string, string>();
  private deferred = new Map<string, Promise<void>>();
  private server = net.createServer(socket => {
    this.sockets.add(socket);
    socket.on("close", () => this.sockets.delete(socket));
    socket.on("error", () => {});
    let buffer = "";
    socket.on("data", data => {
      buffer += data.toString();
      let end: number;
      while ((end = buffer.indexOf("\n")) !== -1) {
        const line = buffer.slice(0, end); buffer = buffer.slice(end + 1);
        try {
          const { id, method, params = {} } = JSON.parse(line);
          this.requests.push({ method, params: structuredClone(params) });
          try {
            const failure = this.failures.get(method);
            this.failures.delete(method);
            if (failure) throw new Error(failure);
            const result = this.call(method, params);
            const wait = this.deferred.get(method);
            this.deferred.delete(method);
            if (wait) void wait.then(() => { if (!socket.destroyed) socket.write(JSON.stringify({ id, result }) + "\n"); });
            else socket.write(JSON.stringify({ id, result }) + "\n");
          } catch (error) {
            socket.write(JSON.stringify({ id, error: String((error as Error).message) }) + "\n");
          }
        } catch { socket.destroy(new Error("Invalid test protocol request")); }
      }
    });
  });

  async listen(pipe: string) {
    if (!/^mx-controls-ui-test-[a-f0-9-]{36}$/.test(pipe)) throw new Error("Refusing non-test pipe");
    this.server.listen(`\\\\.\\pipe\\${pipe}`);
    await once(this.server, "listening");
  }

  failNext(method: string, message: string) { this.failures.set(method, message); }
  deferNextReply(method: string) {
    let release!: () => void;
    this.deferred.set(method, new Promise<void>(resolve => { release = resolve; }));
    return release;
  }
  disconnect() {
    this.capturing = false;
    for (const socket of this.sockets) socket.destroy();
  }
  expireCapture() {
    this.capturing = false;
    this.emit("keyCapture", { active: false });
  }
  emit(event: string, data: unknown) {
    for (const socket of this.sockets) socket.write(JSON.stringify({ event, data }) + "\n");
  }
  key(code: string, down: boolean) {
    if (!this.capturing) throw new Error("Synthetic keys require active mock capture");
    this.emit("key", { code, down });
  }
  async close() {
    for (const socket of this.sockets) socket.destroy();
    if (this.server.listening) await new Promise<void>(resolve => this.server.close(() => resolve()));
  }

  private profile(id: string | undefined) {
    const profile = this.state.profiles.find(p => p.id === (id ?? this.state.activeProfile));
    if (!profile) throw new Error("Unknown profile");
    return profile;
  }

  /** The running profile's main layer, which the agent repeats at the top of its state. */
  private mirrorRunning() {
    const running = this.profile(undefined);
    this.state.bindings = structuredClone(running.bindings);
    this.state.thumbWheel = structuredClone(running.thumbWheel);
  }

  private call(method: string, params: Record<string, any>): unknown {
    switch (method) {
      case "getState": return this.state;
      case "startKeyCapture": this.capturing = true; return {};
      // The real service publishes keyCapture=false on idle timeout, not on an explicit Stop.
      case "stopKeyCapture": this.capturing = false; return {};
      case "setMacro": {
        const macro = structuredClone(params.macro) as Macro;
        const index = this.state.macros.findIndex(m => m.id === macro.id);
        if (index < 0) this.state.macros.push(macro); else this.state.macros[index] = macro;
        break;
      }
      case "deleteMacro": this.state.macros = this.state.macros.filter(m => m.id !== params.id); break;
      case "setGesturePreset": {
        const index = this.state.gesturePresets.findIndex(p => p.id === params.preset.id);
        if (index < 0) this.state.gesturePresets.push(params.preset); else this.state.gesturePresets[index] = params.preset;
        break;
      }
      case "deleteGesturePreset": this.state.gesturePresets = this.state.gesturePresets.filter(p => p.id !== params.id); break;
      case "setBindings": {
        const profile = this.profile(params.profile);
        if (params.layer) profile.layer.bindings = params.bindings; else profile.bindings = params.bindings;
        this.mirrorRunning();
        break;
      }
      case "setThumbWheel": {
        const profile = this.profile(params.profile);
        if (params.layer) profile.layer.thumbWheel = params.thumbWheel; else profile.thumbWheel = params.thumbWheel;
        this.mirrorRunning();
        break;
      }
      case "renameProfile": this.state.profiles.find(p => p.id === params.id)!.name = params.name; break;
      case "setProfileIcon": this.state.profiles.find(p => p.id === params.id)!.icon = params.icon; break;
      case "setStartAtLogin": this.state.startAtLogin = params.enabled; break;
      case "switchProfile": {
        if (!this.state.profiles.some(p => p.id === params.id)) throw new Error("Unknown profile");
        this.state.activeProfile = this.state.selectedProfile = params.id;
        this.mirrorRunning();
        break;
      }
      default: throw new Error(`Unsupported mock-agent method: ${method}`);
    }
    this.state.revision++;
    this.emit("state", this.state);
    return this.state;
  }
}
