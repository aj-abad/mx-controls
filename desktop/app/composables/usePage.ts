export type Page = "profiles" | "gestures" | "roses" | "macros";

const page = ref<Page>("profiles");
/** The gesture preset, rose menu and macro open on their pages; each page opens its first when none is. */
const openPreset = ref<string | null>(null);
const openRose = ref<string | null>(null);
const openMacro = ref<string | null>(null);

/** The profile workspace and shared libraries, with each library's current selection. */
export function usePage() {
  function goTo(to: Page, id?: string | null) {
    page.value = to;
    if (to === "gestures" && id) openPreset.value = id;
    if (to === "roses" && id) openRose.value = id;
    if (to === "macros" && id) openMacro.value = id;
  }
  return { page, openPreset, openRose, openMacro, goTo };
}
