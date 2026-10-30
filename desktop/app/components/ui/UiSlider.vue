<template>
  <div class="flex flex-col gap-3">
    <div class="flex items-baseline justify-between gap-4">
      <span :id="labelId" class="font-medium">{{ label }}</span>
      <span class="text-sm tabular-nums text-ink-2">{{ format ? format(value) : value }}</span>
    </div>
    <SliderRoot
      :model-value="[value]"
      :min="min"
      :max="max"
      :step="step"
      :aria-labelledby="labelId"
      class="relative flex h-5 w-full touch-none items-center"
      @update:model-value="(next) => next && (value = next[0]!)"
      @value-commit="(next) => emit('commit', next[0]!)">
      <SliderTrack class="relative h-1 grow rounded-full bg-fill-strong">
        <SliderRange class="absolute h-full rounded-full bg-ink" />
      </SliderTrack>
      <SliderThumb
        :aria-valuetext="format ? format(value) : undefined"
        class="block size-[18px] rounded-full bg-white shadow-[0_1px_4px_rgb(0_0_0/0.25)] ring-1 ring-black/10" />
    </SliderRoot>
    <p v-if="description" class="text-sm text-ink-3">{{ description }}</p>
  </div>
</template>

<script setup lang="ts">
import { SliderRange, SliderRoot, SliderThumb, SliderTrack } from "reka-ui";

const { step = 1 } = defineProps<{
  label: string;
  description?: string;
  min: number;
  max: number;
  step?: number;
  format?: (value: number) => string;
}>();
const value = defineModel<number>({ required: true });
/** The value once a drag or key press ends; save here rather than on every move. */
const emit = defineEmits<{ commit: [value: number] }>();

const labelId = useId();
</script>
