<template>
  <span
    class="grid shrink-0 place-items-center rounded-full font-semibold tabular-nums"
    :class="current ? 'bg-ink text-on-ink' : 'bg-fill-strong text-ink-2'">
    {{ index + 1 }}
  </span>
</template>

<script setup lang="ts">
/** An Easy-Switch channel's number, the way the mouse's lights count them. */
defineProps<{ index: number; current?: boolean }>();
</script>
