<template>
  <svg width="64" height="64" viewBox="0 0 80 80" role="img" :aria-label="path.map((direction) => DIRECTION_LABELS[direction]).join(' then ')" class="shrink-0">
    <defs>
      <marker :id="arrowId" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="5" markerHeight="5" orient="auto-start-reverse">
        <path d="M2 1 8 5 2 9" fill="none" class="stroke-accent" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
      </marker>
    </defs>
    <path :d="line" fill="none" class="stroke-accent" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" :marker-end="`url(#${arrowId})`" />
    <circle :cx="points[0]!.x" :cy="points[0]!.y" r="4" class="fill-panel stroke-ink-2" stroke-width="1.5" />
    <circle v-for="(point, index) in points.slice(1, -1)" :key="index" :cx="point.x" :cy="point.y" r="2.5" class="fill-accent" />
  </svg>
</template>

<script setup lang="ts">
import type { Direction } from "~/composables/useAgent";

const { path } = defineProps<{ path: Direction[] }>();
const arrowId = `gesture-arrow-${useId()}`;
// Fit only this movement into a stable square; the open circle always marks the start.
const points = computed(() => {
  const raw = [{ x: 0, y: 0 }, ...path.map((_, index) => endOf(path.slice(0, index + 1), 0, 1))];
  const xs = raw.map((point) => point.x), ys = raw.map((point) => point.y);
  const minX = Math.min(...xs), maxX = Math.max(...xs), minY = Math.min(...ys), maxY = Math.max(...ys);
  const scale = Math.min(32, 48 / Math.max(maxX - minX, maxY - minY, 1));
  return raw.map((point) => ({ x: 40 + (point.x - (minX + maxX) / 2) * scale, y: 40 + (point.y - (minY + maxY) / 2) * scale }));
});
const line = computed(() => points.value.map((point, index) => `${index ? 'L' : 'M'}${point.x} ${point.y}`).join(' '));
</script>
