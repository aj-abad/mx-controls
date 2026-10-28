<template>
  <div v-if="device" class="flex flex-col gap-6">
    <header>
      <h2 class="type-title">Device</h2>
    </header>

    <SettingsSection title="Mouse">
      <SettingsCard>
        <SettingsRow label="Name"><span class="font-medium">{{ device.name }}</span></SettingsRow>
        <Divider />
        <SettingsRow label="Connection"><span class="font-medium">Bluetooth</span></SettingsRow>
        <Divider />
        <SettingsRow label="Battery">
          <StatusPill :tone="device.battery.percent <= 15 ? 'warn' : 'good'">
            {{ device.battery.percent }}%{{ device.battery.charging ? ", charging" : "" }}
          </StatusPill>
        </SettingsRow>
        <Divider />
        <SettingsRow label="Firmware"><span class="font-mono text-sm">{{ device.firmware }}</span></SettingsRow>
        <Divider />
        <SettingsRow label="Product ID"><span class="font-mono text-sm">{{ device.productId }}</span></SettingsRow>
      </SettingsCard>
    </SettingsSection>

    <SettingsSection title="Other apps">
      <SettingsCard>
        <SettingsRow
          label="Logi Options+"
          :description="
            state?.optionsPlusRunning
              ? 'Running. It diverts some buttons too and acts on them alongside MX Controls.'
              : 'Not running.'
          ">
          <StatusPill :tone="state?.optionsPlusRunning ? 'warn' : 'idle'">
            {{ state?.optionsPlusRunning ? "Running" : "Not running" }}
          </StatusPill>
        </SettingsRow>
      </SettingsCard>
    </SettingsSection>
  </div>
</template>

<script setup lang="ts">
import Divider from "@arcon.mobi/sine/components/UI/Divider.vue";
import StatusPill from "@arcon.mobi/sine/components/UI/StatusPill.vue";

const { state, device } = useAgent();
</script>
