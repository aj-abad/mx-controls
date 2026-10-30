import type { AgentState } from "~/composables/useAgent";

/** Sample state for previewing the app in a browser, outside Tauri. It never talks to a mouse. */
export function previewState(): AgentState {
  return {
    connected: true,
    optionsPlusRunning: true,
    error: null,
    startAtLogin: true,
    revision: 0,
    thumbWheel: { mode: "scroll", speed: 100, inverted: false },
    profiles: [
      { id: "default", name: "Default" },
      { id: "games", name: "Games" },
    ],
    activeProfile: "default",
    bindings: [
      { button: "middle", mode: "action", action: { type: "hold", keys: ["ShiftLeft"] } },
      {
        button: "gesture",
        mode: "gesture",
        gesture: {
          click: { type: "press", keys: ["ControlLeft", "KeyC"] },
          up: { type: "press", keys: ["ControlLeft", "KeyV"] },
          down: { type: "press", keys: ["Delete"] },
          left: { type: "press", keys: ["ControlLeft", "KeyZ"] },
          right: { type: "press", keys: ["ControlLeft", "KeyY"] },
        },
      },
    ],
    device: {
      name: "MX Master 4 for Mac",
      productId: "B042",
      firmware: "RBM 27.03 build 0019",
      battery: { percent: 65, charging: false },
      dpi: { current: 800, default: 1000, min: 200, max: 8000, step: 50 },
      smartShift: { ratchet: true, autoDisengage: 12, torque: 75, tunableTorque: true, defaultAutoDisengage: 10, defaultTorque: 75 },
      haptics: {
        enabled: true,
        level: 60,
        waveforms: ["SharpStateChange", "DampStateChange", "SharpCollision", "Completed", "Knock", "Jingle"],
      },
      forceButton: { current: 3913, default: 4891, min: 3913, max: 6505 },
      buttons: [
        { id: "middle", name: "Middle", cid: 0x52, divertedElsewhere: false },
        { id: "back", name: "Back", cid: 0x53, divertedElsewhere: true },
        { id: "forward", name: "Forward", cid: 0x56, divertedElsewhere: true },
        { id: "gesture", name: "Gesture", cid: 0xc3, divertedElsewhere: true },
        { id: "smartshift", name: "Smart Shift", cid: 0xc4, divertedElsewhere: true },
        { id: "haptic", name: "Haptic", cid: 0x1a0, divertedElsewhere: true },
      ],
      thumbWheel: { divertedElsewhere: true },
      hosts: {
        current: 0,
        canRename: true,
        maxNameLength: 24,
        channels: [
          { index: 0, paired: true, name: "DESKTOP-AJ" },
          { index: 1, paired: true, name: "MacBook Pro" },
          { index: 2, paired: false, name: "" },
        ],
      },
    },
  };
}

/**
 * Applies a request to a state the way the agent would. It's the whole agent in the preview, and in the app it
 * shows a change before the agent has answered.
 */
export function previewCall(state: AgentState, method: string, params: Record<string, unknown>): AgentState {
  const device = state.device!;
  switch (method) {
    case "setBindings":
      return { ...state, bindings: params.bindings as AgentState["bindings"] };
    case "setStartAtLogin":
      return { ...state, startAtLogin: params.enabled as boolean };
    case "setDpi":
      return { ...state, device: { ...device, dpi: { ...device.dpi, current: params.dpi as number } } };
    case "setSmartShift":
      return { ...state, device: { ...device, smartShift: { ...device.smartShift, ...params } } };
    case "setHapticLevel": {
      const level = params.level as number;
      return { ...state, device: { ...device, haptics: { ...device.haptics, enabled: level > 0, level: level || device.haptics.level } } };
    }
    case "setThumbWheel":
      return { ...state, thumbWheel: params.thumbWheel as AgentState["thumbWheel"] };
    case "switchProfile":
      // The sample keeps one set of bindings; switching only changes which profile is named.
      return { ...state, activeProfile: params.id as string };
    case "createProfile": {
      const id = Math.random().toString(36).slice(2, 10);
      return { ...state, profiles: [...state.profiles, { id, name: params.name as string }], activeProfile: id };
    }
    case "renameProfile":
      return { ...state, profiles: state.profiles.map((p) => (p.id === params.id ? { ...p, name: params.name as string } : p)) };
    case "deleteProfile": {
      if (state.profiles.length === 1) throw new Error("The last profile can't be deleted.");
      const profiles = state.profiles.filter((p) => p.id !== params.id);
      return { ...state, profiles, activeProfile: state.activeProfile === params.id ? profiles[0]!.id : state.activeProfile };
    }
    case "setHostName": {
      const hosts = device.hosts!;
      const channels = hosts.channels.map((c) => (c.index === hosts.current ? { ...c, name: params.name as string } : c));
      return { ...state, device: { ...device, hosts: { ...hosts, channels } } };
    }
    case "setForce":
      return { ...state, device: { ...device, forceButton: { ...device.forceButton!, current: params.force as number } } };
    default:
      return state;
  }
}
