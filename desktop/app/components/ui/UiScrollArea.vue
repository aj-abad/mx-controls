<template>
  <ScrollAreaRoot ref="root" type="auto" class="relative min-h-0 min-w-0 overflow-hidden">
    <SelectViewport v-if="selectViewport" as-child>
      <ScrollAreaViewport v-bind="viewportAttrs" as-child class="ui-scroll-viewport size-full rounded-[inherit]" :class="viewportClass">
        <div class="min-h-full min-w-full" :class="contentClass" :style="contentStyle"><slot /></div>
      </ScrollAreaViewport>
    </SelectViewport>
    <ScrollAreaViewport v-else v-bind="viewportAttrs" as-child class="ui-scroll-viewport size-full rounded-[inherit]" :class="viewportClass">
      <div class="min-h-full min-w-full" :class="contentClass" :style="contentStyle"><slot /></div>
    </ScrollAreaViewport>
    <ScrollAreaScrollbar v-if="orientation !== 'horizontal'" orientation="vertical" class="ui-scrollbar w-2 p-0.5">
      <ScrollAreaThumb class="ui-scroll-thumb relative flex-1 rounded-full bg-ink-3/50 hover:bg-ink-2/70" />
    </ScrollAreaScrollbar>
    <ScrollAreaScrollbar v-if="orientation !== 'vertical'" orientation="horizontal" class="ui-scrollbar h-2 flex-col p-0.5">
      <ScrollAreaThumb class="ui-scroll-thumb relative flex-1 rounded-full bg-ink-3/50 hover:bg-ink-2/70" />
    </ScrollAreaScrollbar>
    <ScrollAreaCorner v-if="orientation === 'both'" class="bg-transparent" />
  </ScrollAreaRoot>
</template>

<script setup lang="ts">
import { ScrollAreaRoot, ScrollAreaViewport, ScrollAreaScrollbar, ScrollAreaThumb, ScrollAreaCorner, SelectViewport } from "reka-ui";
import type { CSSProperties, HTMLAttributes } from "vue";

const {
  orientation = "vertical", selectViewport = false, viewportAttrs = {}, contentClass, contentStyle,
} = defineProps<{
  orientation?: "vertical" | "horizontal" | "both";
  /** Compose the two primitives on one viewport so Select keeps keyboard scrolling and focus management. */
  selectViewport?: boolean;
  viewportAttrs?: HTMLAttributes;
  contentClass?: string;
  contentStyle?: CSSProperties;
}>();
const root = useTemplateRef<InstanceType<typeof ScrollAreaRoot>>("root");
const viewport = computed(() => root.value?.viewport);
const viewportClass = computed(() => ({ 'pr-2': orientation !== "horizontal", 'pb-2': orientation !== "vertical" }));

// Expose the actual scroll element to timeline zooming, keyboard navigation and list drag auto-scrolling.
defineExpose({ viewport });
watchPostEffect((cleanup) => {
  const element = viewport.value;
  if (!element) return;
  const explicit = viewportAttrs.tabindex;
  const updateTabStop = () => {
    // Reka's viewport defaults to tabindex=0. Avoid an extra tab stop when no scrolling is possible.
    element.tabIndex = explicit !== undefined ? Number(explicit) :
      (element.scrollHeight > element.clientHeight || element.scrollWidth > element.clientWidth ? 0 : -1);
  };
  const observer = new ResizeObserver(updateTabStop);
  observer.observe(element);
  if (element.firstElementChild) observer.observe(element.firstElementChild);
  updateTabStop();
  cleanup(() => observer.disconnect());
});
</script>

<style scoped>
/* Select composes an extra component around the viewport, so these rules target primitive descendants. */
:deep(.ui-scroll-viewport) { max-height: inherit; }
:deep(.ui-scrollbar) { display: flex; touch-action: none; user-select: none; }
</style>
