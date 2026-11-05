<template>
  <div class="flex flex-col gap-6">
    <SettingRow
      v-if="device.scrollInverted !== null"
      label="Reverse scroll direction"
      label-for="scroll-inverted"
      description="Flips this mouse's wheel. Other mice and trackpads keep their direction.">
      <UiSwitch id="scroll-inverted" v-model="inverted" @update:model-value="saveDirection" />
    </SettingRow>
    <SettingRow
      label="SmartShift"
      label-for="ratchet"
      :description="
        ratchet
          ? 'Scrolling is detented, switching to free-spin when you scroll fast.'
          : 'Scrolling is always free-spinning.'
      ">
      <UiSwitch id="ratchet" v-model="ratchet" @update:model-value="save" />
    </SettingRow>
    <div class="flex flex-col gap-6" :class="!ratchet && 'opacity-40'" :inert="!ratchet || undefined">
      <UiSlider
        v-model="autoDisengage"
        :disabled="!ratchet"
        label="Threshold"
        description="How fast you scroll before the wheel spins freely."
        :default-value="device.smartShift.defaultAutoDisengage"
        :min="1"
        :max="50"
        @commit="save" />
      <UiSlider
        v-if="device.smartShift.tunableTorque"
        v-model="torque"
        :disabled="!ratchet"
        label="Scroll detent"
        description="How firmly the wheel clicks."
        :default-value="device.smartShift.defaultTorque"
        :min="1"
        :max="100"
        :format="(value) => `${value}%`"
        @commit="save" />
    </div>
  </div>
</template>

<script setup lang="ts">
import type { DeviceState } from "~/composables/useAgent";

const { device } = defineProps<{ device: DeviceState }>();
const { attempt } = useAgent();

const inverted = ref(false);
const ratchet = ref(true);
const autoDisengage = ref(1);
const torque = ref(1);

watchEffect(() => {
  inverted.value = device.scrollInverted ?? false;
  ratchet.value = device.smartShift.ratchet;
  autoDisengage.value = Math.min(Math.max(device.smartShift.autoDisengage, 1), 50);
  torque.value = device.smartShift.torque;
});

const saveDirection = () =>
  attempt("setScrollInverted", { inverted: inverted.value }, "Couldn't change the scroll direction");

const save = () =>
  attempt(
    "setSmartShift",
    { ratchet: ratchet.value, autoDisengage: autoDisengage.value, torque: torque.value },
    "Couldn't change SmartShift",
  );
</script>
