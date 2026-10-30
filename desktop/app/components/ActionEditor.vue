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
      v-if="choice === 'newMacro'"
      ref="capture"
      :model-value="[]"
      :label="`${label} new macro`"
      @update:model-value="saveMacro" />
    <ShortcutCapture
      v-else-if="modelValue && (choice === 'shortcut' || choice === 'hold')"
      ref="capture"
      :model-value="modelValue.keys"
      :label="label"
      @update:model-value="(keys) => update({ keys })" />
    <button
      v-if="modelValue?.type === 'macro' && modelValue.macro"
      type="button"
      class="self-start text-xs text-ink-3 hover:text-ink"
      @click="goTo('macros', modelValue.macro)">
      Edit macro ›
    </button>
    <UiCheckbox
      v-if="modelValue && allowRepeat && (modelValue.type === 'press' || modelValue.type === 'system' || modelValue.type === 'macro')"
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
/** Recording the keys of a macro to make, which the action runs once it's saved. */
const makingMacro = ref(false);

const choice = computed(() => {
  if (makingMacro.value) return "newMacro";
  if (!modelValue) return "default";
  if (modelValue.type === "macro") return `macro:${modelValue.macro}`;
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
  makingMacro.value = id === "newMacro";
  if (id === "default") return emit("default");
  if (id === "newMacro") return;
  if (id.startsWith("macro:"))
    return emit("update:modelValue", { type: "macro", macro: id.slice(6), ...(modelValue?.repeat && { repeat: true }) });
  const keys = modelValue && (modelValue.type === "press" || modelValue.type === "hold") && !presetFor(modelValue) ? modelValue.keys : [];
  if (id === "shortcut") emit("update:modelValue", { type: "press", keys });
  else if (id === "hold") emit("update:modelValue", { type: "hold", keys });
  else emit("update:modelValue", presetById(id)?.action ?? NOTHING);
}

const update = (patch: Partial<ButtonAction>) => modelValue && emit("update:modelValue", { ...modelValue, ...patch });

const { goTo } = usePage();
const macros = useMacros();

/** Saves the keys just recorded as a macro named after them, and runs it from here. */
async function saveMacro(keys: string[]) {
  const id = await macros.createFromKeys(keys);
  makingMacro.value = false;
  if (id) emit("update:modelValue", { type: "macro", macro: id, ...(modelValue?.repeat && { repeat: true }) });
}
</script>
