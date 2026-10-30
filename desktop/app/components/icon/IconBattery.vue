<template>
  <svg viewBox="0 0 24 12" fill="none" aria-hidden="true">
    <rect x="0.75" y="0.75" width="20" height="10.5" rx="2.5" stroke="currentColor" stroke-width="1.5" />
    <rect x="22" y="4" width="1.75" height="4" rx="0.8" fill="currentColor" />
    <rect x="2.5" y="2.5" :width="Math.max(1, (16.5 * percent) / 100)" height="7" rx="1.2" fill="currentColor" />
  </svg>
</template>

<script setup lang="ts">
defineProps<{ percent: number }>();
</script>
