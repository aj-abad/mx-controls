export type Page = "buttons" | "gestures" | "macros";

const page = ref<Page>("buttons");
/** The gesture preset and macro open on their pages; each page opens its first when none is. */
const openPreset = ref<string | null>(null);
const openMacro = ref<string | null>(null);

/** Which of the window's pages shows, chosen in the header, and what's open on the Gestures and Macros pages. */
export function usePage() {
  function goTo(to: Page, id?: string | null) {
    page.value = to;
    if (to === "gestures" && id) openPreset.value = id;
    if (to === "macros" && id) openMacro.value = id;
  }
  return { page, openPreset, openMacro, goTo };
}
