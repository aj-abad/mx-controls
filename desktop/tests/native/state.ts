import type { AgentState } from "../../app/composables/useAgent";

/** Independent protocol fixture. No real device, stored settings, user names, or OS integration. */
export function initialState(): AgentState {
  return {
    connected: true, optionsPlusRunning: false, error: null, revision: 0,
    startAtLogin: false, multiClickMs: 300, hostShortcuts: [],
    profiles: [
      {
        id: "default", name: "Default", icon: null, programs: [],
        bindings: [{ button: "back", mode: "action", action: { type: "macro", macro: "snapleft" } }],
        thumbWheel: { mode: "scroll", speed: 100, inverted: false },
        layer: { bindings: [], thumbWheel: null },
      },
      {
        id: "work", name: "Work", icon: "briefcase", programs: [], bindings: [],
        thumbWheel: { mode: "scroll", speed: 100, inverted: false },
        layer: { bindings: [], thumbWheel: null },
      },
    ],
    activeProfile: "default",
    selectedProfile: "default",
    layerActive: false,
    followsPrograms: false,
    systemActions: ["taskView", "showDesktop", "zoomIn", "zoomOut"],
    opensApps: false,
    runsShortcuts: false,
    bindings: [{ button: "back", mode: "action", action: { type: "macro", macro: "snapleft" } }],
    thumbWheel: { mode: "scroll", speed: 100, inverted: false },
    builtInPresets: [], presetUses: [], roseMenus: [], roseUses: [], baseProfile: "default", macroUses: [{ macro: "snapleft", profile: "default", button: "back" }],
    gesturePresets: [{
      id: "tiling", name: "Window tiling",
      up: { type: "press", keys: ["MetaLeft", "ArrowUp"] },
      down: { type: "press", keys: ["MetaLeft", "ArrowDown"] },
      left: { type: "press", keys: ["MetaLeft", "ArrowLeft"] },
      right: { type: "press", keys: ["MetaLeft", "ArrowRight"] },
    }],
    macros: [
      { id: "snapleft", name: "Snap left", steps: [{ type: "keys", keys: ["MetaLeft", "ArrowLeft"] }] },
      { id: "shot", name: "Screenshot", steps: [{ type: "keys", keys: ["MetaLeft", "ShiftLeft", "KeyS"] }] },
      { id: "signoff", name: "Sign off", steps: [{ type: "keys", keys: ["ControlLeft", "End"] }, { type: "wait", ms: 50 }, { type: "text", text: "Best,\nTest" }] },
    ],
    device: {
      name: "MX Master 4 (simulated)", productId: "B042", firmware: "UI test fixture",
      battery: { percent: 65, charging: false },
      dpi: { current: 800, default: 1000, min: 200, max: 8000, step: 50 },
      scrollInverted: false,
      smartShift: { ratchet: true, autoDisengage: 12, torque: 75, tunableTorque: true, defaultAutoDisengage: 10, defaultTorque: 75 },
      haptics: { enabled: true, level: 60, waveforms: ["SharpStateChange", "Completed"] },
      forceButton: { current: 3913, default: 4891, min: 3913, max: 6505 },
      buttons: ["middle", "back", "forward", "gesture", "smartshift", "haptic"].map((id, cid) => ({ id, name: id, cid, divertedElsewhere: false })),
      thumbWheel: { divertedElsewhere: false },
      hosts: { current: 0, canRename: true, maxNameLength: 24, channels: [{ index: 0, paired: true, name: "Test host" }] },
    },
  };
}
