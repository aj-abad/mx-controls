<template>
  <div v-if="inherits" class="flex flex-col items-start gap-3 rounded-xl bg-fill/60 p-4 ring-1 ring-line ring-inset">
    <p class="text-[13px] leading-relaxed text-ink-2">
      In the second layer the thumb wheel keeps what it does in the main layer.
    </p>
    <UiButton size="sm" @click="customize">Set it for this layer</UiButton>
  </div>
  <div v-else class="flex flex-col gap-6">
    <UiSegmented v-model="draft.mode" :options="MODE_OPTIONS" label="What the thumb wheel does" @update:model-value="changeMode" />

    <div class="flex flex-col gap-4" :class="draft.mode !== 'actions' && 'opacity-40'" :inert="draft.mode !== 'actions' || undefined">
      <div class="flex flex-wrap items-center gap-1.5">
        <span class="mr-0.5 text-xs text-ink-3">Presets</span>
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
    </div>

    <UiSlider
      v-model="draft.speed"
      :label="draft.mode === 'scroll' ? 'Scroll speed' : 'Action rate'"
      :description="
        draft.mode === 'scroll'
          ? 'Speeds other than 100% only apply while MX Studio runs in the background.'
          : 'At 100%, an action runs once per notch.'
      "
      :default-value="100"
      :min="25"
      :max="300"
      :step="5"
      :format="(value) => `${value}%`"
      @commit="save" />

    <SettingRow label="Invert direction" label-for="thumb-invert" description="Swaps left and right.">
      <UiSwitch id="thumb-invert" v-model="draft.inverted" @update:model-value="save" />
    </SettingRow>

    <button v-if="view.second.value" type="button" class="self-start text-xs text-ink-3 hover:text-ink" @click="inherit">
      Use the main layer's settings instead
    </button>
  </div>
</template>

<script setup lang="ts">
import type { ButtonAction, DeviceState, ThumbWheelSettings } from "~/composables/useAgent";

defineProps<{ device: DeviceState }>();
const { attempt } = useAgent();
const view = useProfileView();
/** The second layer keeps the main layer's settings until it's given its own. */
const inherits = computed(() => view.second.value && !view.thumbWheel.value);

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
  const saved = view.thumbWheel.value ?? view.mainThumbWheel.value;
  if (saved) draft.value = { ...saved };
});

function customize() {
  draft.value = { ...(view.mainThumbWheel.value ?? draft.value) };
  save();
}

function inherit() {
  void attempt("setThumbWheel", { thumbWheel: null, ...view.target() }, "Couldn't change the thumb wheel");
}

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
  void attempt("setThumbWheel", { thumbWheel: { ...next }, ...view.target() }, "Couldn't change the thumb wheel");
}
</script>
