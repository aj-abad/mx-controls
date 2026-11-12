import type {
  AgentState,
  AppInfo,
  ShortcutInfo,
  ButtonAction,
  ButtonBinding,
  GesturePreset,
  Macro,
  MacroUse,
  ProfileSummary,
  ProgramInfo,
  RoseItem,
  RoseMenu,
  ThumbWheelSettings,
} from "~/composables/useAgent";

const step = (id: string, more: Partial<ButtonAction> = {}): ButtonAction => ({ ...presetById(id)!.action, ...more });
const IS_MAC = navigator.userAgent.includes("Mac");

/** Build lazily: actions describe macros through useAgent, which also imports this preview. */
const builtInPresets = (): GesturePreset[] => [
  { id: "desktops", name: "Desktops", up: step("taskView"), down: step("showDesktop"), left: step("desktopLeft"), right: step("desktopRight") },
  { id: "browser", name: "Browser", up: step("newTab"), down: step("closeTab"), left: step("back"), right: step("forward") },
  {
    id: "media",
    name: "Media",
    up: step("volumeUp", { repeat: true }),
    down: step("volumeDown", { repeat: true }),
    left: step("previousTrack"),
    right: step("nextTrack"),
  },
];

const usesOf = (profile: string, bindings: ButtonBinding[], layer = false) =>
  bindings
    .filter((b) => b.mode === "gesture" && b.preset)
    .map((b) => ({ preset: b.preset!, profile, button: b.button, ...(layer && { layer }) }));

const presetUsesOf = (profiles: ProfileSummary[]) =>
  profiles.flatMap((p) => [...usesOf(p.id, p.bindings), ...usesOf(p.id, p.layer.bindings, true)]);

const roseUsesOf = (profiles: ProfileSummary[]) =>
  profiles.flatMap((p) =>
    [...p.bindings.map((b) => ({ b, layer: false })), ...p.layer.bindings.map((b) => ({ b, layer: true }))]
      .filter(({ b }) => b.mode === "rose" && b.menu)
      .map(({ b, layer }) => ({ menu: b.menu!, profile: p.id, button: b.button, ...(layer && { layer }) })),
  );

/** Every action a rose's options and folders run, leaving out pass-through options. */
export const roseActions = (items: RoseItem[]): ButtonAction[] =>
  items.flatMap((item) => (item.passThrough ? [] : item.items ? roseActions(item.items) : [item.action]));

const SCROLL: ThumbWheelSettings = { mode: "scroll", speed: 100, inverted: false };

const profile = (id: string, name: string, icon: string | null, more: Partial<ProfileSummary> = {}): ProfileSummary => ({
  id,
  name,
  icon,
  programs: [],
  bindings: [],
  thumbWheel: SCROLL,
  layer: { bindings: [], thumbWheel: null },
  ...more,
});

/** The apps the preview pretends are installed. */
export const previewApps = (): AppInfo[] => [
  { id: "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", name: "Calculator" },
  { id: "Chrome", name: "Google Chrome" },
  { id: "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", name: "Notepad" },
  { id: "C:\\Users\\Me\\AppData\\Roaming\\Spotify\\Spotify.exe", name: "Spotify" },
  { id: "Microsoft.VisualStudioCode", name: "Visual Studio Code" },
];

/** The Shortcuts the preview pretends the Shortcuts app has. */
export const previewShortcuts = (): ShortcutInfo[] => [
  { id: "6A1F0C2E-0000-4000-8000-000000000001", name: "Start focus timer" },
  { id: "6A1F0C2E-0000-4000-8000-000000000002", name: "Resize image" },
  { id: "6A1F0C2E-0000-4000-8000-000000000003", name: "Text last image to me" },
];

/** Stand-ins for programs' icons: a coloured tile with the program's initial, as the agent can't be asked. */
export function previewProgramIcons(ids: string[]): Record<string, string | null> {
  const colours = ["#4285f4", "#1db954", "#e34f26", "#7c3aed", "#f59e0b", "#0ea5e9"];
  return Object.fromEntries(
    ids.map((id, i) => {
      const letter = (id.replace(/^.*[\/]/, "")[0] ?? "?").toUpperCase();
      const colour = colours[[...id].reduce((sum, c) => sum + c.charCodeAt(0), i) % colours.length];
      const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48"><rect width="48" height="48" rx="11" fill="${colour}"/><text x="24" y="32" font-family="sans-serif" font-size="24" font-weight="700" fill="white" text-anchor="middle">${letter}</text></svg>`;
      return [id, `data:image/svg+xml,${encodeURIComponent(svg)}`];
    }),
  );
}

/** The programs the preview pretends are open. */
export const previewPrograms = (): ProgramInfo[] => [
  { id: "chrome.exe", name: "Google Chrome" },
  { id: "code.exe", name: "Visual Studio Code" },
  { id: "photoshop.exe", name: "Adobe Photoshop" },
  { id: "spotify.exe", name: "Spotify" },
];

/** The running profile's main layer, as the agent mirrors it at the top of the state. */
function withRunning(state: AgentState): AgentState {
  const running = state.profiles.find((p) => p.id === state.activeProfile) ?? state.profiles[0]!;
  return { ...state, bindings: running.bindings, thumbWheel: running.thumbWheel };
}

/** The profile a `setBindings` or `setThumbWheel` changes: the one named, or the one running. */
const editedProfile = (state: AgentState, params: Record<string, unknown>) => (params.profile as string | undefined) ?? state.activeProfile;

function editProfile(state: AgentState, id: string, change: (p: ProfileSummary) => ProfileSummary): AgentState {
  if (!state.profiles.some((p) => p.id === id)) throw new Error("That profile was deleted.");
  const profiles = state.profiles.map((p) => (p.id === id ? change(p) : p));
  return withRunning({ ...state, profiles, presetUses: presetUsesOf(profiles), roseUses: roseUsesOf(profiles) });
}

const switchable = (p: ProfileSummary) => p.programs.length === 0;

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
  const steps = (p: GesturePreset) => DIRECTIONS.map((d) => p[d]);
  const inLayer = (profile: string, bindings: ButtonBinding[], wheel: ThumbWheelSettings | null, layer: boolean): MacroUse[] => [
    ...bindings.flatMap((b) =>
      ran([b.action, b.doubleClick, b.tripleClick]).map((macro) => ({ macro, profile, button: b.button, ...(layer && { layer }) })),
    ),
    ...ran([wheel?.left, wheel?.right]).map((macro) => ({ macro, profile, button: "thumbwheel", ...(layer && { layer }) })),
  ];
  const uses: MacroUse[] = [
    ...state.profiles.flatMap((p) => [
      ...inLayer(p.id, p.bindings, p.thumbWheel, false),
      ...inLayer(p.id, p.layer.bindings, p.layer.thumbWheel, true),
    ]),
    ...state.gesturePresets.flatMap((p) => ran(steps(p)).map((macro) => ({ macro, preset: p.id }))),
    ...state.roseMenus.flatMap((m) => ran(roseActions(m.items)).map((macro) => ({ macro, rose: m.id }))),
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
    { button: "haptic", mode: "action", action: { type: "layer", layer: "momentary" } },
    { button: "smartshift", mode: "rose", action: { type: "none" }, menu: "tools" },
  ];
  const option = (id: string, more: Partial<RoseItem> = {}): RoseItem => ({ action: step(id), ...more });
  const roseMenus: RoseMenu[] = [
    {
      id: "tools",
      name: "Quick tools",
      items: [
        option("copy", { icon: "copy", iconPath: roseIconPath("copy") }),
        option("paste", { icon: "clipboard-text", iconPath: roseIconPath("clipboard-text") }),
        {
          action: { type: "none" },
          label: "Windows",
          icon: "squares-four",
          iconPath: roseIconPath("squares-four"),
          items: [
            option("taskView"),
            option("showDesktop"),
            { action: { type: "none" }, label: "Desktops", items: [option("desktopLeft"), option("desktopRight")] },
            option("minimizeWindow"),
          ],
        },
        option("screenshot"),
        option("undo"),
        { action: { type: "macro", macro: "signoff" } },
      ],
    },
  ];
  const profiles = [
    profile("default", "Default", null, {
      bindings,
      layer: {
        bindings: [
          { button: "back", mode: "action", action: { type: "press", keys: ["ControlLeft", "KeyZ"] } },
          { button: "forward", mode: "action", action: { type: "press", keys: ["ControlLeft", "KeyY"] } },
        ],
        thumbWheel: { mode: "actions", speed: 100, inverted: false, left: { type: "system", system: "zoomOut" }, right: { type: "system", system: "zoomIn" } },
      },
    }),
    profile("work", "Work", "briefcase", {
      bindings: [
        { button: "smartshift", mode: "action", action: { type: "macro", macro: "shot" } },
        { button: "back", mode: "inherit" },
      ],
    }),
    profile("games", "Games", "game-controller", {
      bindings: [{ button: "gesture", mode: "gesture", action: { type: "none" }, preset: "desktops" }],
    }),
    profile("code", "Code", "terminal-window", {
      programs: ["code.exe", "devenv.exe"],
      bindings: [{ button: "forward", mode: "action", action: { type: "profile", profile: "work" } }],
    }),
  ];
  return withMacroUses({
    connected: true,
    optionsPlusRunning: true,
    error: null,
    startAtLogin: true,
    revision: 0,
    multiClickMs: 300,
    hostShortcuts: [],
    builtInPresets: builtIn,
    gesturePresets: [
      ...builtIn,
      {
        id: "tiling",
        name: "Window tiling",
        up: { type: "press", keys: ["MetaLeft", "ArrowUp"] },
        down: { type: "press", keys: ["MetaLeft", "ArrowDown"] },
        left: { type: "press", keys: ["MetaLeft", "ArrowLeft"] },
        right: { type: "press", keys: ["MetaLeft", "ArrowRight"] },
      },
    ],
    presetUses: presetUsesOf(profiles),
    roseMenus,
    roseUses: roseUsesOf(profiles),
    baseProfile: "default",
    macros: MACROS,
    macroUses: [],
    bindings,
    thumbWheel: SCROLL,
    profiles,
    activeProfile: "default",
    selectedProfile: "default",
    layerActive: false,
    followsPrograms: true,
    systemActions: IS_MAC
      ? ["taskView", "appExpose", "showDesktop", "launchpad", "switchApps", "zoomIn", "zoomOut", "search", "screenshot", "emoji",
        "lockScreen", "fileManager", "settings", "calculator", "minimizeWindow", "fullScreen", "closeWindow", "quitApp",
        "brightnessUp", "brightnessDown", "lookUp"]
      : ["taskView", "showDesktop", "switchApps", "zoomIn", "zoomOut", "search", "screenshot", "emoji", "dictation", "notifications",
        "lockScreen", "fileManager", "settings", "calculator", "minimizeWindow", "maximizeWindow", "fullScreen", "closeWindow", "micMute"],
    opensApps: true,
    runsShortcuts: IS_MAC,
    device: {
      name: "MX Master 4 for Mac",
      productId: "B042",
      firmware: "RBM 27.03 build 0019",
      battery: { percent: 65, charging: false },
      dpi: { current: 800, default: 1000, min: 200, max: 8000, step: 50 },
      scrollInverted: false,
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

const MACRO_USERS = new Set([
  "setBindings",
  "setThumbWheel",
  "setGesturePreset",
  "deleteGesturePreset",
  "setRoseMenu",
  "deleteRoseMenu",
  "setMacro",
  "deleteMacro",
  "createProfile",
  "deleteProfile",
]);

function apply(state: AgentState, method: string, params: Record<string, unknown>): AgentState {
  const device = state.device!;
  switch (method) {
    case "setBindings": {
      const bindings = params.bindings as ButtonBinding[];
      return editProfile(state, editedProfile(state, params), (p) =>
        params.layer ? { ...p, layer: { ...p.layer, bindings } } : { ...p, bindings },
      );
    }
    case "setThumbWheel": {
      const wheel = params.thumbWheel as ThumbWheelSettings | null;
      return editProfile(state, editedProfile(state, params), (p) =>
        params.layer ? { ...p, layer: { ...p.layer, thumbWheel: wheel } } : { ...p, thumbWheel: wheel ?? SCROLL },
      );
    }
    case "setProfilePrograms": {
      const programs = [...new Set((params.programs as string[]).map((p) => p.trim().toLowerCase()).filter(Boolean))];
      for (const program of programs) {
        const other = state.profiles.find((p) => p.id !== params.id && p.programs.includes(program));
        if (other) throw new Error(`${program} already runs ${other.name}.`);
      }
      const next = editProfile(state, params.id as string, (p) => ({ ...p, programs }));
      if (!next.profiles.some(switchable)) throw new Error("Keep at least one profile without programs; it runs for every other program.");
      const chosen = next.profiles.find((p) => p.id === next.selectedProfile && switchable(p)) ?? next.profiles.find(switchable)!;
      return withRunning({ ...next, selectedProfile: chosen.id, activeProfile: chosen.id });
    }
    case "setGesturePreset": {
      const preset = params.preset as GesturePreset;
      const known = state.gesturePresets.some((p) => p.id === preset.id);
      const gesturePresets = known ? state.gesturePresets.map((p) => (p.id === preset.id ? preset : p)) : [...state.gesturePresets, preset];
      return { ...state, gesturePresets };
    }
    case "setRoseMenu": {
      const menu = params.menu as RoseMenu;
      const known = state.roseMenus.some((m) => m.id === menu.id);
      return { ...state, roseMenus: known ? state.roseMenus.map((m) => (m.id === menu.id ? menu : m)) : [...state.roseMenus, menu] };
    }
    case "deleteRoseMenu":
      return { ...state, roseMenus: state.roseMenus.filter((m) => m.id !== params.id) };
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
    case "setHostShortcut": {
      const others = state.hostShortcuts.filter((s) => s.host !== params.host);
      const keys = params.keys as string[] | null;
      return { ...state, hostShortcuts: keys ? [...others, { host: params.host as number, keys }] : others };
    }
    case "setDpi":
      return { ...state, device: { ...device, dpi: { ...device.dpi, current: params.dpi as number } } };
    case "setScrollInverted":
      return { ...state, device: { ...device, scrollInverted: params.inverted as boolean } };
    case "setSmartShift":
      return { ...state, device: { ...device, smartShift: { ...device.smartShift, ...params } } };
    case "setHapticLevel": {
      const level = params.level as number;
      return { ...state, device: { ...device, haptics: { ...device.haptics, enabled: level > 0, level: level || device.haptics.level } } };
    }
    case "switchProfile": {
      const target = state.profiles.find((p) => p.id === params.id);
      if (!target) throw new Error("That profile was deleted.");
      if (!switchable(target)) throw new Error(`${target.name} runs only while its programs are in front, so it can't be chosen.`);
      // The preview has no program in front, so the chosen profile is the one running.
      return withRunning({ ...state, selectedProfile: target.id, activeProfile: target.id, layerActive: false });
    }
    case "createProfile": {
      const id = Math.random().toString(36).slice(2, 10);
      const source = state.profiles.find((p) => p.id === params.copyFrom);
      const icon = "icon" in params ? (params.icon as string | null) : (source?.icon ?? null);
      const created = source
        ? { ...structuredClone(source), id, name: params.name as string, icon, programs: [] }
        : profile(id, params.name as string, icon);
      const profiles = [...state.profiles, created];
      return withRunning({ ...state, profiles, presetUses: presetUsesOf(profiles), roseUses: roseUsesOf(profiles), selectedProfile: id, activeProfile: id });
    }
    case "setProfileIcon":
      return { ...state, profiles: state.profiles.map((p) => (p.id === params.id ? { ...p, icon: params.icon as string | null } : p)) };
    case "renameProfile":
      return { ...state, profiles: state.profiles.map((p) => (p.id === params.id ? { ...p, name: params.name as string } : p)) };
    case "deleteProfile": {
      if (state.profiles.length === 1) throw new Error("The last profile can't be deleted.");
      const profiles = state.profiles.filter((p) => p.id !== params.id);
      if (!profiles.some(switchable)) throw new Error("Keep at least one profile without programs; it runs for every other program.");
      const chosen = state.selectedProfile === params.id ? profiles.find(switchable)!.id : state.selectedProfile;
      const running = state.activeProfile === params.id ? chosen : state.activeProfile;
      const baseProfile = profiles.some((p) => p.id === "default") ? "default" : profiles[0]!.id;
      return withRunning({
        ...state,
        profiles,
        baseProfile,
        presetUses: presetUsesOf(profiles),
        roseUses: roseUsesOf(profiles),
        selectedProfile: chosen,
        activeProfile: running,
      });
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
