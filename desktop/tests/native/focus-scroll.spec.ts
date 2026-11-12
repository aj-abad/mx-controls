import { test, expect } from "./fixture";
import { openMacro, watchEditorLayout, watchLayout } from "./layout";

for (const colorScheme of ["light", "dark"] as const) {
test(`keyboard focus stays inside buttons and menu options in ${colorScheme} appearance`, async ({ native }) => {
  const { page } = native;
  await page.emulateMedia({ colorScheme });
  await openMacro(page);
  const stable = await watchEditorLayout(page);
  await page.keyboard.press("Tab");
  const record = page.getByRole("button", { name: "Record", exact: true });
  await record.focus();
  const outline = await record.evaluate(element => {
    const style = getComputedStyle(element);
    return { visible: element.matches(":focus-visible"), style: style.outlineStyle, width: parseFloat(style.outlineWidth), offset: parseFloat(style.outlineOffset) };
  });
  expect(outline).toMatchObject({ visible: true, style: "solid", width: 2 });
  expect(outline.width + outline.offset).toBeLessThanOrEqual(0);
  await page.getByRole("button", { name: "Add", exact: true }).click();
  await page.getByRole("button", { name: "Mouse click", exact: true }).click();
  const trigger = page.getByRole("combobox", { name: "Mouse button", exact: true });
  await trigger.press("Enter");
  await page.keyboard.press("ArrowDown");
  const option = page.getByRole("option", { name: "Back click", exact: true });
  await expect(option).toBeFocused();
  await expect(option).toHaveAttribute("data-highlighted", "");
  const itemOutline = await option.evaluate(element => getComputedStyle(element).outlineStyle);
  expect(itemOutline).toBe("none");
  await page.keyboard.press("Escape");
  await expect(trigger).toBeFocused();
  await stable();
  // Keep the focused menu in the native screenshot for a visual check in each appearance.
  await trigger.press("Enter");
  await page.keyboard.press("ArrowDown");
  await expect(page.getByRole("option", { name: "Back click", exact: true })).toBeFocused();
});
}

test("a long preset menu uses Reka scrolling and keeps keyboard selection in view", async ({ native }) => {
  const { page, agent } = native;
  agent.state.gesturePresets = Array.from({ length: 24 }, (_, i) => ({
    id: `preset-${i + 1}`, name: `Preset ${String(i + 1).padStart(2, "0")}`,
    up: { type: "none" }, down: { type: "none" }, left: { type: "none" }, right: { type: "none" },
  }));
  agent.emit("state", agent.state);
  await page.getByRole("button", { name: "Profiles", exact: true }).click();
  await page.getByRole("button", { name: /^gesture button/i }).last().click();
  const stable = await watchLayout(page, ['main', 'nav[aria-label="Workspace"]', 'footer']);
  const trigger = page.getByRole("combobox", { name: "gesture gesture preset", exact: true });
  await trigger.press("Enter");
  await expect(page.getByRole("option", { name: "None", exact: true })).toBeFocused();
  await page.keyboard.press("End");
  await expect(page.getByRole("option", { name: "New preset…", exact: true })).toBeFocused();
  await page.keyboard.press("ArrowUp");
  const lastPreset = page.getByRole("option", { name: "Preset 24", exact: true });
  await expect(lastPreset).toBeFocused();
  const popup = page.getByRole("listbox");
  const viewport = popup.locator("[data-reka-scroll-area-viewport]");
  await expect.poll(() => viewport.evaluate(element => element.scrollTop)).toBeGreaterThan(0);
  const contained = await lastPreset.evaluate(element => {
    const r = element.getBoundingClientRect();
    const view = element.closest("[data-reka-scroll-area-viewport]")!.getBoundingClientRect();
    return r.top >= view.top - 1 && r.bottom <= view.bottom + 1;
  });
  expect(contained).toBe(true);
  await expect(popup.locator('.ui-scrollbar[data-orientation="vertical"]')).toBeVisible();
  expect(await viewport.evaluate(element => getComputedStyle(element).scrollbarWidth)).toBe("none");
  await page.keyboard.press("Enter");
  await expect.poll(() => agent.state.bindings.find(b => b.button === "gesture")?.preset).toBe("preset-24");
  await expect(trigger).toBeFocused();
  await stable();
});

test("macro steps support wheel scrolling and dragging the Reka thumb", async ({ native }) => {
  const { page, agent } = native;
  agent.state.macros.find(m => m.id === "signoff")!.steps = Array.from({ length: 60 }, (_, i) => ({ type: "text", text: `Line ${i + 1}` }));
  agent.emit("state", agent.state);
  await openMacro(page);
  const stable = await watchEditorLayout(page);
  await page.evaluate(() => {
    const runtime = (window as any).__TAURI_INTERNALS__;
    const invoke = runtime.invoke;
    (window as any).__windowDragCalls = 0;
    runtime.invoke = (command: string, ...args: unknown[]) => {
      if (command === "plugin:window|start_dragging") {
        (window as any).__windowDragCalls++;
        return Promise.resolve();
      }
      return invoke.call(runtime, command, ...args);
    };
  });
  const viewport = page.getByRole("listbox", { name: "Macro step list", exact: true });
  await viewport.hover();
  await page.mouse.wheel(0, 300);
  await expect.poll(() => viewport.evaluate(element => element.scrollTop)).toBeGreaterThan(0);
  const thumb = page.getByRole("region", { name: "Macro steps", exact: true }).locator(".ui-scroll-thumb");
  await expect(thumb).toBeVisible();
  const start = (await thumb.boundingBox())!;
  const before = await viewport.evaluate(element => element.scrollTop);
  await page.mouse.move(start.x + start.width / 2, start.y + start.height / 2);
  await page.mouse.down();
  await page.mouse.move(start.x + start.width / 2, start.y + start.height / 2 + 45, { steps: 8 });
  await page.mouse.up();
  await expect.poll(() => viewport.evaluate(element => element.scrollTop)).toBeGreaterThan(before);
  expect(await page.evaluate(() => (window as any).__windowDragCalls)).toBe(0);
  await stable();
});

test("timeline zoom and horizontal scrolling keep the editor fixed", async ({ native }) => {
  const { page } = native;
  await openMacro(page);
  const stable = await watchEditorLayout(page);
  const timeline = page.getByRole("region", { name: "Macro timeline", exact: true });
  const viewport = timeline.locator("[data-reka-scroll-area-viewport]");
  await page.getByRole("button", { name: "Zoom timeline in", exact: true }).click();
  await expect(timeline.locator('.ui-scrollbar[data-orientation="horizontal"]')).toBeVisible();
  await viewport.hover();
  await page.mouse.wheel(250, 0);
  await expect.poll(() => viewport.evaluate(element => element.scrollLeft)).toBeGreaterThan(0);
  await page.getByRole("button", { name: "Fit", exact: true }).click();
  await expect(timeline.locator('.ui-scrollbar[data-orientation="horizontal"]')).toBeHidden();
  await stable();
});
