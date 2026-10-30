<template>
  <div class="flex flex-col gap-6">
    <UiSlider
      v-model="dpi"
      label="Pointer speed"
      :description="`${device.dpi.default} DPI is the default.`"
      :min="device.dpi.min"
      :max="device.dpi.max"
      :step="device.dpi.step"
      :format="(value) => `${value} DPI`"
      @commit="(value) => attempt('setDpi', { dpi: value }, 'Couldn\'t change the pointer speed')" />
  </div>
</template>

<script setup lang="ts">
import type { DeviceState } from "~/composables/useAgent";

const { device } = defineProps<{ device: DeviceState }>();
const { attempt } = useAgent();

const dpi = ref(0);
watchEffect(() => (dpi.value = device.dpi.current));
</script>
