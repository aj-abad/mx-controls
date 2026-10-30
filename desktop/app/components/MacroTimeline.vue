<template>
  <div class="flex flex-col gap-1" role="group" :aria-label="`Timeline, ${totalMs} of ${MACRO_MAX_MS} ms`">
    <div class="relative max-h-56 overflow-y-auto rounded-xl bg-canvas/60 px-3 py-2 ring-1 ring-line ring-inset">
      <span
        v-for="tick in TICKS"
        :key="tick"
        class="pointer-events-none absolute top-0 bottom-0 w-px bg-line"
        :style="{ left: `calc(0.75rem + (100% - 1.5rem) * ${tick / MACRO_MAX_MS})` }" />
      <p v-if="!rows.length" class="py-3 text-center text-xs text-ink-3">Steps show here at the moment they run.</p>
      <div v-for="row in rows" :key="row.index" class="relative h-6">
        <span
          v-if="row.wait"
          class="absolute top-2 h-2 rounded-full"
          :class="row.index === selected ? 'bg-accent' : 'bg-fill-strong'"
          :style="{ left: `${(row.at / MACRO_MAX_MS) * 100}%`, width: `${Math.max(0.6, (row.ms / MACRO_MAX_MS) * 100)}%` }" />
        <button
          type="button"
          class="absolute top-0.5 max-w-48 truncate rounded-md px-2 py-px text-left text-xs font-medium whitespace-nowrap"
          :class="[
            row.index === selected ? 'bg-accent text-[#141414]' : row.wait ? 'text-ink-3 hover:text-ink' : 'bg-fill-strong text-ink',
            row.flip && '-translate-x-full',
          ]"
          :style="{ left: `${((row.wait ? row.at + row.ms : row.at) / MACRO_MAX_MS) * 100}%` }"
          :aria-label="`Step ${row.index + 1}: ${row.label}`"
          @click="emit('select', row.index)">
          {{ row.label }}
        </button>
      </div>
    </div>
    <div class="relative mx-3 h-4 text-[10.5px] text-ink-3 tabular-nums">
      <span
        v-for="tick in TICKS"
        :key="tick"
        class="absolute"
        :class="tick === MACRO_MAX_MS ? '-translate-x-full' : tick > 0 && '-translate-x-1/2'"
        :style="{ left: `${(tick / MACRO_MAX_MS) * 100}%` }">
        {{ tick === 0 ? "0" : tick === MACRO_MAX_MS ? "1 s" : `${tick} ms` }}
      </span>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { MacroStep } from "~/composables/useAgent";

/**
 * A macro against its one second, one row a step like a Gantt chart: each step sits at the moment it runs, and
 * a wait is a bar from where it starts to where it ends.
 */
const { steps, selected = -1 } = defineProps<{ steps: MacroStep[]; selected?: number }>();
const emit = defineEmits<{ select: [index: number] }>();

const TICKS = [0, 250, 500, 750, 1000];
const MOUSE: Record<string, string> = { middle: "Middle click", back: "Back click", forward: "Forward click" };

function label(step: MacroStep) {
  if (step.type === "keys") return step.keys?.length ? step.keys.map(keyLabel).join(" + ") : "Keys…";
  if (step.type === "text") return step.text ? `“${step.text.replace(/\n/g, "⏎")}”` : "Text…";
  if (step.type === "click") return MOUSE[step.mouse ?? ""] ?? "Click";
  return `${step.ms ?? 0} ms`;
}

const rows = computed(() => {
  let at = 0;
  return steps.map((step, index) => {
    const ms = step.type === "wait" ? Math.max(0, step.ms ?? 0) : 0;
    const row = { index, at, ms, wait: step.type === "wait", label: label(step), flip: at + ms > MACRO_MAX_MS * 0.7 };
    at += ms;
    return row;
  });
});
const totalMs = computed(() => rows.value.reduce((total, row) => total + row.ms, 0));
</script>
