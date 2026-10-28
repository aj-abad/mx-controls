<template>
  <div v-if="device" class="flex flex-col gap-6">
    <header>
      <h2 class="type-title">Pointer and scrolling</h2>
      <p class="mt-1 text-ink-3">These settings are stored on the mouse, so they follow it to other computers.</p>
    </header>

    <SettingsSection title="Pointer">
      <SettingsCard class="px-4 py-4">
        <Slider
          v-model="dpi"
          label="Pointer speed"
          :description="`${device.dpi.default} DPI is the default.`"
          :min="device.dpi.min"
          :max="device.dpi.max"
          :step="device.dpi.step"
          :format="(value) => `${value} DPI`"
          @change="(value) => attempt('setDpi', { dpi: value }, 'Couldn\'t change the pointer speed')" />
      </SettingsCard>
    </SettingsSection>

    <SettingsSection
      title="Smart Shift"
      note="The wheel clicks in ratchet mode and switches to free spin when you scroll fast.">
      <SettingsCard>
        <SettingsRow label="Ratchet" label-for="ratchet" description="Off keeps the wheel in free spin.">
          <Toggle id="ratchet" v-model:checked="ratchet" @update:checked="saveShift" />
        </SettingsRow>
        <template v-if="ratchet">
          <Divider />
          <div class="px-4 py-3">
            <Slider
              v-model="autoDisengage"
              label="Free spin speed"
              :description="`How fast you scroll before the wheel spins freely. ${device.smartShift.defaultAutoDisengage} is the default.`"
              :min="1"
              :max="50"
              @change="saveShift" />
          </div>
          <template v-if="device.smartShift.tunableTorque">
            <Divider />
            <div class="px-4 py-3">
              <Slider
                v-model="torque"
                label="Ratchet force"
                :description="`${device.smartShift.defaultTorque}% is the default.`"
                :min="1"
                :max="100"
                :format="(value) => `${value}%`"
                @change="saveShift" />
            </div>
          </template>
        </template>
      </SettingsCard>
    </SettingsSection>
  </div>
</template>

<script setup lang="ts">
import Divider from "@arcon.mobi/sine/components/UI/Divider.vue";
import Slider from "@arcon.mobi/sine/components/UI/Slider.vue";
import Toggle from "@arcon.mobi/sine/components/UI/Toggle.vue";

const { device, attempt } = useAgent();

const dpi = ref(0);
const ratchet = ref(true);
const autoDisengage = ref(1);
const torque = ref(1);

watchEffect(() => {
  if (!device.value) return;
  dpi.value = device.value.dpi.current;
  ratchet.value = device.value.smartShift.ratchet;
  autoDisengage.value = Math.min(Math.max(device.value.smartShift.autoDisengage, 1), 50);
  torque.value = device.value.smartShift.torque;
});

const saveShift = () =>
  attempt(
    "setSmartShift",
    { ratchet: ratchet.value, autoDisengage: autoDisengage.value, torque: torque.value },
    "Couldn't change Smart Shift",
  );
</script>
