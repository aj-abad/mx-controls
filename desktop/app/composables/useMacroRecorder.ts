import type { MacroStep } from "~/composables/useAgent";
import type { RecordedKey } from "~/utils/macroRecording";

/** Owns one capture session. Pause releases the keyboard; all finish paths clean up the same way. */
export function useMacroRecorder(finish: (steps: MacroStep[]) => void, budget: () => number) {
  const { captureKeys } = useAgent();
  const active = ref(false);
  const paused = ref(false);
  const keepPauses = ref(false);
  const keys = ref<RecordedKey[]>([]);
  const recorded = computed(() => stepsFrom(keys.value, keepPauses.value, Math.max(0, budget())));
  let release: (() => void) | null = null;
  let capture: AbortController | null = null;
  let session = 0;
  let pauseStart = 0;
  let pauseTime = 0;
  const held = new Set<string>();

  function detach() {
    session++;
    window.removeEventListener("keydown", onKey, true);
    window.removeEventListener("keyup", onKey, true);
    capture?.abort();
    capture = null;
    release?.();
    release = null;
  }
  function hear(code: string | null, down: boolean) {
    if (!active.value || paused.value || !code || code === "Unidentified") return;
    if (code === "Escape" || code === "F10") {
      if (down) stop(true);
      return;
    }
    if (code === "F9") {
      if (down) pause();
      return;
    }
    if (down && held.has(code)) return;
    if (down) held.add(code);
    else held.delete(code);
    keys.value = [...keys.value, { code, down, at: performance.now() - pauseTime }];
  }
  function onKey(event: KeyboardEvent) {
    event.preventDefault();
    event.stopImmediatePropagation();
    if (release || event.repeat) return;
    if (event.type === "keydown") {
      for (const [flag, code] of FLAGS) {
        const matching = [...held].filter((k) => k.startsWith(code.slice(0, -4)));
        if (event[flag] && !matching.length) hear(code, true);
        else if (!event[flag] && event.code !== code) matching.forEach((k) => hear(k, false));
      }
    }
    hear(event.code, event.type === "keydown");
  }
  async function attach() {
    window.addEventListener("keydown", onKey, true);
    window.addEventListener("keyup", onKey, true);
    (document.activeElement as HTMLElement | null)?.blur();
    const mine = ++session;
    capture = new AbortController();
    const handBack = await captureKeys({
      key: ({ code, down }) => hear(code, down),
      ended: () => stop(true),
    }, capture.signal);
    if (mine === session && active.value && !paused.value) release = handBack;
    else handBack?.();
  }
  function start() {
    if (active.value) return;
    active.value = true;
    paused.value = false;
    keys.value = [];
    held.clear();
    pauseTime = 0;
    void attach();
  }
  function pause() {
    if (!active.value || paused.value) return;
    for (const code of [...held]) hear(code, false);
    paused.value = true;
    pauseStart = performance.now();
    detach();
  }
  function resume() {
    if (!active.value || !paused.value) return;
    pauseTime += performance.now() - pauseStart;
    paused.value = false;
    void attach();
  }
  function stop(keep: boolean) {
    if (!active.value) return;
    if (!paused.value) for (const code of [...held]) hear(code, false);
    const steps = keep ? cloneMacro(recorded.value.steps) : [];
    active.value = false;
    paused.value = false;
    held.clear();
    detach();
    if (steps.length) finish(steps);
    keys.value = [];
  }
  onBeforeUnmount(() => stop(false));
  return { active, paused, keepPauses, recorded, start, pause, resume, stop };
}

const FLAGS = [
  ["ctrlKey", "ControlLeft"],
  ["shiftKey", "ShiftLeft"],
  ["altKey", "AltLeft"],
  ["metaKey", "MetaLeft"],
] as const;
