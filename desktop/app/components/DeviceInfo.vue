<template>
  <PopoverRoot v-model:open="open">
    <!-- Anchored by hand: inside the context menu, the trigger would anchor the menu instead of the popover. -->
    <PopoverAnchor as="template" :reference="trigger?.$el" />
    <UiContextMenu :items="menu">
      <PopoverTrigger
        ref="trigger"
        class="group flex h-8 items-center gap-1.5 rounded-lg px-2 transition-colors hover:bg-fill data-[state=open]:bg-fill">
        {{ device.name }}
        <PhCaretDown
          class="size-4 text-ink-3 transition-transform group-data-[state=open]:rotate-180" />
      </PopoverTrigger>
    </UiContextMenu>
    <UiPopoverContent
      :open="open"
      align="start"
      class="flex w-80 flex-col gap-5 p-5 text-base font-normal tracking-normal">
      <dl class="grid grid-cols-[6.5rem_1fr] gap-y-2.5 text-sm">
        <dt class="text-ink-3">Name</dt>
        <dd class="font-medium">{{ device.name }}</dd>
        <dt class="text-ink-3">Connection</dt>
        <dd class="font-medium">Bluetooth</dd>
        <dt class="text-ink-3">Battery</dt>
        <dd class="font-medium">
          {{ device.battery.percent }}%{{ device.battery.charging ? ", charging" : "" }}
        </dd>
        <dt class="text-ink-3">Firmware</dt>
        <dd class="font-mono text-xs leading-5">{{ device.firmware }}</dd>
        <dt class="text-ink-3">Product ID</dt>
        <dd class="font-mono text-xs leading-5">{{ device.productId }}</dd>
      </dl>

      <div class="border-t border-line pt-4 text-sm">
        <SettingRow
          label="Start at sign-in"
          label-for="start-at-login"
          description="Keeps your buttons and gestures working after you sign in, even with this window closed.">
          <UiSwitch
            id="start-at-login"
            :model-value="state?.startAtLogin ?? true"
            @update:model-value="
              (enabled) =>
                attempt('setStartAtLogin', { enabled }, 'Couldn\'t change start at sign-in')
            " />
        </SettingRow>
      </div>

      <div class="border-t border-line pt-4 text-sm" v-if="state?.optionsPlusRunning">
        <span class="rounded-full px-3 py-1 text-xs font-medium bg-warn-fill text-warn">
          Logi Options+ is running. It is recommended to quit Options+ to avoid conflicts with MX
          Controls.
        </span>
      </div>
    </UiPopoverContent>
  </PopoverRoot>
</template>

<script setup lang="ts">
import { PhCaretDown, PhCopy, PhInfo, PhPower } from "@phosphor-icons/vue";
import { PopoverAnchor, PopoverRoot, PopoverTrigger } from "reka-ui";
import type { DeviceState } from "~/composables/useAgent";
import type { MenuEntry } from "~/utils/menu";

const { device } = defineProps<{ device: DeviceState }>();
const { state, attempt } = useAgent();
const open = ref(false);
const trigger = useTemplateRef("trigger");

const menu = computed<MenuEntry[]>(() => {
  const startAtLogin = state.value?.startAtLogin ?? true;
  return [
    { label: "Mouse details", icon: PhInfo, select: () => (open.value = true) },
    {
      label: "Copy firmware version",
      icon: PhCopy,
      select: () => copyText(device.firmware, "firmware version"),
    },
    {
      label: "Copy product ID",
      icon: PhCopy,
      select: () => copyText(device.productId, "product ID"),
    },
    "separator",
    {
      label: "Start at sign-in",
      icon: PhPower,
      checked: startAtLogin,
      select: () =>
        attempt("setStartAtLogin", { enabled: !startAtLogin }, "Couldn't change start at sign-in"),
    },
  ];
});
</script>
