<template>
  <div class="flex flex-col gap-6">
    <SettingRow label="SmartShift" label-for="ratchet" description="Off keeps the wheel in free spin.">
      <UiSwitch id="ratchet" v-model="ratchet" @update:model-value="save" />
    </SettingRow>
    <template v-if="ratchet">
      <UiSlider
        v-model="autoDisengage"
        label="Sensitivity"
        :description="`How fast you scroll before the wheel spins freely. ${device.smartShift.defaultAutoDisengage} is the default.`"
        :min="1"
        :max="50"
        @commit="save" />
      <UiSlider
        v-if="device.smartShift.tunableTorque"
        v-model="torque"
        label="Scroll force"
        :description="`How firmly the wheel clicks. ${device.smartShift.defaultTorque}% is the default.`"
        :min="1"
        :max="100"
        :format="(value) => `${value}%`"
        @commit="save" />
    </template>
  </div>
</template>

<script setup lang="ts">
import type { DeviceState } from "~/composables/useAgent";

const { device } = defineProps<{ device: DeviceState }>();
const { attempt } = useAgent();

const ratchet = ref(true);
const autoDisengage = ref(1);
const torque = ref(1);

watchEffect(() => {
  ratchet.value = device.smartShift.ratchet;
  autoDisengage.value = Math.min(Math.max(device.smartShift.autoDisengage, 1), 50);
  torque.value = device.smartShift.torque;
});

const save = () =>
  attempt(
    "setSmartShift",
    { ratchet: ratchet.value, autoDisengage: autoDisengage.value, torque: torque.value },
    "Couldn't change SmartShift",
  );
</script>
