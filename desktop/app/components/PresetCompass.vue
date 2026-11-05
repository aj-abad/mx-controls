<template>
  <svg :width="size" :height="size" viewBox="0 0 56 56" role="img" :aria-label="label">
    <rect x="0.5" y="0.5" width="55" height="55" rx="10" class="fill-canvas stroke-line" vector-effect="non-scaling-stroke" />
    <path d="M28 6V50M6 28H50" class="stroke-line" stroke-width="1" vector-effect="non-scaling-stroke" />
    <path
      :d="drawPaths(preset, 28, step)"
      fill="none"
      :class="highlight?.length ? 'stroke-fill-strong' : 'stroke-accent'"
      :stroke-width="weight"
      vector-effect="non-scaling-stroke"
      stroke-linecap="round"
      stroke-linejoin="round" />
    <template v-if="highlight?.length">
      <path :d="drawPath(highlight, 28, step)" fill="none" class="stroke-accent" :stroke-width="weight" vector-effect="non-scaling-stroke" stroke-linecap="round" stroke-linejoin="round" />
      <circle :cx="end.x" :cy="end.y" :r="dot * 1.4" class="fill-accent" />
    </template>
    <circle cx="28" cy="28" :r="dot" class="fill-ink-3" />
  </svg>
</template>

<script setup lang="ts">
import type { Direction, GesturePreset } from "~/composables/useAgent";

/** Every path of a preset drawn from the centre, with one picked out. */
const { preset, size = 56, weight = 2.5, highlight = null } = defineProps<{
  preset: GesturePreset;
  size?: number;
  /** Line width in pixels, whatever the size. */
  weight?: number;
  highlight?: Direction[] | null;
}>();

// Steps are drawn as far apart as the furthest path lets them be while it stays inside the frame.
const step = computed(() => {
  const reach = Math.max(1, ...pathRows(preset).map(({ path }) => {
    const { x, y } = endOf(path, 0, 1);
    return Math.max(Math.abs(x), Math.abs(y));
  }));
  return 20 / reach;
});

// The centre dot stays about the same on screen at any size, as the lines do.
const dot = computed(() => Math.min(2.5, (4 * 56) / size));

const end = computed(() => endOf(highlight ?? [], 28, step.value));
const label = computed(() => (highlight?.length ? `${preset.name}, showing ${pathText(highlight)}` : `${preset.name}: ${countsText(preset)}`));
</script>
