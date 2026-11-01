<template>
  <section class="flex min-h-0 flex-col border-l border-line" aria-label="Step inspector">
    <h3 class="flex h-9 shrink-0 items-center border-b border-line px-3 text-xs font-medium text-ink-2">
      {{ selected.length === 1 ? `Step ${index + 1} · ${LABELS[step!.type]}` : "Inspector" }}
    </h3>
    <UiScrollArea
      ref="fields"
      class="min-h-0 flex-1"
      content-class="p-3"
      :class="disabled && 'pointer-events-none opacity-50'"
      :inert="disabled || undefined"
    >
      <template v-if="step && selected.length === 1">
        <label class="mb-2 block text-xs text-ink-2">{{ LABELS[step.type] }}</label>
        <ShortcutCapture
          v-if="step.type === 'keys'"
          :model-value="step.keys"
          :label="`Step ${index + 1} keys`"
          @update:model-value="(keys) => emit('update', index, { ...step!, keys })"
        />
        <textarea
          v-else-if="step.type === 'text'"
          :value="step.text ?? ''"
          aria-label="Step text"
          rows="3"
          :maxlength="MACRO_TEXT_LIMIT"
          class="h-24 w-full resize-none rounded-lg border border-line bg-field p-2 text-xs outline-none focus:border-accent"
          placeholder="Text to type"
          @change="
            (event) => emit('update', index, { ...step!, text: (event.target as HTMLTextAreaElement).value })
          "
        />
        <template v-else-if="step.type === 'wait'">
          <label class="flex items-center gap-2 text-xs text-ink-3">
            <input
              type="number"
              aria-label="Delay in milliseconds"
              :value="step.ms ?? 0"
              min="1"
              :max="MACRO_WAIT_LIMIT"
              step="5"
              class="h-9 min-w-0 flex-1 rounded-lg border border-line bg-field px-2 text-sm text-ink tabular-nums outline-none focus:border-accent"
              @change="
                (event) =>
                  emit('update', index, {
                    ...step!,
                    ms: Math.round(Number((event.target as HTMLInputElement).value)),
                  })
              "
            />
            ms
          </label>
          <div class="mt-2 flex gap-1">
            <button
              v-for="ms in [50, 100, 250]"
              :key="ms"
              type="button"
              class="rounded border border-line px-2 py-1 text-[11px] text-ink-2 hover:bg-fill"
              @click="emit('update', index, { ...step!, ms })"
            >
              {{ ms }} ms
            </button>
          </div>
        </template>
        <UiSelect
          v-else
          :model-value="step.mouse ?? 'middle'"
          :options="MOUSE"
          label="Mouse button"
          @update:model-value="(mouse) => emit('update', index, { ...step!, mouse })"
        />
        <p class="mt-3 text-[11px] leading-relaxed text-ink-3">{{ HELP[step.type] }}</p>
      </template>
      <template v-else-if="selected.length > 1">
        <p class="text-xs font-medium">{{ selected.length }} steps selected</p>
        <p class="mt-2 text-xs leading-relaxed text-ink-3">
          Move, duplicate, cut or delete these steps together. Ctrl/⌘-click toggles a step; Shift-click
          selects a range.
        </p>
        <div class="mt-3 flex flex-wrap gap-1.5">
          <button
            v-for="factor in [0.5, 2]"
            :key="factor"
            type="button"
            class="rounded border border-line px-2 py-1.5 text-xs hover:bg-fill"
            @click="emit('scale', factor)"
          >
            {{ factor }}× delays
          </button>
        </div>
      </template>
      <p v-else class="text-xs leading-relaxed text-ink-3">
        Select a step in the timeline or list to edit it. Double-click a row to focus its value.
      </p>
    </UiScrollArea>
  </section>
</template>

<script setup lang="ts">
import type { MacroStep } from "~/composables/useAgent";
const {
  steps,
  selected,
  disabled = false,
} = defineProps<{ steps: MacroStep[]; selected: number[]; disabled?: boolean }>();
const emit = defineEmits<{ update: [index: number, step: MacroStep]; scale: [factor: number] }>();
const index = computed(() => selected.at(-1) ?? -1);
const step = computed(() => steps[index.value]);
const LABELS = { keys: "Keys", text: "Text", wait: "Delay", click: "Mouse click" };
const HELP = {
  keys: "Keys are tapped together once. Recording a key combination leaves the rest of the editor in place.",
  text: "Types this text exactly as written, including line breaks.",
  wait: "Delays share a total budget of 1,000 ms. Select a gap in the timeline to drag its right edge.",
  click: "Clicks the selected mouse button at the pointer’s current location.",
};
const MOUSE = [
  { value: "middle", label: "Middle click" },
  { value: "back", label: "Back click" },
  { value: "forward", label: "Forward click" },
];
const fields = useTemplateRef<{ viewport?: HTMLElement }>("fields");
defineExpose({ focus: () => fields.value?.viewport?.querySelector<HTMLElement>("input, textarea, button")?.focus() });
</script>
