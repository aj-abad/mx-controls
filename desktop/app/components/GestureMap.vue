<template>
  <div role="group" aria-label="Choose a gesture direction" class="gesture-map grid w-full grid-cols-3 gap-2">
    <button
      v-for="direction in DIRECTIONS"
      :key="direction"
      :ref="(element) => (buttons[direction] = element as HTMLButtonElement | null)"
      type="button"
      :aria-label="`Edit ${DIRECTION_LABELS[direction]}: ${summary(direction)}`"
      :title="`${summary(direction)} · ${movementHint(direction)}`"
      :aria-pressed="selected === direction"
      class="flex h-21 min-w-0 flex-col items-center justify-center gap-0.5 rounded-xl border px-1.5 py-1 text-center transition-colors"
      :class="[
        `gesture-map-${direction}`,
        selected === direction ? 'border-accent bg-accent/8' : 'border-line bg-field hover:border-ink-3',
      ]"
      @click="emit('select', direction)"
      @keydown="onKey">
      <span class="flex items-center gap-1 whitespace-nowrap text-[11px] font-semibold" :class="selected === direction ? 'text-accent' : 'text-ink-2'">
        <component :is="arrows[direction]" class="size-4 shrink-0" weight="bold" aria-hidden="true" />Move {{ direction }}
      </span>
      <span class="line-clamp-2 h-8 w-full shrink-0 text-xs leading-4" :class="preset[direction].type === 'none' && 'text-ink-3'">{{ summary(direction) }}</span>
      <span class="h-4 w-full shrink-0 truncate text-[11px] text-ink-3">{{ movementHint(direction) }}</span>
    </button>
    <div class="gesture-map-center flex flex-col items-center justify-center gap-1.5 text-center">
      <span class="grid size-10 place-items-center rounded-full bg-fill"><PhMouseSimple class="size-6 text-ink-2" /></span>
      <span class="text-[11px] font-medium text-ink-2">Hold button</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { PhArrowUp, PhArrowDown, PhArrowLeft, PhArrowRight, PhMouseSimple } from "@phosphor-icons/vue";
import type { Direction, GesturePreset } from "~/composables/useAgent";

const { preset, selected } = defineProps<{ preset: GesturePreset; selected: Direction | null }>();
const emit = defineEmits<{ select: [direction: Direction] }>();
const buttons: Partial<Record<Direction, HTMLButtonElement | null>> = {};
const arrows = { up: PhArrowUp, down: PhArrowDown, left: PhArrowLeft, right: PhArrowRight };
const ARROWS: Record<string, Direction> = { ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right' };
function onKey(event: KeyboardEvent) {
  const direction = ARROWS[event.key];
  if (!direction || event.altKey || event.ctrlKey || event.metaKey) return;
  event.preventDefault();
  emit('select', direction);
  buttons[direction]?.focus();
}
function movementHint(direction: Direction) {
  if (preset[direction].repeat) return "Repeats";
  if (preset[direction].type === "hold") return "Keys held";
  return preset[direction].type === "none" ? "Unassigned" : "On movement";
}
function summary(direction: Direction) {
  const action = preset[direction];
  if (!complete(action)) return "Choose an action";
  return action.type === "none" ? "No action" : describeAction(action);
}
</script>

<style scoped>
.gesture-map { grid-template-areas: ". up ." "left center right" ". down ."; }
.gesture-map-up { grid-area: up; }
.gesture-map-down { grid-area: down; }
.gesture-map-left { grid-area: left; }
.gesture-map-right { grid-area: right; }
.gesture-map-center { grid-area: center; }
</style>
