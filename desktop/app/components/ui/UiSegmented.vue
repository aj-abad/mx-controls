<template>
  <!-- Reka lets a single toggle group deselect; ignoring an empty value keeps one segment chosen. -->
  <ToggleGroupRoot
    :model-value="value"
    type="single"
    :aria-label="label"
    class="relative flex rounded-full bg-fill p-1"
    @update:model-value="(next) => typeof next === 'string' && next && (value = next)">
    <!-- One thumb that slides to the chosen segment. The segments share the width equally, so it moves by
         its own width and never has to measure anything, which the zoomed stage would throw off. -->
    <motion.span
      aria-hidden="true"
      class="absolute inset-y-1 left-1 rounded-full bg-field shadow-sm"
      :style="{ width: `calc((100% - 0.5rem) / ${options.length})` }"
      :initial="false"
      :animate="{ x: `${Math.max(0, options.findIndex((o) => o.value === value)) * 100}%` }"
      :transition="FAST" />
    <ToggleGroupItem
      v-for="option in options"
      :key="option.value"
      :value="option.value"
      class="relative h-8 flex-1 rounded-full px-3 text-sm font-medium text-ink-2 transition-colors hover:text-ink data-[state=on]:text-ink">
      {{ option.label }}
    </ToggleGroupItem>
  </ToggleGroupRoot>
</template>

<script setup lang="ts">
import { motion } from "motion-v";
import { ToggleGroupItem, ToggleGroupRoot } from "reka-ui";

defineProps<{ options: { value: string; label: string }[]; label: string }>();
const value = defineModel<string>({ required: true });
</script>
