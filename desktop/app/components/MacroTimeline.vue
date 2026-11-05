<template>
  <section aria-label="Macro timeline" class="flex h-43 shrink-0 flex-col border-y border-line bg-canvas/50">
    <div class="flex h-8 shrink-0 items-center gap-1 px-3">
      <span class="mr-2 text-xs font-medium text-ink-2">Timeline</span>
      <UiTooltip text="Zoom out"
        ><button
          type="button"
          aria-label="Zoom timeline out"
          :disabled="zoom <= 1"
          :class="TOOL"
          @click="setZoom(zoom / 2)"
        >
          <PhMinus class="size-3.5" /></button
      ></UiTooltip>
      <span class="w-9 text-center text-[11px] text-ink-3 tabular-nums">{{ zoom }}×</span>
      <UiTooltip text="Zoom in · Ctrl/⌘ + scroll"
        ><button
          type="button"
          aria-label="Zoom timeline in"
          :disabled="zoom >= 8"
          :class="TOOL"
          @click="setZoom(zoom * 2)"
        >
          <PhPlus class="size-3.5" /></button
      ></UiTooltip>
      <button
        type="button"
        class="ml-1 rounded px-1.5 py-1 text-[11px] text-ink-2 hover:bg-fill"
        @click="zoom = 1"
      >
        Fit
      </button>
      <span class="flex-1" />
      <span class="text-[11px] text-ink-3 tabular-nums">{{ Math.round(playhead) }} / {{ totalMs }} ms</span>
    </div>
    <div class="flex min-h-0 flex-1">
      <div class="w-14 shrink-0 border-r border-line pt-5 text-[11px] text-ink-3">
        <div v-for="lane in LANES" :key="lane.type" class="flex h-8 items-center px-2">{{ lane.label }}</div>
      </div>
      <UiScrollArea ref="scrollArea" orientation="horizontal" class="min-w-0 flex-1" content-class="h-full" :content-style="{ width: `${zoom * 100}%` }" :viewport-attrs="{ tabindex: -1 }" @wheel="onWheel">
        <div ref="track" class="relative h-full min-w-full">
          <div
            role="slider"
            aria-label="Timeline playhead"
            aria-valuemin="0"
            :aria-valuemax="MACRO_WAIT_LIMIT"
            :aria-valuenow="Math.round(playhead)"
            tabindex="0"
            class="relative h-5 cursor-crosshair border-b border-line text-[10px] text-ink-3 tabular-nums"
            @pointerdown="startSeek"
            @keydown="seekKey"
          >
            <span
              v-for="tick in ticks"
              :key="tick"
              class="absolute top-0.5 border-l border-line pl-1"
              :style="{ left: `${(tick / MACRO_WAIT_LIMIT) * 100}%` }"
              >{{ tick }}{{ tick ? " ms" : "" }}</span
            >
          </div>
          <div
            v-for="(lane, index) in LANES"
            :key="lane.type"
            class="pointer-events-none absolute inset-x-0 h-8 border-b border-line/40"
            :style="{ top: `${20 + index * 32}px` }"
          />
          <UiContextMenu v-for="gap in gaps" :key="`gap:${gap.index}`" :items="menu(gap.index)">
            <div
              class="group absolute top-5"
              :style="{ left: `${gap.at / MACRO_WAIT_LIMIT * 100}%`, width: `${gap.ms / MACRO_WAIT_LIMIT * 100}%`, height: `${LANES.length * 32}px` }"
              @contextmenu="emit('context', gap.index)"
            >
              <button
                type="button"
                :disabled="disabled"
                :aria-label="`Delay gap at step ${gap.index + 1}: ${gap.ms} ms`"
                :aria-pressed="selected.includes(gap.index)"
                :title="`${gap.ms} ms delay · select to edit`"
                class="absolute inset-0 z-0 cursor-default border-x hover:border-line hover:bg-fill/30 focus-visible:bg-accent/5 disabled:pointer-events-none"
                :class="selected.includes(gap.index) ? 'border-accent/25 bg-accent/5' : 'border-transparent'"
                @click="event => emit('select', gap.index, event)"
              />
              <span
                aria-hidden="true"
                class="pointer-events-none absolute top-1/2 left-1/2 max-w-[calc(100%_-_0.5rem)] -translate-x-1/2 -translate-y-1/2 truncate rounded bg-field/90 px-1.5 py-0.5 text-[11px] text-ink-3 group-hover:opacity-100 group-focus-within:opacity-100"
                :class="selected.includes(gap.index) ? 'opacity-100' : 'opacity-0'"
              >{{ gap.ms }} ms</span>
              <span
                role="slider"
                :aria-label="`Resize delay gap at step ${gap.index + 1}`"
                :aria-valuenow="gap.ms"
                aria-valuemin="1"
                :aria-valuemax="MACRO_WAIT_LIMIT"
                :aria-disabled="disabled"
                :tabindex="disabled ? -1 : 0"
                class="absolute inset-y-0 right-0 z-20 w-1.5 cursor-ew-resize border-r-2 border-accent group-hover:pointer-events-auto group-hover:opacity-100 group-focus-within:pointer-events-auto group-focus-within:opacity-100 focus-visible:pointer-events-auto focus-visible:opacity-100"
                :class="selected.includes(gap.index) && !disabled ? 'pointer-events-auto opacity-100' : 'pointer-events-none opacity-0'"
                @pointerdown.stop="event => startDrag(event, gap.index, true)"
                @keydown.stop="event => resizeKey(event, gap.index, gap.ms)"
              />
            </div>
          </UiContextMenu>
          <UiContextMenu v-for="group in groups" :key="group.key" :items="menu(group.indices[0]!)">
            <div
              class="absolute z-10 h-5 min-w-6 max-w-36"
              :style="{
                left: `${(group.at / MACRO_WAIT_LIMIT) * 100}%`,
                top: `${26 + group.lane * 32}px`,
                transform: group.at > 800 ? 'translateX(-100%)' : undefined,
              }"
              @contextmenu="emit('context', group.indices[0]!)"
            >
              <button
                type="button"
                :disabled="disabled"
                class="flex h-full w-full items-center truncate rounded border px-1.5 text-left text-[11px] leading-4 disabled:pointer-events-none"
                :class="[
                  selected.some((i) => group.indices.includes(i))
                    ? 'border-accent bg-accent/15 text-ink'
                    : 'border-line bg-field text-ink-2 hover:border-ink-3',
                ]"
                :aria-label="group.label"
                @click="(event) => !dragged && emit('select', group.indices[0]!, event)"
                @pointerdown="(event) => startDrag(event, group.indices[0]!, false)"
              >
                <span class="truncate">{{ group.label }}</span>
              </button>
            </div>
          </UiContextMenu>
          <div
            aria-hidden="true"
            class="pointer-events-none absolute top-0 bottom-0 z-10 w-px bg-accent"
            :style="{ left: `${((drag?.at ?? playhead) / MACRO_WAIT_LIMIT) * 100}%` }"
          >
            <span class="absolute -left-1 top-0 size-2 rotate-45 bg-accent" />
          </div>
          <p
            v-if="!steps.length"
            class="pointer-events-none absolute inset-x-0 top-13 text-center text-xs text-ink-3"
          >
            Record keys or insert a step to begin.
          </p>
        </div>
      </UiScrollArea>
    </div>
  </section>
</template>

<script setup lang="ts">
import { PhMinus, PhPlus } from "@phosphor-icons/vue";
import type { MacroStep } from "~/composables/useAgent";
import type { MenuEntry } from "~/utils/menu";

const {
  steps,
  selected,
  playhead,
  disabled = false,
  menu,
} = defineProps<{
  steps: MacroStep[];
  selected: number[];
  playhead: number;
  disabled?: boolean;
  menu: (index: number) => MenuEntry[];
}>();
const emit = defineEmits<{
  select: [index: number, modifiers: { shiftKey?: boolean; ctrlKey?: boolean; metaKey?: boolean }];
  context: [index: number];
  seek: [ms: number];
  retime: [index: number, ms: number];
  resize: [index: number, ms: number];
}>();
const TOOL = "grid size-6 place-items-center rounded text-ink-2 hover:bg-fill disabled:opacity-30";
const LANES = [
  { type: "keys", label: "Keys" },
  { type: "text", label: "Text" },
  { type: "click", label: "Mouse" },
];
const zoom = ref(1);
const track = useTemplateRef("track");
const scrollArea = useTemplateRef<{ viewport?: HTMLElement }>("scrollArea");
const scroller = computed(() => scrollArea.value?.viewport);
const entries = computed(() => macroEntries(steps));
const gaps = computed(() => entries.value.filter(entry => entry.step.type === "wait" && entry.ms > 0));
const totalMs = computed(() => entries.value.at(-1)?.end ?? 0);
const ticks = computed(() => Array.from({ length: 4 * zoom.value }, (_, i) => (i * 250) / zoom.value));
const groups = computed(() => {
  const result = new Map<
    string,
    { key: string; at: number; lane: number; indices: number[]; label: string }
  >();
  for (const entry of entries.value) {
    if (entry.step.type === "wait") continue;
    const lane = LANES.findIndex((l) => l.type === entry.step.type);
    const key = `${lane}:${entry.at}`;
    const group = result.get(key);
    if (group) {
      group.indices.push(entry.index);
      group.label = `${group.indices.length} ${{ keys: "key", text: "text", click: "mouse", wait: "delay" }[entry.step.type]} steps`;
    } else
      result.set(key, {
        key,
        at: entry.at,
        lane,
        indices: [entry.index],
        label: macroStepText(entry.step, keyLabel),
      });
  }
  return [...result.values()];
});
const drag = ref<{ index: number; startX: number; origin: number; at: number; resize: boolean } | null>(null);
const dragged = ref(false);
let detach: (() => void) | null = null;
const msAt = (clientX: number) => {
  const rect = track.value!.getBoundingClientRect();
  return Math.max(
    0,
    Math.min(MACRO_WAIT_LIMIT, Math.round((((clientX - rect.left) / rect.width) * MACRO_WAIT_LIMIT) / 5) * 5),
  );
};
function capture(event: PointerEvent, move: (event: PointerEvent) => void, end: (cancel: boolean) => void) {
  const target = event.currentTarget as HTMLElement;
  target.setPointerCapture(event.pointerId);
  const finish = (e: PointerEvent) => {
    const cancel = e.type !== "pointerup";
    cleanup();
    end(cancel);
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
function startSeek(event: PointerEvent) {
  if (event.button !== 0) return;
  event.preventDefault();
  emit("seek", msAt(event.clientX));
  capture(
    event,
    (e) => emit("seek", msAt(e.clientX)),
    () => {},
  );
}
function startDrag(event: PointerEvent, index: number, resize: boolean) {
  if (event.button !== 0 || disabled) return;
  event.preventDefault();
  dragged.value = false;
  const entry = entries.value[index]!;
  const origin = resize ? entry.end : entry.at;
  drag.value = { index, startX: event.clientX, origin, at: origin, resize };
  capture(
    event,
    (e) => {
      if (!drag.value || Math.abs(e.clientX - drag.value.startX) < 4) return;
      dragged.value = true;
      const delta =
        ((e.clientX - drag.value.startX) / track.value!.getBoundingClientRect().width) * MACRO_WAIT_LIMIT;
      drag.value.at = Math.max(0, Math.min(MACRO_WAIT_LIMIT, Math.round((origin + delta) / 5) * 5));
    },
    (cancel) => {
      const d = drag.value;
      drag.value = null;
      if (cancel || !d || !dragged.value) return;
      if (d.resize) emit("resize", index, Math.max(1, d.at - entry.at));
      else emit("retime", index, d.at);
      setTimeout(() => {
        dragged.value = false;
      }, 0);
    },
  );
}
function seekKey(event: KeyboardEvent) {
  if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
  event.preventDefault();
  const next =
    event.key === "Home"
      ? 0
      : event.key === "End"
        ? totalMs.value
        : playhead + (event.key === "ArrowRight" ? 1 : -1) * (event.shiftKey ? 25 : 5);
  emit("seek", Math.max(0, Math.min(MACRO_WAIT_LIMIT, next)));
}
function resizeKey(event: KeyboardEvent, index: number, ms: number) {
  if (!["ArrowLeft", "ArrowRight"].includes(event.key) || disabled) return;
  event.preventDefault();
  emit(
    "resize",
    index,
    Math.max(
      1,
      Math.min(MACRO_WAIT_LIMIT, ms + (event.key === "ArrowRight" ? 1 : -1) * (event.shiftKey ? 25 : 5)),
    ),
  );
}
function onWheel(event: WheelEvent) {
  if (!(event.ctrlKey || event.metaKey)) return;
  event.preventDefault();
  setZoom(zoom.value * (event.deltaY < 0 ? 2 : 0.5), event.clientX);
}
function setZoom(next: number, clientX?: number) {
  const scroll = scroller.value;
  if (!scroll) return;
  const offset =
    clientX === undefined ? scroll.clientWidth / 2 : clientX - scroll.getBoundingClientRect().left;
  const share = (scroll.scrollLeft + offset) / scroll.scrollWidth;
  zoom.value = Math.max(1, Math.min(8, next));
  void nextTick(() => {
    scroll.scrollLeft = share * scroll.scrollWidth - offset;
  });
}
onBeforeUnmount(() => detach?.());
</script>
