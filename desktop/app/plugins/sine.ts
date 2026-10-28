import { setLinkComponent } from "@arcon.mobi/sine/utils/link";
import { NuxtLink } from "#components";

export default defineNuxtPlugin(() => {
  setLinkComponent(NuxtLink);

  // Sine themes by `data-theme`; follow the system appearance.
  const dark = window.matchMedia("(prefers-color-scheme: dark)");
  const apply = () => (document.documentElement.dataset.theme = dark.matches ? "dark" : "light");
  apply();
  dark.addEventListener("change", apply);
});
