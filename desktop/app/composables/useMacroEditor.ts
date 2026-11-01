import type { Macro, MacroStep } from "~/composables/useAgent";
import type { MacroEdit } from "~/utils/macroEditing";

interface Document {
  macro: Macro;
  saved: string;
  selection: number[];
  anchor: number;
  cursor: number;
  past: MacroEdit[];
  future: MacroEdit[];
  saving: boolean;
  token: number;
  error: boolean;
}

// Survives page navigation, including unfinished drafts and their edit histories.
const documents = reactive(new Map<string, Document>());
const stepClipboard = ref<MacroStep[]>([]);

export function useMacroEditor() {
  const library = useMacros();
  const { openMacro: openId } = usePage();
  const freshId = ref<string | null>(null);
  const currentId = computed(() =>
    openId.value && documents.has(openId.value)
      ? openId.value
      : (library.find(openId.value)?.id ?? library.macros.value[0]?.id ?? null),
  );
  const document = computed(() => (currentId.value ? (documents.get(currentId.value) ?? null) : null));
  const draft = computed(() => document.value?.macro ?? null);
  const selection = computed(() => document.value?.selection ?? []);
  const selectedIndex = computed(() =>
    document.value && selection.value.includes(document.value.cursor)
      ? document.value.cursor
      : (selection.value.at(-1) ?? -1),
  );
  const problem = computed(() => (draft.value ? macroProblem(draft.value) : null));
  const status = computed(() =>
    !document.value
      ? ""
      : problem.value
        ? "Draft · not saved"
        : document.value.error
          ? "Couldn't save · draft retained"
          : document.value.saving
            ? "Saving…"
            : "Saved",
  );

  function ensure(macro: Macro) {
    if (!documents.has(macro.id))
      documents.set(macro.id, {
        macro: cloneMacro(macro),
        saved: canonical(macro),
        selection: [],
        anchor: -1,
        cursor: -1,
        past: [],
        future: [],
        saving: false,
        token: 0,
        error: false,
      });
    return documents.get(macro.id)!;
  }
  watch(
    () => library.macros.value,
    (macros) => {
      for (const macro of macros) {
        const doc = ensure(macro);
        if (!doc.saving && canonical(doc.macro) === doc.saved && canonical(macro) !== doc.saved) {
          doc.macro = cloneMacro(macro);
          doc.saved = canonical(macro);
          doc.past = [];
          doc.future = [];
          doc.selection = doc.selection.filter((i) => i < macro.steps.length);
        }
      }
    },
    { immediate: true },
  );

  async function save(doc: Document) {
    if (macroProblem(doc.macro)) return;
    const snapshot = cloneMacro(doc.macro);
    const token = ++doc.token;
    doc.saving = true;
    doc.error = false;
    const ok = await library.change(snapshot, false);
    if (token !== doc.token) return;
    doc.saving = false;
    doc.error = !ok;
    if (ok) {
      doc.saved = canonical(snapshot);
      if (freshId.value === snapshot.id) freshId.value = null;
    }
  }

  function commit(label: string, macro: Macro, nextSelection = selection.value) {
    const doc = document.value;
    if (!doc || canonical(macro) === canonical(doc.macro)) return;
    forgetRedo();
    doc.past.push({
      label,
      before: cloneMacro(doc.macro),
      after: cloneMacro(macro),
      beforeSelection: [...doc.selection],
      afterSelection: [...nextSelection],
    });
    if (doc.past.length > 100) doc.past.shift();
    doc.future = [];
    doc.macro = cloneMacro(macro);
    doc.selection = [...nextSelection];
    doc.anchor = nextSelection.at(-1) ?? -1;
    doc.cursor = doc.anchor;
    void save(doc);
  }
  function undo() {
    const doc = document.value;
    const edit = doc?.past.pop();
    if (!doc || !edit) return;
    doc.future.push(edit);
    doc.macro = cloneMacro(edit.before);
    doc.selection = [...edit.beforeSelection];
    doc.anchor = doc.selection.at(-1) ?? -1;
    doc.cursor = doc.anchor;
    void save(doc);
  }
  function redo() {
    const doc = document.value;
    const edit = doc?.future.pop();
    if (!doc || !edit) return;
    doc.past.push(edit);
    doc.macro = cloneMacro(edit.after);
    doc.selection = [...edit.afterSelection];
    doc.anchor = doc.selection.at(-1) ?? -1;
    doc.cursor = doc.anchor;
    void save(doc);
  }
  function select(
    index: number,
    modifiers: { shiftKey?: boolean; ctrlKey?: boolean; metaKey?: boolean } = {},
  ) {
    const doc = document.value;
    if (!doc || index < 0 || index >= doc.macro.steps.length) return;
    doc.cursor = index;
    if (modifiers.shiftKey && doc.anchor >= 0) {
      const low = Math.min(doc.anchor, index),
        high = Math.max(doc.anchor, index);
      doc.selection = Array.from({ length: high - low + 1 }, (_, i) => low + i);
    } else if (modifiers.ctrlKey || modifiers.metaKey) {
      doc.selection = doc.selection.includes(index)
        ? doc.selection.filter((i) => i !== index)
        : [...doc.selection, index].sort((a, b) => a - b);
      doc.anchor = index;
    } else {
      doc.selection = [index];
      doc.anchor = index;
    }
  }
  function all() {
    if (document.value) document.value.selection = document.value.macro.steps.map((_, i) => i);
  }
  function insert(
    steps: MacroStep[],
    boundary = selection.value.length ? Math.max(...selection.value) + 1 : (draft.value?.steps.length ?? 0),
    label = "Insert steps",
  ) {
    if (!draft.value || !steps.length) return;
    const at = Math.max(0, Math.min(draft.value.steps.length, boundary));
    commit(
      label,
      {
        ...draft.value,
        steps: [...draft.value.steps.slice(0, at), ...cloneMacro(steps), ...draft.value.steps.slice(at)],
      },
      steps.map((_, i) => at + i),
    );
  }
  function remove() {
    if (!draft.value || !selection.value.length) return;
    const steps = draft.value.steps.filter((_, i) => !selection.value.includes(i));
    const index = Math.min(Math.min(...selection.value), steps.length - 1);
    commit("Delete steps", { ...draft.value, steps }, index >= 0 ? [index] : []);
  }
  function update(index: number, step: MacroStep, label = "Edit step") {
    if (!draft.value) return;
    commit(label, { ...draft.value, steps: draft.value.steps.map((s, i) => (i === index ? step : s)) });
  }
  function move(boundary: number) {
    if (!draft.value || !selection.value.length) return;
    const moved = reorderMacroSteps(draft.value.steps, selection.value, boundary);
    commit("Move steps", { ...draft.value, steps: moved.steps }, moved.selection);
  }
  function nudge(by: number) {
    if (!selection.value.length || !draft.value) return;
    const first = Math.min(...selection.value),
      last = Math.max(...selection.value);
    if ((by < 0 && first === 0) || (by > 0 && last === draft.value.steps.length - 1)) return;
    move(by < 0 ? first - 1 : last + 2);
  }
  function duplicateSteps() {
    if (draft.value)
      insert(
        [...selection.value].sort((a, b) => a - b).map((i) => draft.value!.steps[i]!),
        undefined,
        "Duplicate steps",
      );
  }
  async function copy(cut = false) {
    if (!draft.value || !selection.value.length) return false;
    stepClipboard.value = cloneMacro(
      [...selection.value].sort((a, b) => a - b).map((i) => draft.value!.steps[i]!),
    );
    // Editing remains immediate even if the platform clipboard is unavailable or waiting for permission.
    if (navigator.clipboard)
      void navigator.clipboard.writeText(encodeMacroSteps(stepClipboard.value)).catch(() => {});
    if (cut) remove();
    return true;
  }
  async function paste() {
    const target = currentId.value;
    const boundary = selection.value.length
      ? Math.max(...selection.value) + 1
      : (draft.value?.steps.length ?? 0);
    let steps = stepClipboard.value;
    try {
      const text = await Promise.race([
        navigator.clipboard.readText(),
        new Promise<string>((resolve) => setTimeout(() => resolve(""), 400)),
      ]);
      if (text) {
        const decoded = decodeMacroSteps(text);
        if (!decoded) return false;
        steps = decoded;
      }
    } catch {
      /* Use the in-app clipboard. */
    }
    if (!steps.length || currentId.value !== target) return false;
    insert(steps, boundary, "Paste steps");
    return true;
  }
  function create() {
    const macro: Macro = { id: library.newId(), name: "New macro", steps: [] };
    const doc = ensure(macro);
    doc.saved = "";
    freshId.value = macro.id;
    openId.value = macro.id;
  }
  return {
    library,
    documents,
    currentId,
    document,
    draft,
    selection,
    selectedIndex,
    problem,
    status,
    freshId,
    ensure,
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
    copy,
    paste,
    create,
    save,
  };
}
