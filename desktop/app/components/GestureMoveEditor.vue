<template>
  <section v-if="row" aria-label="Selected movement" class="flex min-w-0 flex-col gap-4">
    <div class="flex h-22 shrink-0 items-center gap-2 rounded-xl bg-fill/50 px-2" aria-label="Movement guide">
      <GestureDirectionPreview :path="path" />
      <div class="min-w-0 flex-1">
        <div class="flex h-7 items-center">
          <h3 v-if="path.length === 1" class="text-sm font-semibold">Move {{ path[0] }}</h3>
          <UiScrollArea v-else orientation="horizontal" class="h-7 w-full" content-class="flex h-5 items-center gap-1 whitespace-nowrap" :viewport-attrs="{ role: 'navigation', 'aria-label': 'Gesture path', tabindex: -1 }">
            <template v-for="(direction, index) in path" :key="index">
              <PhCaretRight v-if="index" class="size-3 text-ink-3" />
              <button
                type="button"
                class="shrink-0 rounded-md px-1.5 py-0.5 text-xs font-medium"
                :class="index === path.length - 1 ? 'bg-accent/10 text-ink' : 'text-ink-3 hover:bg-fill hover:text-ink'"
                :aria-current="index === path.length - 1 ? 'step' : undefined"
                @click="emit('select', path.slice(0, index + 1))"
              >
                {{ GLYPHS[direction] }} {{ DIRECTION_LABELS[direction] }}
              </button>
            </template>
          </UiScrollArea>
        </div>
        <p class="mt-1 line-clamp-2 text-xs leading-4 text-ink-3" :title="movementGuide">{{ movementGuide }}</p>
      </div>
    </div>

    <div ref="actionField" class="flex flex-col gap-2">
      <h3 class="text-xs font-medium text-ink-2">{{ row.leadsOn ? "Action if you stop here" : "Action" }}</h3>
      <ActionEditor
        :key="pathKey(path)"
        :model-value="actionOf(row.step)"
        :label="`${preset.name} ${pathText(path)}`"
        :allow-hold="row.instant"
        :allow-repeat="row.repeatable"
        repeat-label="Repeat with movement"
        layers="toggle"
        profiles
        @update:model-value="(action) => emit('change', path, action)"
      />
      <UiScrollArea class="h-20 rounded-lg bg-fill/50 text-xs leading-relaxed text-ink-3" content-class="px-3 py-2" aria-label="Movement behavior">
        <p class="mb-1 font-medium text-ink-2">{{ behaviorTitle }}</p>
        <p>{{ behavior }}</p>
      </UiScrollArea>
    </div>

    <div class="flex h-8 items-center gap-2">
      <PopoverRoot v-model:open="adding">
        <PopoverTrigger
          class="self-start rounded-md border border-line px-2.5 py-2 text-xs font-medium text-ink-2 hover:bg-fill"
          >Add another movement</PopoverTrigger
        >
        <UiPopoverContent :open="adding" class="w-72 p-4">
          <p class="mt-2 text-xs leading-relaxed text-ink-3">
            Continue in the same direction or turn. A sequence runs when you release the button.
          </p>
          <div v-if="extensions.length" class="mt-2 flex flex-wrap gap-1.5">
            <button
              v-for="extension in extensions"
              :key="extension.direction"
              type="button"
              class="rounded-lg border border-line px-2.5 py-2 text-xs hover:bg-fill"
              @click="
                adding = false;
                emit('extend', extension.path);
              "
            >
              {{ GLYPHS[extension.direction] }} {{ movementLabel(extension.direction) }}
            </button>
          </div>
          <p v-else class="mt-2 text-xs text-ink-3">
            This step has all the movements it can support. Select another step to extend it.
          </p>
        </UiPopoverContent>
      </PopoverRoot>

      <UiTooltip :text="row.leadsOn ? 'Remove this step and every step after it' : 'Remove this step'">
        <button
          type="button"
          :disabled="path.length === 1"
          class="flex h-8 items-center gap-1.5 text-xs text-ink-3 hover:text-danger disabled:invisible"
          @click="emit('remove', path)"
        >
          <PhTrash class="size-3.5" /> Remove step
        </button>
      </UiTooltip>
    </div>
    <div v-if="children.length" class="flex flex-col gap-1.5">
      <h3 class="text-xs font-medium text-ink-2">Next movement</h3>
      <button
        v-for="child in children"
        :key="pathKey(child.path)"
        type="button"
        class="flex items-center gap-2 rounded-lg border border-line bg-field px-3 py-2 text-left text-xs hover:bg-fill/50"
        @click="emit('select', child.path)"
      >
        <span class="text-base text-accent">{{ GLYPHS[child.path.at(-1)!] }}</span>
        <span class="min-w-0 flex-1"
          ><span class="block font-medium">{{ movementLabel(child.path.at(-1)!) }}</span
          ><span class="mt-0.5 block truncate text-ink-3">{{
            complete(actionOf(child.step)) ? describeAction(actionOf(child.step)) : "Choose an action"
          }}</span></span
        >
        <PhCaretRight class="size-3.5 text-ink-3" />
      </button>
    </div>
  </section>
</template>

<script setup lang="ts">
import { PhCaretRight, PhTrash } from "@phosphor-icons/vue";
import { PopoverRoot, PopoverTrigger } from "reka-ui";
import type { ButtonAction, Direction, GesturePreset } from "~/composables/useAgent";

const { preset, path } = defineProps<{ preset: GesturePreset; path: Direction[] }>();
const emit = defineEmits<{
  select: [path: Direction[]];
  change: [path: Direction[], action: ButtonAction];
  extend: [path: Direction[]];
  remove: [path: Direction[]];
}>();
const actionField = useTemplateRef("actionField");
const adding = ref(false);
watch(
  () => pathKey(path),
  () => (adding.value = false),
);
defineExpose({ focusAction: () => actionField.value?.querySelector("button")?.focus() });
const rows = computed(() => pathRows(preset));
const row = computed(() => rows.value.find((item) => pathKey(item.path) === pathKey(path)));
const children = computed(() =>
  rows.value.filter(
    (item) => item.path.length === path.length + 1 && path.every((d, i) => item.path[i] === d),
  ),
);
const extensions = computed(() =>
  DIRECTIONS.flatMap((direction) => {
    const next = [...path, direction];
    return shapeProblem(next) || fitProblem(preset, next) ? [] : [{ direction, path: next }];
  }),
);
const movementLabel = (direction: Direction) =>
  `${direction === path.at(-1) ? "Continue" : "Turn"} ${direction}`;
const movementGuide = computed(() => {
  const moves = path.map((direction, index) => index === 0 ? `move ${direction}` : `${direction === path[index - 1] ? 'continue' : 'turn'} ${direction}`);
  return `Hold the button, ${moves.join(', then ')}.`;
});
const behaviorTitle = computed(() => {
  if (row.value?.step.type === "none") return row.value.leadsOn ? "Continue to another movement" : "No action assigned";
  if (row.value?.step.repeat) return "Repeats with movement";
  if (!row.value?.instant) return "Runs on release";
  if (row.value.step.type === "hold") return "Keys stay held";
  return row.value.step.repeat ? "Repeats with movement" : "Runs as you move";
});
const behavior = computed(() => {
  if (!row.value) return "";
  if (!row.value.instant && row.value.step.repeat && row.value.step.type !== "none")
    return `Runs as you reach this step, then repeats every 4 mm as you keep moving ${row.value.path.at(-1)}. Move back to stop.`;
  if (!row.value.instant)
    return row.value.step.type === "none"
      ? "This is a waypoint. Keep moving to another step, or release here to do nothing."
      : "Runs once when you release at this step. Move back toward the start to cancel.";
  if (row.value.step.type === "none") return "No action is assigned to this direction yet.";
  if (row.value.step.type === "hold")
    return "Starts after a 2 mm movement. The key stays down until you release the button or change direction.";
  return row.value.step.repeat
    ? "Runs after a 2 mm movement, then repeats every 4 mm as you keep moving."
    : "Runs as soon as you move 2 mm in this direction. You can release the button straight away.";
});
</script>
