import type { RoseItem, RoseMenu } from "~/composables/useAgent";

/** The rose menu library, who shows each menu, and changes to it that Ctrl+Z puts back. */
export function useRoseMenus() {
  const { state, attempt } = useAgent();
  const presets = useGesturePresets();

  const menus = computed(() => state.value?.roseMenus ?? []);
  const find = (id: string | null | undefined) => (id ? menus.value.find((m) => m.id === id) : undefined);
  const profileName = (id: string) => state.value?.profiles.find((p) => p.id === id)?.name ?? id;

  /** "Smart Shift button · Work" for each button showing the menu, leaving out `except`. */
  function usedBy(id: string, except?: { profile: string; button: string; layer?: boolean }) {
    return (state.value?.roseUses ?? [])
      .filter(
        (use) =>
          use.menu === id &&
          !(except && use.profile === except.profile && use.button === except.button && !!use.layer === !!except.layer),
      )
      .map((use) => `${presets.buttonLabel(use.button)} · ${profileName(use.profile)}${use.layer ? " (second layer)" : ""}`);
  }

  const save = (menu: RoseMenu) => attempt("setRoseMenu", { menu }, `Couldn't save “${menu.name}”`);
  const remove = (id: string, name: string) => attempt("deleteRoseMenu", { id }, `Couldn't delete “${name}”`);

  /** Saves a change, and remembers how to put back what was there. */
  async function change(menu: RoseMenu) {
    const before = find(menu.id);
    if (!(await save(menu))) return false;
    rememberUndo(() => (before ? save(before) : remove(menu.id, menu.name)), () => save(menu), "Edit rose menu");
    return true;
  }

  /** Deletes a menu. Buttons showing it keep pointing at it, so undo brings their rose back too. */
  async function discard(menu: RoseMenu) {
    if (!(await remove(menu.id, menu.name))) return false;
    rememberUndo(() => save(menu), () => remove(menu.id, menu.name), "Delete rose menu");
    return true;
  }

  /** Adds a menu and returns its ID, or null if the agent refused it. */
  async function create(from: Omit<RoseMenu, "id">) {
    const id = Math.random().toString(36).slice(2, 10);
    return (await change({ ...from, id })) ? id : null;
  }

  const blank = (): Omit<RoseMenu, "id"> => ({ name: "New rose menu", items: emptyOptions(4) });

  return { menus, find, usedBy, change, discard, create, blank };
}

export const ROSE_MIN = 2;
export const ROSE_MAX = 8;

export const emptyOptions = (count: number): RoseItem[] => Array.from({ length: count }, () => ({ action: NOTHING }));

/** The options of the folder at the end of `path`, by index from the top; null where it breaks off. */
export function roseLevel(items: RoseItem[], path: number[]): RoseItem[] | null {
  let level: RoseItem[] | null = items;
  for (const index of path) level = level?.[index]?.items ?? null;
  return level;
}

/** The options with the folder at `path` given new options. */
export function withRoseLevel(items: RoseItem[], path: number[], change: (level: RoseItem[]) => RoseItem[]): RoseItem[] {
  if (!path.length) return change(items);
  const [first, ...rest] = path as [number, ...number[]];
  return items.map((item, i) => (i === first && item.items ? { ...item, items: withRoseLevel(item.items, rest, change) } : item));
}

/** What an option says: its label, or the folder or action it is. */
export const describeRoseItem = (item: RoseItem) =>
  item.label || (item.items ? "Folder" : item.action.type === "none" ? "Nothing" : describeAction(item.action));

/** "Windows › Option 2" for the first option, folders included, whose action isn't finished; null when all are. */
export function unfinishedRoseItem(items: RoseItem[], names: string[] = []): string | null {
  for (const [i, item] of items.entries()) {
    const here = [...names, item.label || `Option ${i + 1}`];
    if (item.passThrough) continue;
    if (item.items) {
      const inside = unfinishedRoseItem(item.items, here);
      if (inside) return inside;
    } else if (!complete(item.action)) return here.join(" › ");
  }
  return null;
}
