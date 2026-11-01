import {
  PhArrowDown,
  PhArrowUp,
  PhCopy,
  PhPencilSimple,
  PhPlus,
  PhScissors,
  PhTrash,
  PhClipboardText,
} from "@phosphor-icons/vue";
import type { MacroStep } from "~/composables/useAgent";
import type { MenuEntry, MenuAction } from "~/utils/menu";

export function useMacroWorkspace() {
  const editor = useMacroEditor();
  const { pipeConnected, device } = useAgent();
  const {
    library,
    documents,
    currentId,
    document,
    draft,
    selection,
    selectedIndex,
    problem,
    status,
    commit,
    undo,
    redo,
    select,
    all,
    insert,
    remove,
    update,
    move,
    nudge,
    duplicateSteps,
    create,
  } = editor;
  const { openMacro: openId } = usePage();
  const globalHistory = useUndoState();
  const macroHistory = (label: string) =>
    ["Create macro", "Delete macro", "Edit macro"].includes(label) ? label : "";
  const undoLabel = computed(() =>
    globalHistory.undoLabel.value === "Delete macro"
      ? "Delete macro"
      : (document.value?.past.at(-1)?.label ?? macroHistory(globalHistory.undoLabel.value)),
  );
  const redoLabel = computed(() =>
    globalHistory.redoLabel.value === "Delete macro"
      ? "Delete macro"
      : (document.value?.future.at(-1)?.label ?? macroHistory(globalHistory.redoLabel.value)),
  );
  async function undoEdit() {
    if (globalHistory.undoLabel.value === "Delete macro" || !document.value?.past.length) {
      if (macroHistory(globalHistory.undoLabel.value)) {
        await undoLast();
        reconcile();
      }
    } else undo();
  }
  async function redoEdit() {
    if (globalHistory.redoLabel.value === "Delete macro") {
      await redoLast();
      reconcile();
    } else if (document.value?.future.length) redo();
    else if (macroHistory(globalHistory.redoLabel.value)) {
      await redoLast();
      reconcile();
    }
  }
  function reconcile() {
    const id = currentId.value;
    if (id && !library.find(id) && documents.get(id)?.saved) {
      documents.delete(id);
      openId.value = null;
    }
  }
  const inspector = useTemplateRef<{ focus: () => void }>("inspector");
  const nameField = useTemplateRef<HTMLInputElement>("nameField");

  const moreOpen = ref(false);
  const confirming = ref(false);
  const notice = ref("");
  let clearNotice: ReturnType<typeof setTimeout> | undefined;
  function say(message: string) {
    notice.value = message;
    clearTimeout(clearNotice);
    clearNotice = setTimeout(() => (notice.value = ""), 3000);
  }
  const usage = computed(() => {
    const uses = currentId.value ? library.usedBy(currentId.value) : [];
    return uses.length ? `Used by ${uses.join(", ")}` : "Not assigned yet";
  });
  const list = computed(() => {
    const ids = new Set(library.macros.value.map((m) => m.id));
    for (const [id, doc] of documents) if (!doc.saved) ids.add(id);
    return [...ids].map((id) => {
      const doc = documents.get(id)!;
      const m = doc.macro;
      return {
        id,
        name: m.name,
        draft: canonical(m) !== doc.saved,
        summary:
          m.steps.length === 1
            ? macroStepText(m.steps[0]!, keyLabel)
            : `${m.steps.length} steps · ${macroMs(m)} ms`,
      };
    });
  });
  let recordAt = 0;
  const recorder = useMacroRecorder(
    (steps) => insert(steps, recordAt, "Record keys"),
    () => MACRO_WAIT_LIMIT - (draft.value ? macroMs(draft.value) : 0),
  );
  const { active: recording, paused, keepPauses, recorded } = recorder;
  const shownSteps = computed(() =>
    !draft.value
      ? []
      : recording.value
        ? [
            ...draft.value.steps.slice(0, recordAt),
            ...recorded.value.steps,
            ...draft.value.steps.slice(recordAt),
          ]
        : draft.value.steps,
  );
  const feedback = computed(() =>
    recording.value
      ? `${paused.value ? "Recording paused" : "Recording keys"} · ${recorded.value.steps.length} ${recorded.value.steps.length === 1 ? "step" : "steps"} captured${recorded.value.trimmed ? " · delays trimmed to 1,000 ms" : " · Esc or F10 to stop"}`
      : (problem.value ??
        (notice.value || "Drag to reorder · right-click for editing commands · Space to preview")),
  );
  function startRecording() {
    if (!draft.value) return;
    stopPreview();
    recordAt = selection.value.length ? Math.max(...selection.value) + 1 : draft.value.steps.length;
    recorder.start();
  }
  function add(type: MacroStep["type"]) {
    const step: MacroStep =
      type === "keys"
        ? { type, keys: [] }
        : type === "text"
          ? { type, text: "" }
          : type === "wait"
            ? { type, ms: 50 }
            : { type, mouse: "middle" };
    insert([step], undefined, `Insert ${type === "wait" ? "delay" : type}`);
    void nextTick(() => inspector.value?.focus());
  }
  function context(index: number) {
    if (!recording.value && !selection.value.includes(index)) select(index);
  }
  function rename(event: Event) {
    const input = event.target as HTMLInputElement;
    const name = input.value.trim();
    if (name && draft.value) commit("Rename macro", { ...draft.value, name });
    else input.value = draft.value?.name ?? "";
  }
  async function duplicate(id = currentId.value) {
    const source = id ? documents.get(id)?.macro : null;
    if (!source || macroProblem(source)) return;
    const copy = { ...cloneMacro(source), id: library.newId(), name: `${source.name} copy` };
    if (await library.change(copy)) {
      editor.ensure(copy);
      openId.value = copy.id;
    }
  }
  async function deleteMacro() {
    confirming.value = false;
    const id = currentId.value;
    if (!id) return;
    const source = library.find(id);
    const oldDocument = documents.get(id)!;
    if (!source || (await library.discard(source, false))) {
      documents.delete(id);
      openId.value = null;
      if (source)
        rememberUndo(
          async () => {
            if (!(await library.change(source, false))) return false;
            documents.set(id, oldDocument);
            openId.value = id;
            return true;
          },
          async () => {
            if (!(await library.discard(source, false))) return false;
            documents.delete(id);
            openId.value = null;
            return true;
          },
          "Delete macro",
        );
    }
  }
  function askDelete(id = currentId.value) {
    if (!id || recording.value) return;
    openId.value = id;
    if (library.usedBy(id).length) confirming.value = true;
    else void deleteMacro();
  }
  function focusName(id = currentId.value) {
    if (id) openId.value = id;
    void nextTick(() => {
      nameField.value?.focus();
      nameField.value?.select();
    });
  }
  const headerActions = computed<MenuAction[]>(() => [
    { label: "Rename", icon: PhPencilSimple, select: () => focusName() },
    { label: "Duplicate macro", icon: PhCopy, disabled: !!problem.value, select: () => void duplicate() },
    { label: "Delete macro", icon: PhTrash, danger: true, select: () => askDelete() },
  ]);
  function macroMenu(id: string): MenuEntry[] {
    return [
      { label: "Rename", icon: PhPencilSimple, disabled: recording.value, select: () => focusName(id) },
      {
        label: "Duplicate macro",
        icon: PhCopy,
        disabled: recording.value || !!macroProblem(documents.get(id)!.macro),
        select: () => void duplicate(id),
      },
      "separator",
      {
        label: "Delete macro",
        icon: PhTrash,
        danger: true,
        disabled: recording.value,
        select: () => askDelete(id),
      },
    ];
  }
  const command = navigator.userAgent.includes("Mac") ? "⌘" : "Ctrl+";
  async function copy(cut = false) {
    if (await editor.copy(cut)) say(cut ? "Steps cut" : "Steps copied");
  }
  async function paste() {
    if (!(await editor.paste())) say("Copy some macro steps first.");
  }
  function stepMenu(index: number): MenuEntry[] {
    const disabled = recording.value;
    return [
      { label: "Cut", icon: PhScissors, shortcut: `${command}X`, disabled, select: () => void copy(true) },
      { label: "Copy", icon: PhCopy, shortcut: `${command}C`, disabled, select: () => void copy() },
      {
        label: "Paste after selection",
        icon: PhClipboardText,
        shortcut: `${command}V`,
        disabled,
        select: () => void paste(),
      },
      { label: "Duplicate", icon: PhCopy, shortcut: `${command}D`, disabled, select: duplicateSteps },
      "separator",
      {
        label: "Move earlier",
        icon: PhArrowUp,
        shortcut: "Alt+↑",
        disabled: disabled || Math.min(...selection.value) <= 0,
        select: () => nudge(-1),
      },
      {
        label: "Move later",
        icon: PhArrowDown,
        shortcut: "Alt+↓",
        disabled: disabled || Math.max(...selection.value) >= (draft.value?.steps.length ?? 0) - 1,
        select: () => nudge(1),
      },
      {
        label: "Insert delay before",
        icon: PhPlus,
        disabled,
        select: () => insert([{ type: "wait", ms: 50 }], index, "Insert delay"),
      },
      "separator",
      { label: "Delete", icon: PhTrash, shortcut: "⌫", danger: true, disabled, select: remove },
    ];
  }
  function scaleDelays(factor: number) {
    if (!draft.value) return;
    commit("Scale delays", {
      ...draft.value,
      steps: draft.value.steps.map((step, i) =>
        selection.value.includes(i) && step.type === "wait"
          ? { ...step, ms: Math.max(1, Math.round((step.ms ?? 0) * factor)) }
          : step,
      ),
    });
  }
  function resizeDelay(index: number, ms: number) {
    const step = draft.value?.steps[index];
    if (step?.type === "wait") update(index, { ...step, ms }, "Resize delay");
  }
  function retime(index: number, ms: number) {
    if (!draft.value) return;
    const entries = macroEntries(draft.value.steps),
      entry = entries[index];
    if (!entry) return;
    if (entry.step.type === "wait") {
      say("Select a gap and drag its right edge to resize it; reorder delay steps in the list.");
      return;
    }
    const previous = entries[index - 1];
    const base = previous?.step.type === "wait" ? previous.at : entry.at;
    const delay = Math.max(0, ms - base);
    if (previous?.step.type === "wait") {
      if (!delay) {
        select(index - 1);
        remove();
        select(index - 1);
      } else resizeDelay(index - 1, delay);
    } else if (delay) insert([{ type: "wait", ms: delay }], index, "Adjust step timing");
  }

  const playing = ref(false),
    previewIndex = ref(-1),
    playhead = ref(0),
    speed = ref(1);
  let frame = 0,
    previousTime = 0,
    remaining = 0;
  function stopPreview() {
    playing.value = false;
    cancelAnimationFrame(frame);
  }
  function resetPreview() {
    stopPreview();
    previewIndex.value = -1;
    playhead.value = 0;
    remaining = 0;
  }
  function togglePreview() {
    if (playing.value) return stopPreview();
    if (!draft.value?.steps.length || problem.value) return;
    if (previewIndex.value < 0 || previewIndex.value >= draft.value.steps.length) {
      previewIndex.value = Math.max(0, selectedIndex.value);
      const entry = macroEntries(draft.value.steps)[previewIndex.value]!;
      const offset = entry.ms && playhead.value >= entry.at && playhead.value < entry.end
        ? playhead.value - entry.at : 0;
      remaining = (entry.ms || 180) - offset;
      playhead.value = entry.at + offset;
    }
    playing.value = true;
    previousTime = performance.now();
    frame = requestAnimationFrame(tick);
  }
  function tick(now: number) {
    if (!playing.value || !draft.value) return;
    const entries = macroEntries(draft.value.steps),
      entry = entries[previewIndex.value];
    if (!entry) {
      stopPreview();
      previewIndex.value = -1;
      return;
    }
    const duration = entry.ms || 180;
    if (remaining <= 0) remaining = duration;
    remaining -= (now - previousTime) * speed.value;
    previousTime = now;
    playhead.value = entry.at + (entry.ms ? Math.min(entry.ms, duration - Math.max(0, remaining)) : 0);
    if (remaining <= 0) {
      previewIndex.value++;
      remaining = 0;
    }
    frame = requestAnimationFrame(tick);
  }
  function seek(ms: number) {
    stopPreview();
    previewIndex.value = -1;
    playhead.value = ms;
    const entries = macroEntries(draft.value?.steps ?? []);
    const index = entries.findLastIndex((entry) => entry.at <= ms);
    if (index >= 0) select(index);
  }
  function jump(by: number) {
    if (!draft.value?.steps.length) return;
    const index = Math.max(0, Math.min(draft.value.steps.length - 1, selectedIndex.value + by));
    stopPreview();
    previewIndex.value = -1;
    select(index);
    playhead.value = macroEntries(draft.value.steps)[index]!.at;
  }
  watch(
    () => canonical(draft.value?.steps),
    resetPreview,
  );
  watch(currentId, () => {
    resetPreview();
    recorder.stop(false);
    confirming.value = false;
    notice.value = "";
  });
  watch(() => pipeConnected.value && !!device.value, ready => { if (!ready) stopPreview(); });
  function onKey(event: KeyboardEvent) {
    if (
      !pipeConnected.value || !device.value ||
      event.defaultPrevented ||
      (event.target as HTMLElement).closest(
        "input, textarea, [contenteditable], [role=slider], [data-dismissable-layer], [role=dialog]",
      )
    )
      return;
    if (event.code === "F9" && recording.value) {
      event.preventDefault();
      paused.value ? recorder.resume() : recorder.pause();
      return;
    }
    if (recording.value) {
      if (["Escape", "F10"].includes(event.code)) {
        event.preventDefault();
        recorder.stop(true);
      }
      return;
    }
    const command = navigator.userAgent.includes("Mac") ? event.metaKey : event.ctrlKey;
    const actions: Record<string, () => unknown> = {
      KeyZ: event.shiftKey ? redoEdit : undoEdit,
      KeyY: redoEdit,
      KeyA: all,
      KeyC: () => copy(),
      KeyX: () => copy(true),
      KeyV: paste,
      KeyD: duplicateSteps,
    };
    if (command && actions[event.code]) {
      event.preventDefault();
      event.stopPropagation();
      actions[event.code]!();
      return;
    }
    if (event.key === "Delete" || event.key === "Backspace") {
      event.preventDefault();
      remove();
    } else if (event.key === "ArrowUp" || event.key === "ArrowDown") {
      event.preventDefault();
      const by = event.key === "ArrowUp" ? -1 : 1;
      if (event.altKey) nudge(by);
      else if (draft.value?.steps.length)
        select(Math.max(0, Math.min(draft.value.steps.length - 1, selectedIndex.value + by)), event);
    } else if (
      event.code === "Space" &&
      !(event.target as HTMLElement).closest("button:not([role=option])")
    ) {
      event.preventDefault();
      togglePreview();
    } else if (event.key === "F2") {
      event.preventDefault();
      focusName();
    }
  }
  onMounted(() => window.addEventListener("keydown", onKey));
  onBeforeUnmount(() => {
    window.removeEventListener("keydown", onKey);
    stopPreview();
    clearTimeout(clearNotice);
  });
  return {
    editor,
    library,
    documents,
    currentId,
    document,
    draft,
    selection,
    selectedIndex,
    problem,
    status,
    openId,
    inspector,
    nameField,
    moreOpen,
    confirming,
    usage,
    list,
    undoLabel,
    redoLabel,
    undoEdit,
    redoEdit,
    headerActions,
    macroMenu,
    recording,
    paused,
    keepPauses,
    recorder,
    shownSteps,
    feedback,
    create,
    playing,
    speed,
    previewIndex,
    playhead,
    jump,
    togglePreview,
    add,
    startRecording,
    stepMenu,
    select,
    context,
    seek,
    retime,
    resizeDelay,
    move,
    update,
    scaleDelays,
    onKey,
    rename,
    deleteMacro,
  };
}
