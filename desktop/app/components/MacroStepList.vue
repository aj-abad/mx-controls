<template>
  <section class="flex min-h-0 min-w-0 flex-col" aria-label="Macro steps">
    <div class="flex h-9 shrink-0 items-center border-b border-line px-3 text-xs text-ink-3">
      <span class="flex-1 font-medium text-ink-2">Steps</span>
      <span>{{ selected.length ? `${selected.length} selected` : `${steps.length} total` }}</span>
    </div>
    <UiScrollArea
      ref="scrollArea"
      :viewport-attrs="{ role: 'listbox', 'aria-label': 'Macro step list', 'aria-multiselectable': true, tabindex: selected.length ? -1 : 0 }"
      content-class="p-1.5"
      class="relative min-h-0 flex-1"
    >
      <UiContextMenu v-for="entry in entries" :key="entry.index" :items="menu(entry.index)">
        <button
          type="button"
          role="option"
          :aria-selected="selected.includes(entry.index)"
          :tabindex="cursor === entry.index ? 0 : -1"
          :aria-label="`Step ${entry.index + 1}: ${macroStepText(entry.step, keyLabel)}`"
          class="relative flex h-9 w-full items-center gap-2 rounded-md px-2 text-left text-xs outline-none"
          :class="
            selected.includes(entry.index)
              ? 'bg-accent/12 ring-1 ring-accent/25 ring-inset'
              : playing === entry.index
                ? 'bg-fill-strong'
                : 'hover:bg-fill/60'
          "
          @click="(event) => !disabled && !skipClick && emit('select', entry.index, event)"
          @contextmenu="!disabled && emit('context', entry.index)"
          @dblclick="!disabled && emit('edit')"
          @pointerdown="(event) => startPointer(event, entry.index)"
        >
          <span
            v-if="dragging && boundary === entry.index"
            aria-hidden="true"
            class="absolute inset-x-0 top-0 h-0.5 bg-accent"
          />
          <PhDotsSixVertical class="size-3.5 shrink-0 text-ink-3" aria-hidden="true" />
          <span class="w-4 shrink-0 text-right text-ink-3 tabular-nums">{{ entry.index + 1 }}</span>
          <component :is="ICONS[entry.step.type]" class="size-3.5 shrink-0 text-ink-3" />
          <span class="min-w-0 flex-1 truncate">{{ macroStepText(entry.step, keyLabel) }}</span>
          <span class="shrink-0 text-[11px] text-ink-3 tabular-nums">{{ entry.at }} ms</span>
          <span
            v-if="dragging && boundary === entry.index + 1 && boundary === steps.length"
            aria-hidden="true"
            class="absolute inset-x-0 bottom-0 h-0.5 bg-accent"
          />
        </button>
      </UiContextMenu>
      <p
        v-if="!steps.length"
        class="grid h-full min-h-20 place-items-center px-5 text-center text-xs leading-relaxed text-ink-3"
      >
        Record a shortcut or add a step.<br />Drag steps to reorder them.
      </p>
    </UiScrollArea>
  </section>
</template>

<script setup lang="ts">
import { PhCursorClick, PhDotsSixVertical, PhHourglass, PhKeyboard, PhTextT } from "@phosphor-icons/vue";
import type { MacroStep } from "~/composables/useAgent";
import type { MenuEntry } from "~/utils/menu";
const {
  steps,
  selected,
  cursor = -1,
  playing = -1,
  disabled = false,
  menu,
} = defineProps<{
  steps: MacroStep[];
  selected: number[];
  cursor?: number;
  playing?: number;
  disabled?: boolean;
  menu: (index: number) => MenuEntry[];
}>();
const emit = defineEmits<{
  select: [index: number, modifiers: { shiftKey?: boolean; ctrlKey?: boolean; metaKey?: boolean }];
  context: [index: number];
  move: [boundary: number];
  edit: [];
}>();
const ICONS = { keys: PhKeyboard, text: PhTextT, wait: PhHourglass, click: PhCursorClick };
const entries = computed(() => macroEntries(steps));
const scrollArea = useTemplateRef<{ viewport?: HTMLElement }>("scrollArea");
const list = computed(() => scrollArea.value?.viewport);
watch(
  () => cursor,
  () => {
    const keepFocus = !!list.value?.contains(document.activeElement);
    void nextTick(() => {
      const row = list.value?.querySelector<HTMLElement>('[tabindex="0"]');
      row?.scrollIntoView({ block: "nearest" });
      if (keepFocus) row?.focus({ preventScroll: true });
    });
  },
);
const dragging = ref(false);
const boundary = ref(-1);
const skipClick = ref(false);
let detach: (() => void) | null = null;
function startPointer(event: PointerEvent, index: number) {
  if (event.button !== 0 || disabled) return;
  const target = event.currentTarget as HTMLElement;
  const startY = event.clientY;
  target.setPointerCapture(event.pointerId);
  const move = (e: PointerEvent) => {
    if (!list.value || (!dragging.value && Math.abs(e.clientY - startY) < 4)) return;
    if (!dragging.value && !selected.includes(index)) emit("context", index);
    dragging.value = true;
    skipClick.value = true;
    const rect = list.value.getBoundingClientRect();
    if (e.clientY < rect.top + 20) list.value.scrollTop -= 12;
    if (e.clientY > rect.bottom - 20) list.value.scrollTop += 12;
    const rows = [...list.value.querySelectorAll<HTMLElement>("[role=option]")];
    const before = rows.findIndex((row) => {
      const r = row.getBoundingClientRect();
      return e.clientY < r.top + r.height / 2;
    });
    boundary.value = before < 0 ? steps.length : before;
  };
  const finish = (e: PointerEvent) => {
    cleanup();
    if (e.type === "pointerup" && dragging.value && boundary.value >= 0) emit("move", boundary.value);
    dragging.value = false;
    boundary.value = -1;
    setTimeout(() => (skipClick.value = false), 0);
  };
  const cleanup = () => {
    target.removeEventListener("pointermove", move);
    target.removeEventListener("pointerup", finish);
    target.removeEventListener("pointercancel", finish);
    target.removeEventListener("lostpointercapture", finish);
    detach = null;
  };
  target.addEventListener("pointermove", move);
  target.addEventListener("pointerup", finish);
  target.addEventListener("pointercancel", finish);
  target.addEventListener("lostpointercapture", finish);
  detach = cleanup;
}
onBeforeUnmount(() => detach?.());
</script>
