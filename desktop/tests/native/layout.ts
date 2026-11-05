import { expect, type Page } from "@playwright/test";

export async function openMacro(page: Page, name = "Sign off") {
  await page.getByRole("button", { name: /^Macros/ }).click();
  await page.getByRole("button", { name: new RegExp(`^${name} `) }).click();
  await expect(page.getByRole("region", { name: "Macro timeline", exact: true })).toBeVisible();
}

/** Observe every DOM update and resize, including shifts that settle before a final screenshot. */
export async function watchEditorLayout(page: Page) {
  return watchLayout(page, [
      'nav[aria-label="Workspace"]', 'main', '[aria-label="Macros"]', '[aria-label="Macro name"]',
      '[aria-label="Macro editor controls"]', '[aria-label="Macro timeline"]',
      '[aria-label="Macro steps"]', '[aria-label="Step inspector"]', 'footer',
      '[aria-label="Macro editor controls"] > button',
    ]);
}

export async function watchLayout(page: Page, selectors: string[]) {
  await page.evaluate(selectors => {
    const nodes = selectors.flatMap(selector => {
      const matches = [...document.querySelectorAll(selector)];
      if (!matches.length) throw new Error(`Missing layout region: ${selector}`);
      return matches.map((node, index) => {
        const { x, y, width, height } = node.getBoundingClientRect();
        return { node, label: `${selector}:${index}`, initial: { x, y, width, height } };
      });
    });
    const shifts: unknown[] = [];
    const changed = new Set<string>();
    const sample = () => {
      for (const { node, label, initial } of nodes) {
        if (!node.isConnected || changed.has(label)) continue;
        const { x, y, width, height } = node.getBoundingClientRect();
        const actual = { x, y, width, height };
        if ((["x", "y", "width", "height"] as const).some(key => Math.abs(actual[key] - initial[key]) > 0.5)) {
          changed.add(label);
          if (shifts.length < 20) shifts.push({ label, initial, actual });
        }
      }
    };
    const resize = new ResizeObserver(sample);
    nodes.forEach(({ node }) => resize.observe(node));
    const mutation = new MutationObserver(sample);
    mutation.observe(document.body, { childList: true, subtree: true, attributes: true, characterData: true });
    const clearTimer = window.clearInterval.bind(window);
    const timer = setInterval(sample, 25);
    (window as any).__editorLayout = { shifts, stop: () => { sample(); clearTimer(timer); resize.disconnect(); mutation.disconnect(); } };
  }, selectors);
  return async () => {
    const shifts = await page.evaluate(() => {
      const probe = (window as any).__editorLayout;
      probe.stop();
      return probe.shifts;
    });
    expect(shifts, "Editor controls and panels moved during an action").toEqual([]);
  };
}
