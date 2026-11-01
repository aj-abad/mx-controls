import { test, expect } from "./fixture";
import { watchLayout } from "./layout";

test("a button shortcut is stored inline and survives reopening without creating a shared macro", async ({ native }) => {
  const { page, agent } = native;
  const originalMacros = structuredClone(agent.state.macros);
  await page.getByRole("button", { name: "Profiles", exact: true }).click();
  await page.getByRole("button", { name: /^back button/i }).last().click();
  const editor = page.locator(".action-editor");
  const stable = await watchLayout(page, ['main', '.action-editor', 'footer']);
  await editor.getByRole("button", { name: /^back Click: Snap left/ }).click();
  await page.getByRole("textbox", { name: /search actions$/ }).fill("Keyboard shortcut");
  await page.getByRole("option", { name: /^Keyboard shortcut…/ }).click();
  await expect.poll(() => agent.capturing).toBe(true);
  agent.key("ControlLeft", true); agent.key("ShiftLeft", true); agent.key("KeyK", true);
  await expect.poll(() => agent.capturing).toBe(false);
  await expect.poll(() => agent.state.bindings.find(b => b.button === "back")?.action).toEqual({
    type: "press", keys: ["ControlLeft", "ShiftLeft", "KeyK"],
  });
  await expect(editor.getByRole("button", { name: /^back Click: Keyboard shortcut/ })).toBeVisible();
  await expect(editor.getByRole("button", { name: "back Click: Left Ctrl + Left Shift + K", exact: true })).toBeFocused();
  expect(agent.state.macros).toEqual(originalMacros);
  expect(agent.requests.filter(r => r.method === "setMacro")).toHaveLength(0);
  await stable();
  await page.reload();
  await page.getByRole("button", { name: /^back button/i }).last().click();
  await expect(page.getByRole("button", { name: "back Click: Left Ctrl + Left Shift + K", exact: true })).toBeVisible();
  expect(agent.state.macros).toEqual(originalMacros);
});

test("gesture shortcuts repeat with movement and switching direction keeps the editor anchored", async ({ native }) => {
  const { page, agent } = native;
  await page.getByRole("button", { name: /^Gestures/ }).click();
  const movement = page.getByRole("region", { name: "Selected movement", exact: true });
  const stable = await watchLayout(page, [
    'main', '.action-editor', '[aria-label="Movement guide"]', '[aria-label="Movement behavior"]',
    '[aria-label="Choose a gesture direction"] > button', '.gesture-workspace', 'footer',
  ]);
  await expect(movement.getByRole("heading", { name: "Move up", exact: true })).toBeVisible();
  await expect(movement.getByText("Hold the button, move up.", { exact: true })).toBeVisible();
  await movement.getByRole("button", { name: /^Window tiling .*:/ }).first().click();
  await page.getByRole("textbox", { name: /search actions$/ }).fill("Keyboard shortcut");
  await page.getByRole("option", { name: /^Keyboard shortcut…/ }).click();
  await expect.poll(() => agent.capturing).toBe(true);
  agent.key("AltLeft", true); agent.key("KeyK", true);
  await expect.poll(() => agent.capturing).toBe(false);
  await expect.poll(() => agent.state.gesturePresets[0]!.up.keys).toEqual(["AltLeft", "KeyK"]);
  await movement.getByRole("checkbox", { name: "Repeat with movement", exact: true }).check();
  await expect.poll(() => agent.state.gesturePresets[0]!.up.repeat).toBe(true);
  await expect(movement.getByText("Repeats with movement", { exact: true })).toBeVisible();
  await expect(movement.getByText(/repeats every 4 mm/)).toBeVisible();
  await page.getByRole("button", { name: /^Edit Right:/ }).click();
  await expect(movement.getByRole("heading", { name: "Move right", exact: true })).toBeVisible();
  await expect(movement.getByRole("checkbox", { name: "Repeat with movement", exact: true })).not.toBeChecked();
  await page.getByRole("button", { name: /^Edit Up:/ }).click();
  await expect(movement.getByRole("checkbox", { name: "Repeat with movement", exact: true })).toBeChecked();
  expect(agent.state.macros).toHaveLength(3);
  expect(agent.requests.filter(r => r.method === "setMacro")).toHaveLength(0);
  await stable();
});

test("a shared one-combination macro can be copied into just one action", async ({ native }) => {
  const { page, agent } = native;
  const originalMacros = structuredClone(agent.state.macros);
  await page.getByRole("button", { name: "Profiles", exact: true }).click();
  await page.getByRole("button", { name: /^back button/i }).last().click();
  const stable = await watchLayout(page, ['main', '.action-editor', 'footer']);
  await page.getByRole("button", { name: "Use as shortcut", exact: true }).click();
  await expect.poll(() => agent.state.bindings[0]!.action).toEqual({ type: "press", keys: ["MetaLeft", "ArrowLeft"] });
  await expect(page.getByRole("button", { name: /Edit shared macro/ })).toHaveCount(0);
  await expect(page.getByRole("button", { name: /^back Click: Keyboard shortcut/ })).toBeVisible();
  expect(agent.state.macros).toEqual(originalMacros);
  expect(agent.requests.some(r => r.method === "setMacro" || r.method === "deleteMacro")).toBe(false);
  await stable();
});
