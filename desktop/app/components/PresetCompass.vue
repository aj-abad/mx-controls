<template>
  <svg :width="size" :height="size" viewBox="0 0 56 56" role="img" :aria-label="label">
    <rect x="0.5" y="0.5" width="55" height="55" rx="10" class="fill-canvas stroke-line" vector-effect="non-scaling-stroke" />
    <path d="M28 6V50M6 28H50" class="stroke-line" stroke-width="1" vector-effect="non-scaling-stroke" />
    <path
      v-for="direction in set"
      :key="direction"
      :d="arm(direction)"
      fill="none"
      :class="highlight && highlight !== direction ? 'stroke-fill-strong' : 'stroke-accent'"
      :stroke-width="weight"
      vector-effect="non-scaling-stroke"
      stroke-linecap="round" />
    <circle v-if="highlight" :cx="end(highlight).x" :cy="end(highlight).y" :r="dot * 1.4" class="fill-accent" />
    <circle cx="28" cy="28" :r="dot" class="fill-ink-3" />
  </svg>
</template>

<script setup lang="ts">
import type { Direction, GesturePreset } from "~/composables/useAgent";

/** The directions of a preset that do something, drawn from the centre, with one picked out. */
const { preset, size = 56, weight = 2.5, highlight = null } = defineProps<{
  preset: GesturePreset;
  size?: number;
  /** Line width in pixels, whatever the size. */
  weight?: number;
  highlight?: Direction | null;
}>();

const VECTORS: Record<Direction, [number, number]> = { up: [0, -1], down: [0, 1], left: [-1, 0], right: [1, 0] };
const set = computed(() => DIRECTIONS.filter((d) => preset[d].type !== "none"));
const end = (d: Direction) => ({ x: 28 + VECTORS[d][0] * 18, y: 28 + VECTORS[d][1] * 18 });
const arm = (d: Direction) => `M28 28L${end(d).x} ${end(d).y}`;

// The centre dot stays about the same on screen at any size, as the lines do.
const dot = computed(() => Math.min(2.5, (4 * 56) / size));

const label = computed(() =>
  highlight ? `${preset.name}, showing ${DIRECTION_LABELS[highlight]}` : `${preset.name}: ${countsText(preset)}`,
);
</script>
