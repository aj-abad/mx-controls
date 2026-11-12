<template>
  <section aria-label="Selected movement" class="flex min-w-0 flex-col gap-4">
    <div class="flex h-22 shrink-0 items-center gap-3 rounded-xl bg-fill/50 px-4" aria-label="Movement guide">
      <span class="grid size-12 shrink-0 place-items-center rounded-full bg-panel ring-1 ring-line">
        <component :is="ARROWS[direction]" class="size-6 text-accent" weight="bold" aria-hidden="true" />
      </span>
      <div class="min-w-0 flex-1">
        <h3 class="text-sm font-semibold">Move {{ direction }}</h3>
        <p class="mt-1 line-clamp-2 text-xs leading-4 text-ink-3">Hold the button, then move {{ direction }}.</p>
      </div>
    </div>

    <div ref="actionField" class="flex flex-col gap-2">
      <h3 class="text-xs font-medium text-ink-2">Action</h3>
      <ActionEditor
        :key="direction"
        :model-value="preset[direction]"
        :label="`${preset.name} ${DIRECTION_LABELS[direction]}`"
        repeat-label="Repeat with movement"
        layers="toggle"
        profiles
        @update:model-value="(action) => emit('change', direction, action)"
      />
      <UiScrollArea class="h-20 rounded-lg bg-fill/50 text-xs leading-relaxed text-ink-3" content-class="px-3 py-2" aria-label="Movement behavior">
        <p class="mb-1 font-medium text-ink-2">{{ behaviorTitle }}</p>
        <p>{{ behavior }}</p>
      </UiScrollArea>
    </div>
    <p class="text-xs leading-relaxed text-ink-3">
      For more than four actions on one button, use a rose menu: its folders can nest as deep as you like.
    </p>
  </section>
</template>

<script setup lang="ts">
import { PhArrowDown, PhArrowLeft, PhArrowRight, PhArrowUp } from "@phosphor-icons/vue";
import type { ButtonAction, Direction, GesturePreset } from "~/composables/useAgent";

const { preset, direction } = defineProps<{ preset: GesturePreset; direction: Direction }>();
const emit = defineEmits<{ change: [direction: Direction, action: ButtonAction] }>();
const ARROWS = { up: PhArrowUp, down: PhArrowDown, left: PhArrowLeft, right: PhArrowRight };
const actionField = useTemplateRef("actionField");
defineExpose({ focusAction: () => actionField.value?.querySelector("button")?.focus() });

const behaviorTitle = computed(() => {
  const action = preset[direction];
  if (action.type === "none") return "No action assigned";
  if (action.repeat) return "Repeats with movement";
  if (action.type === "hold") return "Keys stay held";
  return "Runs as you move";
});
const behavior = computed(() => {
  const action = preset[direction];
  if (action.type === "none") return "No action is assigned to this direction yet.";
  if (action.type === "hold")
    return "Starts after a 2 mm movement. The key stays down until you release the button or change direction.";
  return action.repeat
    ? "Runs after a 2 mm movement, then repeats every 4 mm as you keep moving."
    : "Runs as soon as you move 2 mm in this direction. You can release the button straight away.";
});
</script>
