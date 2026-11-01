import type { AgentState, ButtonAction, ButtonBinding, GesturePreset, GestureStep, Macro, MacroUse } from "~/composables/useAgent";

const step = (id: string, more: Partial<GestureStep> = {}): GestureStep => ({ ...(presetById(id)!.action as ButtonAction), ...more });
const NONE: GestureStep = { type: "none" };

/** Build lazily: actions describe macros through useAgent, which also imports this preview. */
const builtInPresets = (): GesturePreset[] => [
  { id: "desktops", name: "Desktops", up: step("taskView"), down: step("showDesktop"), left: step("desktopLeft"), right: step("desktopRight"), spacingMm: 6 },
  { id: "browser", name: "Browser", up: step("newTab"), down: step("closeTab"), left: step("back"), right: step("forward"), spacingMm: 6 },
  {
    id: "media",
    name: "Media",
    up: step("volumeUp", { repeat: true }),
    down: step("volumeDown", { repeat: true }),
    left: step("previousTrack"),
    right: step("nextTrack"),
    spacingMm: 6,
  },
];

const usesOf = (profile: string, bindings: ButtonBinding[]) =>
  bindings.filter((b) => b.mode === "gesture" && b.preset).map((b) => ({ preset: b.preset!, profile, button: b.button }));

const MACROS: Macro[] = [
  { id: "snapleft", name: "Snap left", steps: [{ type: "keys", keys: ["MetaLeft", "ArrowLeft"] }] },
  { id: "shot", name: "Screenshot", steps: [{ type: "keys", keys: ["MetaLeft", "ShiftLeft", "KeyS"] }] },
  {
    id: "signoff",
    name: "Sign off",
    steps: [
      { type: "keys", keys: ["ControlLeft", "End"] },
      { type: "wait", ms: 50 },
      { type: "text", text: "Best,\nAJ" },
    ],
  },
];

/** Where macros run, worked out again as the agent does, keeping the sample's other profiles as they were. */
function withMacroUses(state: AgentState): AgentState {
  const ran = (actions: (ButtonAction | null | undefined)[]) =>
    actions.filter((a): a is ButtonAction => a?.type === "macro" && !!a.macro).map((a) => a.macro!);
  const steps = (p: GesturePreset) => pathRows(p).map((row) => actionOf(row.step));
  const kept = state.macroUses.filter((use) => use.profile && use.profile !== state.activeProfile);
  const uses: MacroUse[] = [
    ...kept,
    ...state.bindings.flatMap((b) => ran([b.action, b.doubleClick, b.tripleClick]).map((macro) => ({ macro, profile: state.activeProfile, button: b.button }))),
    ...ran([state.thumbWheel.left, state.thumbWheel.right]).map((macro) => ({ macro, profile: state.activeProfile, button: "thumbwheel" })),
    ...state.gesturePresets.flatMap((p) => ran(steps(p)).map((macro) => ({ macro, preset: p.id }))),
  ];
  const seen = new Set<string>();
  return { ...state, macroUses: uses.filter((u) => !seen.has(JSON.stringify(u)) && !!seen.add(JSON.stringify(u))) };
}

/** Sample state for previewing the app in a browser, outside Tauri. It never talks to a mouse. */
export function previewState(): AgentState {
  const builtIn = builtInPresets();
  const bindings: ButtonBinding[] = [
    { button: "middle", mode: "action", action: { type: "hold", keys: ["ShiftLeft"] } },
    { button: "back", mode: "action", action: { type: "macro", macro: "snapleft" } },
    { button: "gesture", mode: "gesture", action: { type: "press", keys: ["ControlLeft", "KeyC"] }, preset: "tiling" },
  ];
  return withMacroUses({
    connected: true,
    optionsPlusRunning: true,
    error: null,
    startAtLogin: true,
    revision: 0,
    multiClickMs: 300,
    builtInPresets: builtIn,
    gesturePresets: [
      ...builtIn,
      {
        id: "tiling",
        name: "Window tiling",
        up: { type: "press", keys: ["MetaLeft", "ArrowUp"] },
        down: { type: "press", keys: ["MetaLeft", "ArrowDown"] },
        left: { ...NONE, left: step("desktopLeft") },
        right: { ...NONE, right: { ...NONE, up: { type: "press", keys: ["MetaLeft", "ArrowRight"] }, down: step("desktopRight") } },
        spacingMm: 6,
      },
    ],
    presetUses: [...usesOf("default", bindings), { preset: "desktops", profile: "games", button: "gesture" }],
    macros: MACROS,
    macroUses: [{ macro: "shot", profile: "work", button: "smartshift" }],
    bindings,
    thumbWheel: { mode: "scroll", speed: 100, inverted: false },
    profiles: [
      { id: "default", name: "Default", icon: null },
      { id: "work", name: "Work", icon: "briefcase" },
      { id: "games", name: "Games", icon: "game-controller" },
    ],
    activeProfile: "default",
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
  });
}

/**
 * Applies a request to a state the way the agent would. It's the whole agent in the preview, and in the app it
 * shows a change before the agent has answered.
 */
export function previewCall(state: AgentState, method: string, params: Record<string, unknown>): AgentState {
  const next = apply(state, method, params);
  return MACRO_USERS.has(method) ? withMacroUses(next) : next;
}

const MACRO_USERS = new Set(["setBindings", "setThumbWheel", "setGesturePreset", "deleteGesturePreset", "setMacro", "deleteMacro"]);

function apply(state: AgentState, method: string, params: Record<string, unknown>): AgentState {
  const device = state.device!;
  switch (method) {
    case "setBindings": {
      const bindings = params.bindings as ButtonBinding[];
      const others = state.presetUses.filter((use) => use.profile !== state.activeProfile);
      return { ...state, bindings, presetUses: [...others, ...usesOf(state.activeProfile, bindings)] };
    }
    case "setGesturePreset": {
      const preset = params.preset as GesturePreset;
      const known = state.gesturePresets.some((p) => p.id === preset.id);
      const gesturePresets = known ? state.gesturePresets.map((p) => (p.id === preset.id ? preset : p)) : [...state.gesturePresets, preset];
      return { ...state, gesturePresets };
    }
    case "setMacro": {
      const macro = params.macro as Macro;
      const known = state.macros.some((m) => m.id === macro.id);
      return { ...state, macros: known ? state.macros.map((m) => (m.id === macro.id ? macro : m)) : [...state.macros, macro] };
    }
    case "deleteMacro":
      return { ...state, macros: state.macros.filter((m) => m.id !== params.id) };
    case "deleteGesturePreset": {
      const builtIn = state.builtInPresets.find((p) => p.id === params.id);
      const gesturePresets = builtIn
        ? state.gesturePresets.map((p) => (p.id === builtIn.id ? builtIn : p))
        : state.gesturePresets.filter((p) => p.id !== params.id);
      return { ...state, gesturePresets };
    }
    case "setStartAtLogin":
      return { ...state, startAtLogin: params.enabled as boolean };
    case "setMultiClickMs":
      return { ...state, multiClickMs: params.ms as number };
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
      const source = state.profiles.find((p) => p.id === params.copyFrom);
      const icon = "icon" in params ? (params.icon as string | null) : (source?.icon ?? null);
      return { ...state, profiles: [...state.profiles, { id, name: params.name as string, icon }], activeProfile: id };
    }
    case "setProfileIcon":
      return { ...state, profiles: state.profiles.map((p) => (p.id === params.id ? { ...p, icon: params.icon as string | null } : p)) };
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
