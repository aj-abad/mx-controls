/** Puts back one change; resolves to whether it took. */
type Undo = () => Promise<boolean>;
interface Edit {
  undo: Undo;
  redo?: Undo;
  label: string;
}

const history = shallowRef<Edit[]>([]);
const future = shallowRef<Edit[]>([]);
const busy = ref(false);
let generation = 0;
const LIMIT = 50;

/** Remembers how to put back a change just made, for Ctrl+Z (Cmd+Z on a Mac). */
export function rememberUndo(undo: Undo, redo?: Undo, label = "change") {
  generation++;
  history.value = [...history.value, { undo, redo, label }].slice(-LIMIT);
  future.value = [];
}

export function forgetRedo() {
  generation++;
  future.value = [];
}
export function useUndoState() {
  return {
    undoLabel: computed(() => history.value.at(-1)?.label ?? ""),
    redoLabel: computed(() => future.value.at(-1)?.label ?? ""),
    busy: readonly(busy),
  };
}

/** Drops every change remembered so far, once putting them back would land somewhere else. */
export function forgetUndo() {
  generation++;
  history.value = [];
  future.value = [];
}

/** Puts back the latest change still remembered. */
export async function undoLast() {
  if (busy.value) return false;
  const edit = history.value.at(-1);
  if (!edit) return false;
  busy.value = true;
  const mine = generation;
  try {
    if (!(await edit.undo())) return false;
    history.value = history.value.filter((item) => item !== edit);
    if (edit.redo && generation === mine) future.value = [...future.value, edit];
    return true;
  } finally {
    busy.value = false;
  }
}

export async function redoLast() {
  if (busy.value) return false;
  const edit = future.value.at(-1);
  if (!edit?.redo) return false;
  busy.value = true;
  try {
    if (!(await edit.redo())) return false;
    future.value = future.value.filter((item) => item !== edit);
    history.value = [...history.value, edit];
    return true;
  } finally {
    busy.value = false;
  }
}
