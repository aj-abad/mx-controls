/** Puts back one change; resolves to whether it took. */
type Undo = () => Promise<boolean>;

const history: Undo[] = [];
const LIMIT = 50;

/** Remembers how to put back a change just made, for Ctrl+Z (Cmd+Z on a Mac). */
export function rememberUndo(undo: Undo) {
  history.push(undo);
  if (history.length > LIMIT) history.shift();
}

/** Drops every change remembered so far, once putting them back would land somewhere else. */
export function forgetUndo() {
  history.length = 0;
}

/** Puts back the latest change still remembered. */
export function undoLast() {
  return history.pop()?.() ?? Promise.resolve(false);
}
