<template>
  <div v-if="device" class="flex flex-col gap-6">
    <header>
      <h2 class="type-title">Haptics</h2>
      <p class="mt-1 text-ink-3">The mouse taps your thumb for feedback, and for any effect played from here.</p>
    </header>

    <SettingsSection title="Feedback">
      <SettingsCard>
        <SettingsRow label="Haptic feedback" label-for="haptics">
          <Toggle id="haptics" v-model:checked="enabled" @update:checked="saveLevel" />
        </SettingsRow>
        <template v-if="enabled">
          <Divider />
          <div class="px-4 py-3">
            <Slider v-model="level" label="Strength" :min="1" :max="100" :format="(value) => `${value}%`" @change="saveLevel" />
          </div>
        </template>
      </SettingsCard>
    </SettingsSection>

    <SettingsSection
      v-if="device.forceButton"
      title="Haptic sense panel"
      note="How hard you press the panel under your thumb before it clicks.">
      <SettingsCard class="px-4 py-4">
        <Slider
          v-model="force"
          label="Press force"
          :min="device.forceButton.min"
          :max="device.forceButton.max"
          :format="forceLabel"
          @change="(value) => attempt('setForce', { force: value }, 'Couldn\'t change the press force')" />
      </SettingsCard>
    </SettingsSection>

    <SettingsSection title="Effects" note="Effects play at the strength set above.">
      <SettingsCard>
        <template v-for="(waveform, index) in device.haptics.waveforms" :key="waveform">
          <Divider v-if="index > 0" />
          <SettingsRow :label="humanize(waveform)">
            <Button
              variant="ghost"
              size="sm"
              :disabled="!device.haptics.enabled"
              :aria-label="`Play ${humanize(waveform)}`"
              @click="attempt('playHaptic', { waveform }, 'Couldn\'t play the effect')">
              <template #leading><IconPlay aria-hidden="true" /></template>
              Play
            </Button>
          </SettingsRow>
        </template>
      </SettingsCard>
    </SettingsSection>
  </div>
</template>

<script setup lang="ts">
import Button from "@arcon.mobi/sine/components/UI/Button.vue";
import Divider from "@arcon.mobi/sine/components/UI/Divider.vue";
import Slider from "@arcon.mobi/sine/components/UI/Slider.vue";
import Toggle from "@arcon.mobi/sine/components/UI/Toggle.vue";
import IconPlay from "#sine-icons/ui/glyph/18/media-play";

const { device, attempt } = useAgent();

const enabled = ref(true);
const level = ref(50);
const force = ref(0);

watchEffect(() => {
  if (!device.value) return;
  enabled.value = device.value.haptics.enabled;
  level.value = Math.max(device.value.haptics.level, 1);
  force.value = device.value.forceButton?.current ?? 0;
});

const saveLevel = () =>
  attempt("setHapticLevel", { level: enabled.value ? level.value : 0 }, "Couldn't change haptic feedback");

function forceLabel(value: number) {
  const range = device.value?.forceButton;
  if (!range) return `${value}`;
  const share = Math.round(((value - range.min) / (range.max - range.min)) * 100);
  return value === range.default ? `${share}% (default)` : `${share}%`;
}

const humanize = (name: string) => name.replace(/(?<=[a-z])(?=[A-Z])/g, " ").replace(/ (\w)/g, (_, c: string) => ` ${c.toLowerCase()}`);
</script>
