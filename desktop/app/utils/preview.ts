import type { AgentState } from "~/composables/useAgent";

/** Sample state for previewing the app in a browser, outside Tauri. It never talks to a mouse. */
export function previewState(): AgentState {
  return {
    connected: true,
    optionsPlusRunning: true,
    error: null,
    mappings: [{ button: "middle", key: "ShiftLeft" }],
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
    },
  };
}

/** Applies a request to the sample state the way the agent would. */
export function previewCall(state: AgentState, method: string, params: Record<string, unknown>): AgentState {
  const device = state.device!;
  switch (method) {
    case "setMappings":
      return { ...state, mappings: params.mappings as AgentState["mappings"] };
    case "setDpi":
      return { ...state, device: { ...device, dpi: { ...device.dpi, current: params.dpi as number } } };
    case "setSmartShift":
      return { ...state, device: { ...device, smartShift: { ...device.smartShift, ...params } } };
    case "setHapticLevel": {
      const level = params.level as number;
      return { ...state, device: { ...device, haptics: { ...device.haptics, enabled: level > 0, level: level || device.haptics.level } } };
    }
    case "setForce":
      return { ...state, device: { ...device, forceButton: { ...device.forceButton!, current: params.force as number } } };
    default:
      return state;
  }
}
