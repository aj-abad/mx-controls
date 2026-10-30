<template>
  <PopoverRoot v-model:open="open">
    <PopoverTrigger
      :aria-label="`Easy-Switch: channel ${hosts.current + 1}`"
      class="flex h-8 items-center gap-1.5 rounded-lg px-2.5 text-sm text-ink-2 transition-colors hover:bg-fill hover:text-ink data-[state=open]:bg-fill data-[state=open]:text-ink">
      <span class="grid size-4 place-items-center rounded-full bg-ink text-[10px] font-semibold text-white">{{ hosts.current + 1 }}</span>
      Easy-Switch
    </PopoverTrigger>
    <UiPopoverContent :open="open" align="end" class="flex w-80 flex-col gap-3 p-4">
      <ul aria-label="Easy-Switch channels" class="flex flex-col gap-1">
        <li v-for="channel in hosts.channels" :key="channel.index" class="flex h-11 items-center gap-3 rounded-xl bg-panel pr-2 pl-3">
          <span
            class="grid size-6 shrink-0 place-items-center rounded-full text-xs font-semibold"
            :class="channel.index === hosts.current ? 'bg-ink text-white' : 'bg-fill-strong text-ink-2'">
            {{ channel.index + 1 }}
          </span>
          <InlineRename
            v-if="renaming && channel.index === hosts.current"
            :value="channel.name"
            label="This computer's name on the mouse"
            :maxlength="hosts.maxNameLength"
            @save="rename"
            @cancel="renaming = false" />
          <template v-else>
            <span class="min-w-0 flex-1">
              <span class="block truncate text-sm" :class="channel.paired ? 'font-medium' : 'text-ink-3'">
                {{ channel.paired ? channel.name || "Unnamed computer" : "No computer paired" }}
              </span>
              <span v-if="channel.index === hosts.current" class="block text-xs text-ink-3">This computer</span>
            </span>
            <UiButton
              v-if="channel.index === hosts.current && hosts.canRename"
              variant="ghost"
              size="sm"
              @click="renaming = true">
              Rename
            </UiButton>
            <UiButton v-else-if="channel.index !== hosts.current && channel.paired" variant="secondary" size="sm" @click="switchTo(channel)">
              Switch
            </UiButton>
          </template>
        </li>
      </ul>
      <p class="text-xs text-ink-3">
        Switching sends the mouse to that computer, and it leaves this one. To bring it back, press the Easy-Switch button
        under the mouse. To pair a channel, hold that button until its light blinks fast.
      </p>
    </UiPopoverContent>
  </PopoverRoot>
</template>

<script setup lang="ts">
import { PopoverRoot, PopoverTrigger } from "reka-ui";
import type { HostsInfo } from "~/composables/useAgent";

const { hosts } = defineProps<{ hosts: HostsInfo }>();
const { attempt } = useAgent();

const open = ref(false);
const renaming = ref(false);
watch(open, (isOpen) => !isOpen && (renaming.value = false));

async function switchTo(channel: HostsInfo["channels"][number]) {
  open.value = false;
  if (await attempt("switchHost", { host: channel.index }, "Couldn't switch the mouse"))
    showToast(`Sent the mouse to ${channel.name || `channel ${channel.index + 1}`}`);
}

async function rename(name: string) {
  renaming.value = false;
  await attempt("setHostName", { name }, "Couldn't rename this computer");
}
</script>
