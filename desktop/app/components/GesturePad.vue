<template>
  <!-- Laid out the way the hand moves: click in the middle, each direction on its side. -->
  <div role="radiogroup" :aria-label="label" class="grid grid-cols-3 grid-rows-3 gap-1.5" @keydown="onKey">
    <button
      v-for="part in GESTURE_PARTS"
      :key="part.id"
      :ref="(el) => (cells[part.id] = el as HTMLButtonElement | null)"
      type="button"
      role="radio"
      :aria-checked="selected === part.id"
      :aria-label="`${part.label}: ${describeAction(gesture[part.id])}`"
      :tabindex="selected === part.id ? 0 : -1"
      class="flex h-16 min-w-0 cursor-pointer flex-col items-center justify-center gap-0.5 rounded-xl px-1.5 ring-inset transition-[background-color,box-shadow,color]"
      :class="[
        AREAS[part.id],
        selected === part.id ? 'bg-ink text-white ring-0' : 'bg-field ring-1 ring-line hover:ring-fill-strong',
        flashed === part.id && 'ring-2! ring-accent!',
      ]"
      @click="selected = part.id">
      <span class="text-sm leading-4 font-medium" :class="selected === part.id ? 'text-white/70' : 'text-ink-3'" aria-hidden="true">
        {{ part.glyph }}
      </span>
      <span
        class="w-full truncate text-center text-xs leading-4"
        :class="gesture[part.id].type === 'none' && selected !== part.id && 'text-ink-3'">
        {{ describeAction(gesture[part.id]) }}
      </span>
    </button>
  </div>
</template>

<script setup lang="ts">
import type { GestureActions, GestureEvent } from "~/composables/useAgent";

type Part = keyof GestureActions;

const { gesture, label, fired } = defineProps<{
  gesture: GestureActions;
  label: string;
  /** The last gesture this button made, so its cell can light up as it happens. */
  fired: GestureEvent | null;
}>();
const selected = defineModel<Part>({ required: true });

const AREAS: Record<Part, string> = {
  up: "col-start-2 row-start-1",
  left: "col-start-1 row-start-2",
  click: "col-start-2 row-start-2",
  right: "col-start-3 row-start-2",
  down: "col-start-2 row-start-3",
};

const cells: Partial<Record<Part, HTMLButtonElement | null>> = {};

// Arrow keys move the way the gesture does; from a side, the opposite arrow comes back to the middle.
const MOVES: Record<string, Partial<Record<Part, Part>>> = {
  ArrowUp: { click: "up", down: "click", left: "up", right: "up" },
  ArrowDown: { click: "down", up: "click", left: "down", right: "down" },
  ArrowLeft: { click: "left", right: "click", up: "left", down: "left" },
  ArrowRight: { click: "right", left: "click", up: "right", down: "right" },
};

function onKey(event: KeyboardEvent) {
  const next = MOVES[event.key]?.[selected.value];
  if (!next) return;
  event.preventDefault();
  selected.value = next;
  cells[next]?.focus();
}

const flashed = ref<Part | null>(null);
let clearFlash: ReturnType<typeof setTimeout> | undefined;
watch(
  () => fired,
  (gesture) => {
    if (!gesture) return;
    flashed.value = gesture.direction;
    clearTimeout(clearFlash);
    clearFlash = setTimeout(() => (flashed.value = null), 600);
  },
);
onBeforeUnmount(() => clearTimeout(clearFlash));
</script>
