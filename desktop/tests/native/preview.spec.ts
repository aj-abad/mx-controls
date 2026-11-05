import { test, expect } from "./fixture";
import { openMacro, watchEditorLayout } from "./layout";

test("preview starts at the scrubbed time and pauses without resetting the playhead", async ({ native }) => {
  const { page, agent } = native;
  agent.state.macros.find(m => m.id === "signoff")!.steps[1]!.ms = 500;
  agent.emit("state", agent.state);
  await openMacro(page);
  const stable = await watchEditorLayout(page);
  // Drive animation frames inside the hidden renderer without focusing or showing a native window.
  await page.clock.install({ time: 0 });
  await page.clock.pauseAt(1000);
  const playhead = page.getByRole("slider", { name: "Timeline playhead", exact: true });
  await playhead.focus();
  for (let i = 0; i < 5; i++) await playhead.press("Shift+ArrowRight");
  await expect(playhead).toHaveAttribute("aria-valuenow", "125");
  await page.getByRole("button", { name: "Preview macro", exact: true }).click();
  await page.clock.runFor(32);
  expect(Number(await playhead.getAttribute("aria-valuenow"))).toBeGreaterThan(125);
  expect(Number(await playhead.getAttribute("aria-valuenow"))).toBeLessThan(200);
  await page.getByRole("button", { name: "Pause macro preview", exact: true }).click();
  const pausedAt = await playhead.getAttribute("aria-valuenow");
  await page.clock.runFor(1000);
  await expect(playhead).toHaveAttribute("aria-valuenow", pausedAt!);
  await page.getByRole("button", { name: "Preview macro", exact: true }).click();
  await page.clock.runFor(1200);
  await expect(page.getByRole("button", { name: "Preview macro", exact: true })).toBeVisible();
  await expect(playhead).toHaveAttribute("aria-valuenow", "500");
  await stable();
});

test("switching to an identical sequence stops preview and resets the transport", async ({ native }) => {
  const { page, agent } = native;
  const copy = structuredClone(agent.state.macros.find(m => m.id === "signoff")!);
  copy.id = "same"; copy.name = "Same sequence";
  agent.state.macros.push(copy);
  agent.emit("state", agent.state);
  await openMacro(page);
  const stable = await watchEditorLayout(page);
  await page.clock.install({ time: 0 });
  await page.clock.pauseAt(1000);
  await page.getByRole("button", { name: "Preview macro", exact: true }).click();
  await page.clock.runFor(32);
  await page.getByRole("button", { name: /^Same sequence / }).click();
  await expect(page.getByRole("button", { name: "Preview macro", exact: true })).toBeVisible();
  await expect(page.getByRole("slider", { name: "Timeline playhead", exact: true })).toHaveAttribute("aria-valuenow", "0");
  await stable();
});
