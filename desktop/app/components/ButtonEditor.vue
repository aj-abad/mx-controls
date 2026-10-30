<template>
  <div v-if="draft" class="flex flex-col gap-5">
    <UiSegmented v-model="mode" :options="MODE_OPTIONS" label="What the button does" />

    <p v-if="mode === 'default'" class="text-sm text-ink-3">{{ defaultText }}</p>

    <ActionEditor v-else-if="mode === 'action'" v-model="draft.action" :label="button.name" />

    <div v-else class="flex flex-col gap-4">
      <p class="text-sm text-ink-3">Hold the button and move the mouse, or click it without moving.</p>
      <div class="flex flex-wrap items-center gap-1.5">
        <span class="mr-0.5 text-xs text-ink-3">Presets</span>
        <button
          v-for="preset in GESTURE_PRESETS"
          :key="preset.id"
          type="button"
          class="h-7 rounded-full bg-field px-3 text-xs font-medium ring-1 ring-line ring-inset transition-colors hover:bg-fill"
          @click="applyGesturePreset(preset)">
          {{ preset.label }}
        </button>
      </div>
      <GesturePad v-model="part" :gesture="draft.gesture" :fired="fired" :label="`${button.name} gesture`" />
      <div class="flex flex-col gap-2">
        <span class="text-sm font-medium">{{ partHeading }}</span>
        <ActionEditor
          :key="part"
          v-model="draft.gesture[part]"
          :label="`${button.name} ${partLabel.toLowerCase()}`"
          :allow-hold="part !== 'click'"
          :allow-repeat="part !== 'click'"
          repeat-label="Repeat as you keep moving" />
      </div>
      <p v-if="unfinished" class="text-sm text-warn">
        Record keys for “{{ unfinished.id === "click" ? "Click" : `Move ${unfinished.label.toLowerCase()}` }}” to save this gesture.
      </p>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { ButtonAction, ButtonBinding, ButtonState, GestureActions } from "~/composables/useAgent";

const { button, binding, apply } = defineProps<{
  button: ButtonState;
  binding: ButtonBinding | undefined;
  /** Sends the binding, null for the default; resolves false if the agent refused it. */
  apply: (binding: ButtonBinding | null) => Promise<boolean>;
}>();

const MODE_OPTIONS = [
  { value: "default", label: "Default" },
  { value: "action", label: "Action" },
  { value: "gesture", label: "Gesture" },
];

/** What the button does without MX Controls, for the buttons whose own action is known. */
const OWN_ACTIONS: Record<string, string> = { back: "Goes back", forward: "Goes forward", middle: "Middle-clicks" };
const defaultText = computed(() => {
  const own = OWN_ACTIONS[button.id];
  return own ? `${own}, as it does without MX Controls.` : "Does what it does without MX Controls.";
});

const mode = ref("default");
const draft = ref<{ action: ButtonAction; gesture: GestureActions } | null>(null);

const part = ref<keyof GestureActions>("click");
const partLabel = computed(() => GESTURE_PARTS.find((p) => p.id === part.value)!.label);
const partHeading = computed(() => (part.value === "click" ? "Click" : `Move ${partLabel.value.toLowerCase()}`));

function applyGesturePreset({ parts, repeating }: GesturePreset) {
  const gesture = blankGesture();
  for (const id of Object.keys(parts) as (keyof GestureActions)[]) {
    const action = presetById(parts[id])?.action ?? NOTHING;
    gesture[id] = repeating?.includes(id) ? { ...action, repeat: true } : action;
  }
  draft.value!.gesture = gesture;
}

// Making the gesture with the mouse opens that direction, and the pad lights it up.
const { lastGesture } = useAgent();
const fired = computed(() => (lastGesture.value?.button === button.id ? lastGesture.value : null));
watch(fired, (gesture) => gesture && (part.value = gesture.direction));

const blankGesture = (): GestureActions => ({ click: NOTHING, up: NOTHING, down: NOTHING, left: NOTHING, right: NOTHING });

function reset() {
  mode.value = binding?.mode ?? "default";
  draft.value = {
    action: binding?.action ?? { type: "press", keys: [] },
    gesture: binding?.gesture ? { ...binding.gesture } : blankGesture(),
  };
}

// The agent sends back nulls and `repeat: false` that the editor leaves out, so compare without them.
const canonical = (value: unknown) =>
  JSON.stringify(value, (_, v) =>
    v && typeof v === "object" && !Array.isArray(v)
      ? Object.fromEntries(Object.entries(v).filter(([, x]) => x != null && x !== false).sort(([a], [b]) => a.localeCompare(b)))
      : v,
  );

// Every state the agent pushes, a battery update included, brings new binding objects, so only a
// different button or a binding that really changed throws away an unsaved draft.
watch(() => `${button.id}|${canonical(binding ?? null)}`, reset, { immediate: true });

const next = computed<ButtonBinding | null>(() => {
  if (!draft.value || mode.value === "default") return null;
  return mode.value === "action"
    ? { button: button.id, mode: "action", action: draft.value.action }
    : { button: button.id, mode: "gesture", gesture: draft.value.gesture };
});

/** Whether the agent would take the action: a shortcut needs its keys before it can do anything. */
const complete = (action: ButtonAction) =>
  action.type === "press" || action.type === "hold" ? !!action.keys?.length : true;

const unfinished = computed(() =>
  mode.value === "gesture" ? GESTURE_PARTS.find((p) => !complete(draft.value!.gesture[p.id])) : undefined,
);

/**
 * Changes apply as they're made, with Ctrl+Z to put them back, once the agent would take them. Until then the
 * draft waits: an action with no choice yet, a shortcut with no keys, or a gesture that does nothing at all,
 * which would only be a button switched off by looking at the Gesture tab.
 */
const ready = computed(() => {
  const binding = next.value;
  if (!binding) return true;
  if (binding.mode === "action") return complete(binding.action!);
  const actions = Object.values(binding.gesture!);
  return actions.every(complete) && actions.some((action) => action.type !== "none");
});

// A refused change goes back to what the button does, rather than staying on screen as if it applied.
watch(
  () => canonical(next.value),
  async (now) => {
    if (ready.value && now !== canonical(binding ?? null) && !(await apply(next.value))) reset();
  },
);
</script>
