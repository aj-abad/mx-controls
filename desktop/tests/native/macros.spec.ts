import { test, expect } from "./fixture";
import type { Page } from "@playwright/test";

async function openSignOff(page: Page) {
  await page.getByRole("button", { name: /^Macros/ }).click();
  await page.getByRole("button", { name: /^Sign off / }).click();
  await expect(page.getByRole("region", { name: "Macro timeline", exact: true })).toBeVisible();
}

async function frames(page: Page) {
  return page.evaluate(() => [
    '[aria-label="Macro editor controls"]', '[aria-label="Macro timeline"]',
    '[aria-label="Macro steps"]', '[aria-label="Step inspector"]', 'footer',
  ].map(selector => {
    const element = document.querySelector(selector);
    if (!element) throw new Error(`Missing layout region: ${selector}`);
    const { x, y, width, height } = element.getBoundingClientRect();
    return { selector, x, y, width, height };
  }));
}

test("native shell uses real Rust IPC and an isolated simulated agent", async ({ native }) => {
  const { page, agent, environment } = native;
  expect(await environment()).toMatchObject({ hardwareAgentEnabled: false, visible: false, focused: false });
  await openSignOff(page);
  await expect(page.getByRole("option")).toHaveCount(3);
  expect(agent.requests.filter(r => r.method === "getState").length).toBeGreaterThan(0);
  expect(await page.evaluate(() => "__TAURI_INTERNALS__" in window)).toBe(true);
});

test("record, pause, resume, stop and undo keep the workspace fixed", async ({ native }) => {
  const { page, agent } = native;
  await openSignOff(page);
  const before = await frames(page);
  await page.getByRole("button", { name: "Record", exact: true }).click();
  await expect.poll(() => agent.capturing).toBe(true);
  expect(await frames(page)).toEqual(before);
  agent.key("ControlLeft", true); agent.key("KeyA", true);
  agent.key("KeyA", false); agent.key("ControlLeft", false);
  await page.getByRole("button", { name: "Pause recording", exact: true }).click();
  await expect.poll(() => agent.capturing).toBe(false);
  expect(await frames(page)).toEqual(before);
  await page.getByRole("button", { name: "Resume recording", exact: true }).click();
  await expect.poll(() => agent.capturing).toBe(true);
  expect(await frames(page)).toEqual(before);
  agent.key("KeyB", true); agent.key("KeyB", false);
  await page.getByRole("button", { name: "Stop", exact: true }).click();
  await expect.poll(() => agent.capturing).toBe(false);
  expect(await frames(page)).toEqual(before);
  await expect.poll(() => agent.state.macros.find(m => m.id === "signoff")!.steps.length).toBeGreaterThan(3);
  await page.getByRole("button", { name: "Undo macro edit", exact: true }).click();
  await expect(page.getByRole("option")).toHaveCount(3);
  expect(await frames(page)).toEqual(before);
});

test("delay gaps edit through the native pipe and support undo and redo", async ({ native }) => {
  const { page, agent } = native;
  await openSignOff(page);
  const before = await frames(page);
  await page.getByRole("button", { name: "Delay gap at step 2: 50 ms", exact: true }).click();
  await page.getByRole("button", { name: "250 ms", exact: true }).click();
  await expect(page.getByRole("button", { name: "Delay gap at step 2: 250 ms", exact: true })).toHaveAttribute("aria-pressed", "true");
  await expect.poll(() => agent.state.macros.find(m => m.id === "signoff")!.steps[1]!.ms).toBe(250);
  expect(await frames(page)).toEqual(before);
  await page.getByRole("button", { name: "Undo macro edit", exact: true }).click();
  await expect(page.getByRole("button", { name: "Delay gap at step 2: 50 ms", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Redo macro edit", exact: true }).click();
  await expect(page.getByRole("button", { name: "Delay gap at step 2: 250 ms", exact: true })).toBeVisible();
  const handle = page.getByRole("slider", { name: "Resize delay gap at step 2", exact: true });
  await handle.focus();
  await handle.press("ArrowRight");
  await expect.poll(() => agent.state.macros.find(m => m.id === "signoff")!.steps[1]!.ms).toBe(255);
  expect(await frames(page)).toEqual(before);
});

test("context menus, selection and clipboard use the app's editing commands", async ({ native }) => {
  const { page, agent } = native;
  await openSignOff(page);
  const before = await frames(page);
  const steps = page.getByRole("listbox", { name: "Macro step list", exact: true });
  await steps.getByRole("option").first().click();
  await steps.getByRole("option").nth(1).click({ modifiers: ["Shift"] });
  await expect(steps.locator('[aria-selected="true"]')).toHaveCount(2);
  await steps.getByRole("option").nth(1).click({ button: "right" });
  await page.getByRole("menuitem", { name: /^Copy/ }).click();
  await steps.press("Control+v");
  await expect(steps.getByRole("option")).toHaveCount(5);
  await expect.poll(() => agent.state.macros.find(m => m.id === "signoff")!.steps.length).toBe(5);
  expect(await frames(page)).toEqual(before);
  await page.getByRole("button", { name: "Undo macro edit", exact: true }).click();
  await expect(steps.getByRole("option")).toHaveCount(3);
});

test("failed saves can retry without moving the editor", async ({ native }) => {
  const { page, agent } = native;
  await openSignOff(page);
  const before = await frames(page);
  agent.failNext("setMacro", "Simulated save failure");
  await page.getByRole("button", { name: "Delay gap at step 2: 50 ms", exact: true }).click();
  await page.getByRole("button", { name: "250 ms", exact: true }).click();
  const retry = page.getByRole("button", { name: "Retry saving macro", exact: true });
  await expect(retry).toBeVisible();
  expect(agent.state.macros.find(m => m.id === "signoff")!.steps[1]!.ms).toBe(50);
  expect(await frames(page)).toEqual(before);
  await retry.click({ timeout: 2000 });
  await expect.poll(() => agent.state.macros.find(m => m.id === "signoff")!.steps[1]!.ms).toBe(250);
  await expect(retry).toBeHidden();
  expect(await frames(page)).toEqual(before);
});

test("timeline pointer gestures, zoom and preview keep the editor frame stable", async ({ native }) => {
  const { page, agent } = native;
  await openSignOff(page);
  const before = await frames(page);
  await page.getByRole("button", { name: "Delay gap at step 2: 50 ms", exact: true }).click();
  await page.getByRole("button", { name: "250 ms", exact: true }).click();
  const handle = await page.getByRole("slider", { name: "Resize delay gap at step 2", exact: true }).boundingBox();
  if (!handle) throw new Error("Missing timeline resize handle");
  // Playwright's mouse sends CDP input to this WebView, never Windows SendInput.
  await page.mouse.move(handle.x + handle.width / 2, handle.y + handle.height / 2);
  await page.mouse.down();
  await page.mouse.move(handle.x + handle.width / 2 + 65, handle.y + handle.height / 2, { steps: 6 });
  await page.mouse.up();
  await expect.poll(() => agent.state.macros.find(m => m.id === "signoff")!.steps[1]!.ms).toBeGreaterThan(250);
  await page.getByRole("button", { name: "Zoom timeline in", exact: true }).click();
  expect(await frames(page)).toEqual(before);
  await page.getByRole("slider", { name: "Timeline playhead", exact: true }).press("End");
  await page.getByRole("button", { name: "Preview macro", exact: true }).click();
  expect(await frames(page)).toEqual(before);
  expect(agent.requests.every(r => ["getState", "setMacro"].includes(r.method))).toBe(true);
});
