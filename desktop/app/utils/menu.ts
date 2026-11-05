import type { Component } from "vue";

export interface MenuAction {
  label: string;
  icon?: Component;
  /** Destroys something, so it reads in red. */
  danger?: boolean;
  disabled?: boolean;
  /** Shows a check, for an item that toggles or names the current choice. */
  checked?: boolean;
  shortcut?: string;
  select: () => void;
}

/** What `UiContextMenu` lists, top to bottom; a separator splits it into groups. */
export type MenuEntry = MenuAction | "separator";

/** Copies text to the clipboard, saying so only if it can't. */
export async function copyText(text: string, what: string) {
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    reportFailure(`Couldn't copy the ${what}`);
  }
}
