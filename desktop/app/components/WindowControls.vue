<template>
  <!-- The window's own minimize and close, drawn like the header's other buttons; macOS keeps its traffic lights.
       They dim while the window is in the background, as system title bars do. Like a system title bar's, they
       stay out of the tab order: the keyboard has Alt+F4 and Win+Down, and Tab shouldn't stop on Close on its way
       to the settings. Screen readers still reach them by name. -->
  <div
    class="flex items-center gap-1 transition-opacity duration-150"
    :class="!focused && 'opacity-50'">
    <button
      type="button"
      tabindex="-1"
      aria-label="Minimize"
      :class="BUTTON"
      class="hover:bg-fill hover:text-ink size-12"
      @click="minimize">
      <PhMinus class="size-4" />
    </button>
    <button
      type="button"
      tabindex="-1"
      aria-label="Close"
      :class="BUTTON"
      class="hover:bg-danger hover:text-white size-12"
      @click="close">
      <PhX class="size-4" />
    </button>
  </div>
</template>

<script setup lang="ts">
import { PhMinus, PhX } from "@phosphor-icons/vue";
import { getCurrentWindow } from "@tauri-apps/api/window";

const BUTTON = "grid size-8 place-items-center text-ink-2";

const inTauri = "__TAURI_INTERNALS__" in window;

const minimize = () => inTauri && void getCurrentWindow().minimize();
const close = () => inTauri && void getCurrentWindow().close();

const focused = ref(document.hasFocus());
const onFocus = () => (focused.value = true);
const onBlur = () => (focused.value = false);
onMounted(() => {
  window.addEventListener("focus", onFocus);
  window.addEventListener("blur", onBlur);
});
onBeforeUnmount(() => {
  window.removeEventListener("focus", onFocus);
  window.removeEventListener("blur", onBlur);
});
</script>
