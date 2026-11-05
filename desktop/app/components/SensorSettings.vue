<template>
  <UiSlider
    v-model="dpi"
    inline
    label="Speed"
    :default-value="device.dpi.default"
    :min="device.dpi.min"
    :max="device.dpi.max"
    :step="device.dpi.step"
    :format="(value) => `${value} DPI`"
    @commit="(value) => attempt('setDpi', { dpi: value }, 'Couldn\'t change the pointer speed')" />
</template>

<script setup lang="ts">
import type { DeviceState } from "~/composables/useAgent";

const { device } = defineProps<{ device: DeviceState }>();
const { attempt } = useAgent();

const dpi = ref(0);
watchEffect(() => (dpi.value = device.dpi.current));
</script>
