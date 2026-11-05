import { getCurrentWindow } from "@tauri-apps/api/window";

/** Anything that reacts to a press; the window moves only when none of these is under the pointer. */
const INTERACTIVE = "button, a, input, textarea, select, label, [role], [contenteditable], [tabindex], [data-dismissable-layer], .ui-scrollbar";

function onMouseDown(event: MouseEvent) {
  if (event.button !== 0 || event.defaultPrevented) return;
  const target = event.target as Element;
  if (target.closest(INTERACTIVE)) return;
  // A press on a scrollbar lands on the element that scrolls.
  if (
    target instanceof HTMLElement &&
    (event.offsetX > target.clientWidth || event.offsetY > target.clientHeight) &&
    (target.scrollHeight > target.clientHeight || target.scrollWidth > target.clientWidth)
  )
    return;
  void getCurrentWindow().startDragging();
}

/** The window has no system title bar, so it moves when dragged by any part that isn't a control. */
export function useWindowDrag() {
  if (!("__TAURI_INTERNALS__" in window)) return;
  onMounted(() => document.addEventListener("mousedown", onMouseDown));
  onBeforeUnmount(() => document.removeEventListener("mousedown", onMouseDown));
}
