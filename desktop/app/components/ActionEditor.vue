<template>
  <div class="flex flex-col gap-2.5">
    <ActionPicker
      :model-value="choice"
      :label="label"
      :allow-hold="allowHold"
      :default-label="defaultLabel"
      @update:model-value="choose"
      @record="capture?.start()" />
    <ShortcutCapture
      v-if="modelValue && (choice === 'shortcut' || choice === 'hold')"
      ref="capture"
      :model-value="modelValue.keys"
      :label="label"
      @update:model-value="(keys) => update({ keys })" />
    <UiCheckbox
      v-if="modelValue && allowRepeat && (modelValue.type === 'press' || modelValue.type === 'system')"
      :model-value="!!modelValue.repeat"
      :label="repeatLabel"
      @update:model-value="(repeat) => update({ repeat })" />
  </div>
</template>

<script setup lang="ts">
import type { ButtonAction } from "~/composables/useAgent";

const {
  modelValue,
  label,
  allowHold = true,
  allowRepeat = true,
  repeatLabel = "Repeat while held",
  defaultLabel,
} = defineProps<{
  /** Null for the button's default, where `defaultLabel` is given. */
  modelValue: ButtonAction | null;
  label: string;
  /** A key only makes sense where the action lasts as long as the press. */
  allowHold?: boolean;
  /** Repeating needs a press to repeat through. */
  allowRepeat?: boolean;
  /** A gesture direction repeats with movement rather than with time. */
  repeatLabel?: string;
  /** Offers the button's default, under this name; picking it emits `default`. */
  defaultLabel?: string;
}>();
const emit = defineEmits<{ "update:modelValue": [action: ButtonAction]; default: [] }>();

const capture = useTemplateRef("capture");

/**
 * Set once a custom shortcut is chosen, so recording keys that happen to match a preset, such as Ctrl + C,
 * doesn't swap the key field for "Copy" mid-edit.
 */
const custom = ref(false);

const choice = computed(() => {
  if (!modelValue) return "default";
  const preset = presetFor(modelValue);
  if (preset && !custom.value) return preset.id;
  // A fresh action has no keys and no choice yet; the picker asks for one.
  if ((modelValue.type === "press" || modelValue.type === "hold") && !modelValue.keys?.length && !custom.value) return "";
  if (modelValue.type === "hold") return "hold";
  if (modelValue.type === "press") return "shortcut";
  return "none";
});

function choose(id: string) {
  custom.value = id === "shortcut" || id === "hold";
  if (id === "default") return emit("default");
  const keys = modelValue && (modelValue.type === "press" || modelValue.type === "hold") && !presetFor(modelValue) ? modelValue.keys : [];
  if (id === "shortcut") emit("update:modelValue", { type: "press", keys });
  else if (id === "hold") emit("update:modelValue", { type: "hold", keys });
  else emit("update:modelValue", presetById(id)?.action ?? NOTHING);
}

const update = (patch: Partial<ButtonAction>) => modelValue && emit("update:modelValue", { ...modelValue, ...patch });
</script>
