<template>
  <div class="flex flex-col gap-6">
    <UiSegmented v-model="draft.mode" :options="MODE_OPTIONS" label="What the thumb wheel does" @update:model-value="changeMode" />

    <template v-if="draft.mode === 'actions'">
      <div class="flex flex-wrap items-center gap-1.5">
        <span class="mr-0.5 text-xs text-ink-3">Start from</span>
        <button
          v-for="preset in STEP_PRESETS"
          :key="preset.id"
          type="button"
          class="h-7 rounded-full bg-field px-3 text-xs font-medium ring-1 ring-line ring-inset transition-colors hover:bg-fill"
          @click="applyPreset(preset)">
          {{ preset.label }}
        </button>
      </div>
      <div v-for="side in SIDES" :key="side.id" class="flex flex-col gap-2">
        <span class="text-sm font-medium">{{ side.label }}</span>
        <ActionEditor
          :model-value="draft[side.id] ?? NOTHING"
          :label="`Thumb wheel ${side.id}`"
          :allow-hold="false"
          :allow-repeat="false"
          @update:model-value="(action) => ((draft[side.id] = action), save())" />
      </div>
    </template>

    <UiSlider
      v-model="draft.speed"
      :label="draft.mode === 'scroll' ? 'Scroll speed' : 'Steps per turn'"
      :description="
        draft.mode === 'scroll'
          ? '100% is the wheel\'s own speed. Any other speed is run by MX Controls while it\'s running.'
          : 'At 100% an action runs once for each notch the wheel would scroll.'
      "
      :min="25"
      :max="300"
      :step="5"
      :format="(value) => `${value}%`"
      @commit="save" />

    <SettingRow label="Invert direction" label-for="thumb-invert" description="Swaps left and right.">
      <UiSwitch id="thumb-invert" v-model="draft.inverted" @update:model-value="save" />
    </SettingRow>
  </div>
</template>

<script setup lang="ts">
import type { ButtonAction, DeviceState, ThumbWheelSettings } from "~/composables/useAgent";

defineProps<{ device: DeviceState }>();
const { state, attempt } = useAgent();

const MODE_OPTIONS = [
  { value: "scroll", label: "Scroll sideways" },
  { value: "actions", label: "Actions" },
];
const SIDES = [
  { id: "left", label: "← Turn left" },
  { id: "right", label: "→ Turn right" },
] as const;

/** Pairs of presets by ID, for the way the wheel scrolls left and right. */
const STEP_PRESETS = [
  { id: "volume", label: "Volume", left: "volumeDown", right: "volumeUp" },
  { id: "zoom", label: "Zoom", left: "zoomOut", right: "zoomIn" },
  { id: "tabs", label: "Tabs", left: "previousTab", right: "nextTab" },
  { id: "desktops", label: "Desktops", left: "desktopLeft", right: "desktopRight" },
  { id: "history", label: "Back and forward", left: "back", right: "forward" },
];

const draft = ref<ThumbWheelSettings>({ mode: "scroll", speed: 100, inverted: false });
watchEffect(() => {
  const saved = state.value?.thumbWheel;
  if (saved) draft.value = { ...saved };
});

const presetAction = (id: string): ButtonAction => presetById(id)?.action ?? NOTHING;

function changeMode(mode: string) {
  // Actions start as the volume, so the wheel does something the moment the mode changes.
  if (mode === "actions" && !draft.value.left && !draft.value.right)
    Object.assign(draft.value, { left: presetAction("volumeDown"), right: presetAction("volumeUp") });
  save();
}

function applyPreset(preset: (typeof STEP_PRESETS)[number]) {
  Object.assign(draft.value, { left: presetAction(preset.left), right: presetAction(preset.right) });
  save();
}

// A custom shortcut has no keys until they're recorded, and the agent refuses a step without any.
const unfinished = (action: ButtonAction | null | undefined) =>
  !!action && (action.type === "press" || action.type === "hold") && !action.keys?.length;

function save() {
  const next = draft.value;
  if (next.mode === "actions" && (unfinished(next.left) || unfinished(next.right))) return;
  void attempt("setThumbWheel", { thumbWheel: next }, "Couldn't change the thumb wheel");
}
</script>
