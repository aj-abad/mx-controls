import { test, expect } from "./fixture";
import { openMacro, watchEditorLayout, watchLayout } from "./layout";

test("recorder hotkeys, discard and navigation release capture and preserve the draft", async ({ native }) => {
  const { page, agent } = native;
  await openMacro(page);
  const stable = await watchEditorLayout(page);
  await page.getByRole("button", { name: "Record", exact: true }).click();
  await expect.poll(() => agent.capturing).toBe(true);
  agent.key("KeyA", true); agent.key("KeyA", false);
  agent.key("F9", true);
  await expect.poll(() => agent.capturing).toBe(false);
  await expect(page.getByRole("button", { name: "Resume recording", exact: true })).toBeEnabled();
  await page.keyboard.press("F9");
  await expect.poll(() => agent.capturing).toBe(true);
  agent.key("KeyB", true); agent.key("KeyB", false);
  agent.key("F10", true);
  await expect.poll(() => agent.capturing).toBe(false);
  await expect.poll(() => agent.state.macros.find(m => m.id === "signoff")!.steps.length).toBe(5);
  expect(agent.state.macros.find(m => m.id === "signoff")!.steps.slice(-2)).toEqual([
    { type: "keys", keys: ["KeyA"] }, { type: "keys", keys: ["KeyB"] },
  ]);
  await page.getByRole("button", { name: "Record", exact: true }).click();
  await expect.poll(() => agent.capturing).toBe(true);
  agent.key("KeyC", true);
  await page.getByRole("button", { name: "Discard recording", exact: true }).click();
  await expect.poll(() => agent.capturing).toBe(false);
  await expect(page.getByRole("option")).toHaveCount(5);
  await stable();
  await page.getByRole("button", { name: "Record", exact: true }).click();
  await expect.poll(() => agent.capturing).toBe(true);
  await page.getByRole("button", { name: "Profiles", exact: true }).click();
  await expect.poll(() => agent.capturing).toBe(false);
  await openMacro(page);
  await expect(page.getByRole("option")).toHaveCount(5);
});

test("long shortcut chords stay within the inspector and return the keyboard", async ({ native }) => {
  const { page, agent } = native;
  await openMacro(page);
  const stable = await watchEditorLayout(page);
  await page.getByRole("option").first().dblclick();
  await page.getByRole("button", { name: /^Step 1 keys:/ }).click();
  await expect.poll(() => agent.capturing).toBe(true);
  const keys = ["ControlLeft", "ControlRight", "ShiftLeft", "ShiftRight", "AltLeft", "AltRight", "MetaLeft", "MetaRight", "KeyA"];
  keys.forEach(code => agent.key(code, true));
  await expect.poll(() => agent.capturing).toBe(false);
  await expect.poll(() => agent.state.macros.find(m => m.id === "signoff")!.steps[0]!.keys).toEqual(keys);
  const field = page.getByRole("button", { name: /^Step 1 keys:/ });
  const overflow = await field.evaluate(button => {
    const rect = button.getBoundingClientRect();
    return [...button.querySelectorAll("kbd")].some(key => {
      const r = key.getBoundingClientRect();
      return r.top < rect.top || r.bottom > rect.bottom;
    });
  });
  expect(overflow, "Shortcut chips overflow their fixed-height field").toBe(false);
  await stable();
});

test("new text and mouse steps validate, survive navigation, and keep editor geometry", async ({ native }) => {
  const { page, agent } = native;
  await openMacro(page);
  const stable = await watchEditorLayout(page);
  await page.getByRole("button", { name: "Add", exact: true }).click();
  await page.getByRole("button", { name: "Text", exact: true }).click();
  const text = page.getByRole("textbox", { name: "Step text", exact: true });
  await expect(text).toBeFocused();
  await expect(page.getByRole("status")).toContainText("Draft");
  expect(agent.state.macros.find(m => m.id === "signoff")!.steps.length).toBe(3);
  await text.fill("A saved line\nAnd a second line");
  await text.press("Tab");
  await expect.poll(() => agent.state.macros.find(m => m.id === "signoff")!.steps.length).toBe(4);
  await page.getByRole("button", { name: "Add", exact: true }).click();
  await page.getByRole("button", { name: "Mouse click", exact: true }).click();
  await expect.poll(() => agent.state.macros.find(m => m.id === "signoff")!.steps.length).toBe(5);
  await stable();
  await page.getByRole("button", { name: "Gestures 1", exact: true }).click();
  await openMacro(page);
  await expect(page.getByRole("option")).toHaveCount(5);
  await page.getByRole("button", { name: "Undo macro edit", exact: true }).click();
  await expect(page.getByRole("option")).toHaveCount(4);
});

test("arrow navigation reveals and focuses steps in a long list", async ({ native }) => {
  const { page, agent } = native;
  const macro = agent.state.macros.find(m => m.id === "signoff")!;
  macro.steps = Array.from({ length: 30 }, (_, i) => ({ type: "text", text: `Line ${i + 1}` }));
  agent.emit("state", agent.state);
  await openMacro(page);
  const stable = await watchEditorLayout(page);
  const list = page.getByRole("listbox", { name: "Macro step list", exact: true });
  await list.getByRole("option").first().click();
  for (let index = 0; index < 20; index++) await page.keyboard.press("ArrowDown");
  await expect(list.getByRole("option").nth(20)).toBeFocused();
  await expect(list.getByRole("option").nth(20)).toHaveAttribute("aria-selected", "true");
  expect(await list.evaluate(element => element.scrollTop)).toBeGreaterThan(0);
  await stable();
});

test("dragging a selected block preserves step order and supports keyboard undo", async ({ native }) => {
  const { page, agent } = native;
  await openMacro(page);
  const stable = await watchEditorLayout(page);
  const list = page.getByRole("listbox", { name: "Macro step list", exact: true });
  await list.getByRole("option").first().click();
  await list.getByRole("option").nth(1).click({ modifiers: ["Shift"] });
  const start = await list.getByRole("option").first().boundingBox();
  const end = await list.getByRole("option").nth(2).boundingBox();
  if (!start || !end) throw new Error("Missing drag rows");
  await page.mouse.move(start.x + 20, start.y + 18);
  await page.mouse.down();
  await page.mouse.move(end.x + 20, end.y + end.height - 2, { steps: 8 });
  await page.mouse.up();
  await expect.poll(() => agent.state.macros.find(m => m.id === "signoff")!.steps.map(s => s.type)).toEqual(["text", "keys", "wait"]);
  await page.keyboard.press("Control+z");
  await expect.poll(() => agent.state.macros.find(m => m.id === "signoff")!.steps.map(s => s.type)).toEqual(["keys", "wait", "text"]);
  await page.keyboard.press("Control+Shift+z");
  await expect.poll(() => agent.state.macros.find(m => m.id === "signoff")!.steps.map(s => s.type)).toEqual(["text", "keys", "wait"]);
  await stable();
});

test("keyboard context menu acts on its focused step", async ({ native }) => {
  const { page, agent } = native;
  await openMacro(page);
  await page.getByRole("option").first().click();
  const text = page.getByRole("option").nth(2);
  await text.focus();
  await text.press("Shift+F10");
  await page.getByRole("menuitem", { name: /^Duplicate/ }).click();
  await expect.poll(() => agent.state.macros.find(m => m.id === "signoff")!.steps.map(s => s.type)).toEqual(["keys", "wait", "text", "text"]);
});

test("a new empty macro can be deleted and restored without losing the remaining library", async ({ native }) => {
  const { page, agent } = native;
  await openMacro(page);
  await page.getByRole("button", { name: "New macro", exact: true }).click();
  await expect(page.getByRole("option")).toHaveCount(0);
  const stable = await watchEditorLayout(page);
  await page.getByRole("button", { name: "Add", exact: true }).click();
  await page.getByRole("button", { name: "Mouse click", exact: true }).click();
  await expect.poll(() => agent.state.macros.length).toBe(4);
  await page.getByRole("button", { name: "Macro actions", exact: true }).click();
  await page.getByRole("button", { name: "Delete macro", exact: true }).click();
  await expect.poll(() => agent.state.macros.length).toBe(3);
  await page.getByRole("button", { name: "Undo macro edit", exact: true }).click();
  await expect.poll(() => agent.state.macros.length).toBe(4);
  await expect(page.getByRole("textbox", { name: "Macro name", exact: true })).toHaveValue("New macro");
  await stable();
});

test("stopping during capture startup releases the keyboard immediately", async ({ native }) => {
  const { page, agent } = native;
  await openMacro(page);
  const stable = await watchEditorLayout(page);
  const answer = agent.deferNextReply("startKeyCapture");
  try {
    await page.getByRole("button", { name: "Record", exact: true }).click();
    await expect.poll(() => agent.capturing).toBe(true);
    await page.getByRole("button", { name: "Discard recording", exact: true }).click();
    await expect.poll(() => agent.capturing, { timeout: 1500 }).toBe(false);
    await expect(page.getByRole("button", { name: "Record", exact: true })).toBeEnabled();
    await stable();
  } finally { answer(); }
});

test("a lost native connection ends recording and retains captured steps for retry", async ({ native }) => {
  const { page, agent } = native;
  await openMacro(page);
  await page.getByRole("button", { name: "Record", exact: true }).click();
  await expect.poll(() => agent.capturing).toBe(true);
  agent.key("KeyA", true); agent.key("KeyA", false);
  await expect(page.getByRole("option")).toHaveCount(4);
  const stable = await watchLayout(page, ['main', 'nav[aria-label="Workspace"]', 'header:has(h1)']);
  agent.disconnect();
  await expect(page.getByRole("button", { name: "Record", exact: true })).toBeVisible();
  await expect(page.getByRole("option")).toHaveCount(4);
  expect(agent.capturing).toBe(false);
  await page.getByRole("button", { name: "Retry saving macro", exact: true }).click();
  await expect.poll(() => agent.state.macros.find(m => m.id === "signoff")!.steps.length).toBe(4);
  await stable();
});

test("leaving an inline shortcut during capture startup releases capture", async ({ native }) => {
  const { page, agent } = native;
  await openMacro(page);
  const stable = await watchEditorLayout(page);
  await page.getByRole("option").first().click();
  const answer = agent.deferNextReply("startKeyCapture");
  try {
    await page.getByRole("button", { name: /^Step 1 keys:/ }).click();
    await expect.poll(() => agent.capturing).toBe(true);
    await page.getByRole("option").nth(2).click();
    await expect.poll(() => agent.capturing, { timeout: 1500 }).toBe(false);
    await expect(page.getByRole("textbox", { name: "Step text", exact: true })).toBeVisible();
    await stable();
  } finally { answer(); }
});

test("kept recording delays respect the remaining budget and can be toggled without shifts", async ({ native }) => {
  const { page, agent } = native;
  await openMacro(page);
  const stable = await watchEditorLayout(page);
  await page.getByRole("checkbox", { name: "Keep delays", exact: true }).check();
  await page.getByRole("button", { name: "Record", exact: true }).click();
  await expect.poll(() => agent.capturing).toBe(true);
  agent.key("KeyA", true); agent.key("KeyA", false);
  // Intentional time between synthetic chords: exercise the real recorder clock and 1,000 ms budget.
  await page.waitForTimeout(1200);
  agent.key("KeyB", true); agent.key("KeyB", false);
  await expect(page.getByRole("status")).toContainText("delays trimmed");
  await page.getByRole("checkbox", { name: "Keep delays", exact: true }).uncheck();
  await expect(page.getByRole("option")).toHaveCount(5);
  await page.getByRole("checkbox", { name: "Keep delays", exact: true }).check();
  await expect(page.getByRole("option")).toHaveCount(6);
  await page.getByRole("button", { name: "Stop", exact: true }).click();
  await expect.poll(() => agent.state.macros.find(m => m.id === "signoff")!.steps.length).toBe(6);
  expect(agent.state.macros.find(m => m.id === "signoff")!.steps.filter(s => s.type === "wait").reduce((sum, s) => sum + s.ms!, 0)).toBe(1000);
  await stable();
});

for (const colorScheme of ["light", "dark"] as const) {
  test(`recorder controls stay fixed with standard motion in ${colorScheme} appearance`, async ({ native }) => {
    const { page, agent } = native;
    await page.emulateMedia({ colorScheme, reducedMotion: "no-preference" });
    await openMacro(page);
    const stable = await watchEditorLayout(page);
    await page.getByRole("button", { name: "Record", exact: true }).click();
    await expect.poll(() => agent.capturing).toBe(true);
    agent.key("ControlLeft", true); agent.key("KeyK", true);
    agent.key("KeyK", false); agent.key("ControlLeft", false);
    await page.getByRole("button", { name: "Pause recording", exact: true }).click();
    await expect.poll(() => agent.capturing).toBe(false);
    await page.getByRole("button", { name: "Resume recording", exact: true }).click();
    await expect.poll(() => agent.capturing).toBe(true);
    agent.key("Escape", true);
    await expect.poll(() => agent.capturing).toBe(false);
    await expect.poll(() => agent.state.macros.find(m => m.id === "signoff")!.steps.length).toBe(4);
    await page.getByRole("option").first().click();
    await page.getByRole("button", { name: /^Step 1 keys:/ }).click();
    await expect.poll(() => agent.capturing).toBe(true);
    agent.key("Escape", true);
    await expect.poll(() => agent.capturing).toBe(false);
    expect(agent.state.macros.find(m => m.id === "signoff")!.steps[0]!.keys).toEqual(["ControlLeft", "End"]);
    await stable();
  });
}

test("native capture timeout accepts captured keys and restores the controls", async ({ native }) => {
  const { page, agent } = native;
  await openMacro(page);
  const stable = await watchEditorLayout(page);
  await page.getByRole("button", { name: "Record", exact: true }).click();
  await expect.poll(() => agent.capturing).toBe(true);
  agent.key("KeyA", true); agent.key("KeyA", false);
  await expect(page.getByRole("option")).toHaveCount(4);
  agent.expireCapture();
  await expect(page.getByRole("button", { name: "Record", exact: true })).toBeEnabled();
  await expect.poll(() => agent.state.macros.find(m => m.id === "signoff")!.steps.length).toBe(4);
  await expect(page.getByRole("button", { name: "Pause recording", exact: true })).toBeDisabled();
  await stable();
});

test("recorded rows keep their width when the step list starts scrolling", async ({ native }) => {
  const { page, agent } = native;
  await openMacro(page);
  const row = page.getByRole("option").first();
  const before = await row.boundingBox();
  await page.getByRole("button", { name: "Record", exact: true }).click();
  await expect.poll(() => agent.capturing).toBe(true);
  agent.key("KeyA", true); agent.key("KeyA", false);
  await expect(page.getByRole("option")).toHaveCount(4);
  expect((await row.boundingBox())!.width).toBe(before!.width);
  await page.getByRole("button", { name: "Stop", exact: true }).click();
  expect((await row.boundingBox())!.width).toBe(before!.width);
  await page.getByRole("button", { name: "Undo macro edit", exact: true }).click();
  await expect(page.getByRole("option")).toHaveCount(3);
  expect((await row.boundingBox())!.width).toBe(before!.width);
});

test("device disconnection releases capture and keeps the workspace anchored", async ({ native }) => {
  const { page, agent } = native;
  await openMacro(page);
  const stable = await watchLayout(page, ['main', 'nav[aria-label="Workspace"]', 'header:has(h1)']);
  await page.getByRole("button", { name: "Record", exact: true }).click();
  await expect.poll(() => agent.capturing).toBe(true);
  agent.key("KeyA", true); agent.key("KeyA", false);
  await expect(page.getByRole("option")).toHaveCount(4);
  agent.state.connected = false;
  agent.state.revision++;
  agent.emit("state", agent.state);
  await expect(page.getByRole("heading", { name: "Connect your MX Master", exact: true })).toBeVisible();
  await expect.poll(() => agent.capturing, { timeout: 1500 }).toBe(false);
  agent.state.connected = true;
  agent.state.revision++;
  agent.emit("state", agent.state);
  await expect(page.getByRole("button", { name: "Record", exact: true })).toBeVisible();
  await expect.poll(() => agent.state.macros.find(m => m.id === "signoff")!.steps.length).toBe(4);
  await stable();
});
