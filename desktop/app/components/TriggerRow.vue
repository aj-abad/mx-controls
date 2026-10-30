<template>
  <div
    class="grid grid-cols-[8rem_minmax(0,1fr)] items-start gap-2 rounded-xl transition-colors duration-300"
    :class="[fired && 'bg-accent/10 ring-4 ring-accent/10', selected && 'bg-accent/8 ring-4 ring-accent/8']">
    <component
      :is="selectable ? 'button' : 'div'"
      :type="selectable ? 'button' : undefined"
      :aria-label="selectable ? selectLabel : undefined"
      class="flex min-h-10 min-w-0 flex-col justify-center rounded-lg text-left"
      :style="{ paddingLeft: `${indent * 12}px` }"
      @click="selectable && emit('select')">
      <span class="flex items-center gap-2 text-sm font-medium whitespace-nowrap" :class="muted && 'text-ink-3'">
        <span v-if="dots" class="flex w-5 justify-center gap-0.5" aria-hidden="true">
          <i v-for="n in dots" :key="n" class="size-1.5 rounded-full bg-ink-2" />
        </span>
        <span v-if="path?.length" class="flex gap-0.5" aria-hidden="true">
          <b
            v-for="(d, i) in path"
            :key="i"
            class="flex size-5 items-center justify-center rounded-md text-xs font-semibold"
            :class="i < path.length - 1 ? 'bg-fill text-ink-3' : 'bg-fill-strong'">
            {{ GLYPHS[d] }}
          </b>
        </span>
        {{ label }}
      </span>
      <span v-if="caption" class="text-[11px] text-ink-3">{{ caption }}</span>
    </component>
    <div class="min-w-0">
      <slot />
    </div>
  </div>
</template>

<script setup lang="ts">
import type { Direction } from "~/composables/useAgent";

/** One trigger and what it runs: a click count or a path on the left, its editor on the right. */
const {
  label,
  dots = 0,
  path,
  caption,
  indent = 0,
  muted = false,
  fired = false,
  selected = false,
  selectable = false,
  selectLabel,
} = defineProps<{
  label: string;
  /** 1, 2 or 3 for a single, double or triple click. */
  dots?: number;
  /** A path's arrows; the earlier steps are dimmed so the last reads as this row's own. */
  path?: Direction[];
  caption?: string;
  /** Steps beyond a first direction sit under it. */
  indent?: number;
  muted?: boolean;
  /** Lit while the mouse runs it. */
  fired?: boolean;
  selected?: boolean;
  /** The trigger itself can be clicked, to show it elsewhere. */
  selectable?: boolean;
  selectLabel?: string;
}>();
const emit = defineEmits<{ select: [] }>();
</script>
