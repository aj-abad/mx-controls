<template>
  <!-- Phosphor's bold battery outline, with the level drawn inside it to scale. -->
  <svg viewBox="0 0 256 256" fill="currentColor" aria-hidden="true" class="text-ink-2">
    <path
      d="M196,52H28A28,28,0,0,0,0,80v96a28,28,0,0,0,28,28H196a28,28,0,0,0,28-28V80A28,28,0,0,0,196,52Zm4,124a4,4,0,0,1-4,4H28a4,4,0,0,1-4-4V80a4,4,0,0,1,4-4H196a4,4,0,0,1,4,4Zm56-72v48a12,12,0,0,1-24,0V104a12,12,0,0,1,24,0Z" />
    <rect
      x="36"
      y="88"
      height="80"
      rx="6"
      class="transition-[width,fill] duration-300 ease-out"
      :class="[FILLS[level], level === 'critical' && 'animate-battery-flash']"
      :style="{ width: `${width}px` }" />
  </svg>
</template>

<script setup lang="ts">
const { percent, charging = false } = defineProps<{ percent: number; charging?: boolean }>();

/** The inside of the outline, less a gap all round. */
const INNER_WIDTH = 152;
/** Even an empty battery keeps a sliver, so the red still shows. */
const MIN_WIDTH = 12;

const LOW = 20;
const CRITICAL = 10;

// Charging wins: a low battery that's filling up needs no warning.
const level = computed(() => {
  if (charging) return "charging";
  if (percent <= CRITICAL) return "critical";
  if (percent <= LOW) return "low";
  return "normal";
});

const FILLS = {
  charging: "fill-good",
  critical: "fill-danger",
  low: "fill-warn",
  normal: "fill-ink-2",
} as const;

const width = computed(() => Math.max(MIN_WIDTH, (INNER_WIDTH * Math.min(100, Math.max(0, percent))) / 100));
</script>
