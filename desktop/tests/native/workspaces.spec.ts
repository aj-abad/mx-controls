import { test, expect } from "./fixture";
import { watchLayout } from "./layout";

test("profile switching, renaming, and device popovers keep the surrounding workspace fixed", async ({ native }) => {
  const { page, agent } = native;
  await page.getByRole("button", { name: "Profiles", exact: true }).click();
  const stable = await watchLayout(page, ['nav[aria-label="Workspace"]', 'main', 'header']);
  await page.getByRole("button", { name: "Work", exact: true }).click();
  await expect.poll(() => agent.state.activeProfile).toBe("work");
  await page.getByRole("button", { name: "Default", exact: true }).click();
  await expect.poll(() => agent.state.activeProfile).toBe("default");
  await page.getByRole("button", { name: "Edit active profile", exact: true }).click();
  const name = page.getByRole("textbox", { name: "Name", exact: true });
  await name.fill("A profile with a longer descriptive name");
  await name.press("Enter");
  await expect.poll(() => agent.state.profiles[0]!.name).toBe("A profile with a longer descriptive name");
  await page.keyboard.press("Escape");
  await page.getByRole("button", { name: /^MX Master 4 \(simulated\)/ }).click();
  await page.getByRole("switch").click();
  await expect.poll(() => agent.state.startAtLogin).toBe(true);
  await page.keyboard.press("Escape");
  // The profile chips deliberately resize with their names; the app frame stays put.
  await stable();
});

test("gesture selection, sequence creation, and action menus keep directions anchored", async ({ native }) => {
  const { page, agent } = native;
  await page.getByRole("button", { name: /^Gestures/ }).click();
  const stable = await watchLayout(page, [
    'main', 'nav[aria-label="Workspace"]', '[aria-label="Gesture presets"]',
    '[aria-label="Choose a gesture direction"]', '[aria-label="Choose a gesture direction"] > button',
    '.gesture-workspace', '.gesture-workspace > div', 'footer',
  ]);
  await page.getByRole("button", { name: /^Edit Up:/ }).focus();
  await page.keyboard.press("ArrowRight");
  await expect(page.getByRole("button", { name: /^Edit Right:/ })).toBeFocused();
  await page.keyboard.press("ArrowUp");
  await page.getByRole("button", { name: "Add another movement", exact: true }).click();
  await page.getByRole("button", { name: /^↑ Continue up$/ }).click();
  const movement = page.getByRole("region", { name: "Selected movement", exact: true });
  await expect(movement.getByText("Hold the button, move up, then continue up.", { exact: true })).toBeVisible();
  await expect(movement.getByRole("checkbox", { name: "Repeat with movement", exact: true })).toHaveCount(0);
  await movement.getByRole("button", { name: /^Window tiling .*: Choose an action$/ }).click();
  await page.getByRole("textbox", { name: /search actions$/ }).fill("Do nothing");
  await page.getByRole("option", { name: "Do nothing", exact: true }).click();
  await expect.poll(() => !!agent.state.gesturePresets[0]!.up.up).toBe(true);
  await page.getByRole("button", { name: /^Edit Up:/ }).click();
  await movement.getByRole("button", { name: /^Window tiling .*:/ }).first().click();
  await page.getByRole("textbox", { name: /search actions$/ }).fill("Do nothing");
  await page.getByRole("option", { name: "Do nothing", exact: true }).click();
  await expect.poll(() => agent.state.gesturePresets[0]!.up.type).toBe("none");
  await stable();
});
