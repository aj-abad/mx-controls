<template>
  <div :class="inline ? 'flex items-center gap-3' : 'flex flex-col gap-3'">
    <!-- Inline, the label and value flank the track on one line, so a list row can hold the slider. -->
    <div :class="inline ? 'contents' : 'flex items-baseline justify-between gap-4'">
      <span
        :id="labelId"
        :class="inline ? 'order-first shrink-0 text-sm font-medium' : 'font-medium'">
        {{ label }}
      </span>
      <span
        class="tabular-nums"
        :class="inline ? 'order-last w-16 shrink-0 text-right text-[13px] text-ink-2' : 'text-sm text-ink-2'">
        {{ format ? format(value) : value }}
      </span>
    </div>
    <!-- The default is marked on the track, so the copy around the slider doesn't have to state it. -->
    <div class="relative" :class="inline ? 'grow' : defaultValue !== undefined && 'pb-4'">
      <SliderRoot
        :model-value="[value]"
        :min="min"
        :max="max"
        :step="step"
        :disabled="disabled"
        :aria-labelledby="labelId"
        class="relative flex h-5 w-full touch-none items-center"
        @update:model-value="(next) => next && (value = next[0]!)"
        @value-commit="(next) => emit('commit', next[0]!)">
        <SliderTrack class="relative h-1 grow rounded-full bg-fill-strong">
          <SliderRange class="absolute h-full rounded-full bg-ink" />
        </SliderTrack>
        <span
          v-if="mark"
          aria-hidden="true"
          class="pointer-events-none absolute top-1/2 h-2.5 w-0.5 -translate-x-1/2 -translate-y-1/2 rounded-full bg-ink-3"
          :style="{ left: mark.left }" />
        <SliderThumb
          :aria-valuetext="valueText"
          class="block size-4.5 rounded-full bg-white shadow-[0_1px_4px_rgb(0_0_0/0.25)] ring-1 ring-black/10" />
      </SliderRoot>
    </div>
    <UiScrollArea v-if="description" class="h-10 text-sm text-ink-3">{{ description }}</UiScrollArea>
  </div>
</template>

<script setup lang="ts">
import { SliderRange, SliderRoot, SliderThumb, SliderTrack } from "reka-ui";

const {
  min,
  max,
  step = 1,
  defaultValue,
  format,
  disabled = false,
} = defineProps<{
  label: string;
  description?: string;
  min: number;
  max: number;
  step?: number;
  /** Marked on the track, and read out as "default" when the slider sits on it. */
  defaultValue?: number;
  format?: (value: number) => string;
  /** Label, track and value on one line. */
  inline?: boolean;
  disabled?: boolean;
}>();
const value = defineModel<number>({ required: true });
/** The value once a drag or key press ends; save here rather than on every move. */
const emit = defineEmits<{ commit: [value: number] }>();

const labelId = useId();

const mark = computed(() => {
  if (defaultValue === undefined) return null;
  const share = (defaultValue - min) / (max - min);
  // Near an end the label lines up with it rather than hanging off the slider.
  const shift = share < 0.08 ? "0" : share > 0.92 ? "-100%" : "-50%";
  return { left: `${share * 100}%`, shift };
});

const valueText = computed(() => {
  const text = format ? format(value.value) : String(value.value);
  return value.value === defaultValue ? `${text}, default` : text;
});
</script>
