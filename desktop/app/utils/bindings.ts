import type { ButtonBinding } from "~/composables/useAgent";

/**
 * "Copy · 2× Task View · Window tiling": the click, what extra clicks run where there's room, then what it does
 * while held. A button following the base profile says so.
 */
export function describeBinding(binding: ButtonBinding): string {
  const state = useAgent().state.value;
  if (binding.mode === "inherit")
    return `Same as ${state?.profiles.find((p) => p.id === state.baseProfile)?.name ?? "Default"}`;
  if (binding.mode === "rose") {
    const menu = state?.roseMenus.find((m) => m.id === binding.menu);
    return menu ? `Rose · ${menu.name}` : "Rose · deleted menu";
  }
  const double = hasAction(binding.doubleClick) ? binding.doubleClick : null;
  const triple = hasAction(binding.tripleClick) ? binding.tripleClick : null;
  const gesture =
    binding.mode === "gesture" ? (state?.gesturePresets.find((p) => p.id === binding.preset)?.name ?? "Gestures") : null;
  const clicks = triple ? ["2×", "3×"] : double ? [gesture ? "2×" : `2× ${describeAction(double)}`] : [];
  const click = binding.action ?? binding.gesture?.click ?? NOTHING;
  return [describeAction(click), ...clicks, gesture].filter(Boolean).join(" · ");
}
