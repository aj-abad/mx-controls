/**
 * Programs' own icons, by program ID, as data URLs from the agent. Undefined until asked for and while loading, null
 * where the agent found none. Asking is batched, so a row of profiles makes one request.
 */
const icons = reactive(new Map<string, string | null>());
const wanted = new Set<string>();
const pending = new Set<string>();
let flushing = false;

async function flush() {
  flushing = false;
  const ids = [...wanted];
  wanted.clear();
  if (!ids.length) return;
  for (const id of ids) pending.add(id);
  try {
    const found = await useAgent().call<Record<string, string | null>>("getProgramIcons", { ids });
    for (const id of ids) icons.set(id, found[id] ?? null);
  } catch {
    // Left unasked, so the next look tries again once the agent is back.
    for (const id of ids) icons.delete(id);
  } finally {
    for (const id of ids) pending.delete(id);
  }
}

export function programIcon(id: string | null | undefined): string | null | undefined {
  if (!id) return null;
  const key = id.toLowerCase();
  if (!icons.has(key) && !wanted.has(key) && !pending.has(key)) {
    wanted.add(key);
    if (!flushing) {
      flushing = true;
      queueMicrotask(flush);
    }
  }
  return icons.get(key);
}
