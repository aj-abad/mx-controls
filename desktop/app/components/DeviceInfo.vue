<template>
  <PopoverRoot v-model:open="open">
    <PopoverTrigger
      class="group flex h-8 items-center gap-1.5 rounded-lg px-2 transition-colors hover:bg-fill data-[state=open]:bg-fill">
      {{ device.name }}
      <IconChevronDown class="size-4 text-ink-3 transition-transform group-data-[state=open]:rotate-180" />
    </PopoverTrigger>
    <UiPopoverContent :open="open" align="start" class="flex w-80 flex-col gap-5 p-5 text-base font-normal tracking-normal">
      <dl class="grid grid-cols-[6.5rem_1fr] gap-y-2.5 text-sm">
        <dt class="text-ink-3">Name</dt>
        <dd class="font-medium">{{ device.name }}</dd>
        <dt class="text-ink-3">Connection</dt>
        <dd class="font-medium">Bluetooth</dd>
        <dt class="text-ink-3">Battery</dt>
        <dd class="font-medium">{{ device.battery.percent }}%{{ device.battery.charging ? ", charging" : "" }}</dd>
        <dt class="text-ink-3">Firmware</dt>
        <dd class="font-mono text-xs leading-5">{{ device.firmware }}</dd>
        <dt class="text-ink-3">Product ID</dt>
        <dd class="font-mono text-xs leading-5">{{ device.productId }}</dd>
      </dl>

      <div class="border-t border-line pt-4 text-sm">
        <SettingRow
          label="Start at sign-in"
          label-for="start-at-login"
          description="The background agent runs your buttons and gestures, even with this window closed.">
          <UiSwitch
            id="start-at-login"
            :model-value="state?.startAtLogin ?? true"
            @update:model-value="(enabled) => attempt('setStartAtLogin', { enabled }, 'Couldn\'t change start at sign-in')" />
        </SettingRow>
      </div>

      <div class="border-t border-line pt-4 text-sm">
        <SettingRow
          label="Logi Options+"
          :description="
            state?.optionsPlusRunning
              ? 'Running. It diverts some buttons too and acts on them alongside MX Controls.'
              : 'Not running.'
          ">
          <span
            class="rounded-full px-3 py-1 text-xs font-medium"
            :class="state?.optionsPlusRunning ? 'bg-warn-fill text-warn' : 'bg-fill text-ink-2'">
            {{ state?.optionsPlusRunning ? "Running" : "Off" }}
          </span>
        </SettingRow>
      </div>
    </UiPopoverContent>
  </PopoverRoot>
</template>

<script setup lang="ts">
import { PopoverRoot, PopoverTrigger } from "reka-ui";
import type { DeviceState } from "~/composables/useAgent";

defineProps<{ device: DeviceState }>();
const { state, attempt } = useAgent();
const open = ref(false);
</script>
