<template>
  <div class="flex flex-col gap-6">
    <section v-if="device.forceButton" class="border-t border-line pt-5">
      <UiSlider
        v-model="force"
        label="Press force"
        description="How hard you press the panel before it clicks."
        :min="device.forceButton.min"
        :max="device.forceButton.max"
        :default-value="device.forceButton.default"
        :format="forceLabel"
        @commit="(value) => attempt('setForce', { force: value }, 'Couldn\'t change the press force')" />
    </section>

    <section class="flex flex-col gap-5 border-t border-line pt-5">
      <SettingRow
        label="Haptic feedback"
        label-for="haptics"
        description="Taps your thumb when a gesture runs.">
        <UiSwitch id="haptics" v-model="enabled" @update:model-value="saveLevel" />
      </SettingRow>
      <UiSlider
        v-model="level"
        :disabled="!enabled"
        :class="!enabled && 'opacity-40'"
        label="Strength"
        :min="1"
        :max="100"
        :format="(value) => `${value}%`"
        @commit="saveLevel" />
      <div class="grid grid-cols-2 gap-2">
        <button
          v-for="waveform in device.haptics.waveforms"
          :key="waveform"
          type="button"
          :disabled="!device.haptics.enabled"
          class="flex items-center gap-2.5 rounded-xl bg-field px-3 py-2.5 text-left text-sm hover:bg-fill disabled:opacity-40"
          @click="attempt('playHaptic', { waveform }, 'Couldn\'t play the effect')">
          <span class="grid size-7 shrink-0 place-items-center rounded-full bg-ink text-on-ink">
            <PhPlay weight="fill" class="size-3" />
          </span>
          {{ humanize(waveform) }}
        </button>
      </div>
    </section>
  </div>
</template>

<script setup lang="ts">
import { PhPlay } from "@phosphor-icons/vue";
import type { DeviceState } from "~/composables/useAgent";

const { device } = defineProps<{ device: DeviceState }>();
const { attempt } = useAgent();

const enabled = ref(true);
const level = ref(50);
const force = ref(0);

watchEffect(() => {
  enabled.value = device.haptics.enabled;
  level.value = Math.max(device.haptics.level, 1);
  force.value = device.forceButton?.current ?? 0;
});

const saveLevel = () =>
  attempt("setHapticLevel", { level: enabled.value ? level.value : 0 }, "Couldn't change haptic feedback");

function forceLabel(value: number) {
  const range = device.forceButton;
  if (!range) return `${value}`;
  const share = Math.round(((value - range.min) / (range.max - range.min)) * 100);
  return `${share}%`;
}

const humanize = (name: string) =>
  name.replace(/(?<=[a-z])(?=[A-Z])/g, " ").replace(/ (\w)/g, (_, c: string) => ` ${c.toLowerCase()}`);
</script>
