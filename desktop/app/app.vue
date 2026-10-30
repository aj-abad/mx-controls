<template>
  <!-- With Reduce Motion on, movement turns into fades; nothing that carries meaning is lost. -->
  <MotionConfig reduced-motion="user">
    <TooltipProvider :delay-duration="300">
      <NuxtLayout>
        <NuxtPage />
      </NuxtLayout>
    </TooltipProvider>
  </MotionConfig>
</template>

<script setup lang="ts">
import { MotionConfig } from "motion-v";
import { TooltipProvider } from "reka-ui";

// Phosphor's bold stroke at 16px matches the 1.5–1.6px strokes the UI was drawn with.
provide("weight", "bold");

useWindowDrag();

// The window is see-through over the system's backdrop; app.css makes the page's surfaces see-through to match.
if ("__TAURI_INTERNALS__" in window) document.documentElement.dataset.glass = "";

// The webview's own menu (Reload, Inspect) would read as a web page's. Fields keep theirs for cut, copy and
// paste, and a dev build keeps it everywhere for the inspector.
function blockNativeMenu(event: MouseEvent) {
  const target = event.target as HTMLElement | null;
  if (!import.meta.dev && !target?.closest("input, textarea, [contenteditable]")) event.preventDefault();
}
onMounted(() => window.addEventListener("contextmenu", blockNativeMenu));
onBeforeUnmount(() => window.removeEventListener("contextmenu", blockNativeMenu));
</script>
