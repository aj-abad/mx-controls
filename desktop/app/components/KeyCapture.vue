<template>
  <Button
    variant="secondary"
    size="sm"
    :active="capturing"
    :aria-label="capturing ? 'Press a key, or Esc to cancel' : `Key for ${buttonName}`"
    @click="capturing ? stop() : begin()"
    @blur="stop">
    <span :class="!modelValue && !capturing && 'text-ink-3'">
      {{ capturing ? "Press a key…" : modelValue ? keyLabel(modelValue) : "Choose key" }}
    </span>
  </Button>
</template>

<script setup lang="ts">
import Button from "@arcon.mobi/sine/components/UI/Button.vue";

const { buttonName } = defineProps<{ modelValue: string | null; buttonName: string }>();
const emit = defineEmits<{ "update:modelValue": [code: string] }>();

const capturing = ref(false);

function onKey(event: KeyboardEvent) {
  event.preventDefault();
  event.stopPropagation();
  if (event.code === "Escape" && !event.repeat) {
    stop();
    return;
  }
  if (!event.code || event.code === "Unidentified") return;
  emit("update:modelValue", event.code);
  stop();
}

function begin() {
  capturing.value = true;
  window.addEventListener("keydown", onKey, { capture: true });
}

function stop() {
  capturing.value = false;
  window.removeEventListener("keydown", onKey, { capture: true });
}

onBeforeUnmount(stop);
</script>
