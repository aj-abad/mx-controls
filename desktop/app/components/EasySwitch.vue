<template>
  <PopoverRoot v-model:open="open">
    <!-- Anchored by hand: inside the context menu, the trigger would anchor the menu instead of the popover. -->
    <PopoverAnchor as="template" :reference="trigger?.$el" />
    <UiContextMenu :items="triggerMenu">
      <PopoverTrigger
        ref="trigger"
        :aria-label="`Easy-Switch: channel ${hosts.current + 1}, ${current?.name || 'this computer'}`"
        :title="current?.name || undefined"
        class="group flex h-8 max-w-full min-w-0 items-center gap-2 rounded-full pr-2.5 pl-1.5 text-sm text-ink-2 ring-1 ring-line ring-inset transition-colors duration-150 hover:bg-fill hover:text-ink data-[state=open]:bg-fill data-[state=open]:text-ink">
        <ChannelBadge :index="hosts.current" current class="size-5 text-[11px]" />
        <span class="truncate font-medium">{{ current?.name || "This computer" }}</span>
        <PhCaretDown class="size-3.5 shrink-0 text-ink-3 transition-transform duration-150 group-data-[state=open]:rotate-180" />
      </PopoverTrigger>
    </UiContextMenu>

    <UiPopoverContent :open="open" align="start" class="flex w-[26rem] flex-col gap-3 p-3">
      <h2 class="px-1 pt-1 text-xs font-medium text-ink-3">Easy-Switch</h2>
      <ul aria-label="Easy-Switch channels" class="grid grid-cols-3 gap-2">
        <li v-for="channel in hosts.channels" :key="channel.index">
          <UiContextMenu :items="channelMenu(channel)">
            <div
              class="relative flex h-32 flex-col rounded-xl p-3 transition-colors duration-150"
              :class="[
                channel.index === hosts.current ? 'bg-panel ring-2 ring-ink' : channel.paired ? 'bg-panel hover:bg-fill' : 'ring-1 ring-line ring-dashed',
              ]">
              <ChannelBadge :index="channel.index" :current="channel.index === hosts.current" class="size-6 text-xs" />
              <component
                :is="channel.paired ? PhLaptop : PhPlus"
                class="mt-auto mb-1.5 size-6"
                :class="channel.paired ? 'text-ink-2' : 'text-ink-3'" />
              <InlineRename
                v-if="renaming && channel.index === hosts.current"
                class="-mx-1 flex-none"
                :value="channel.name"
                label="This computer's name on the mouse"
                :maxlength="hosts.maxNameLength"
                @save="rename"
                @cancel="renaming = false" />
              <template v-else>
                <span class="truncate text-sm" :class="channel.paired ? 'font-medium' : 'text-ink-3'">
                  {{ channel.paired ? channel.name || "Unnamed computer" : "Empty" }}
                </span>
                <span class="text-xs text-ink-3">{{ status(channel) }}</span>
              </template>
              <!-- The whole card is the button; the rename field sits above it when it's open. -->
              <button
                v-if="!(renaming && channel.index === hosts.current)"
                type="button"
                class="absolute inset-0 rounded-xl disabled:cursor-default"
                :disabled="!channel.paired"
                :aria-label="action(channel)"
                @click="activate(channel)" />
            </div>
          </UiContextMenu>
        </li>
      </ul>
      <section v-if="others.length" aria-labelledby="easy-switch-shortcuts" class="flex flex-col gap-2 border-t border-line px-1 pt-3">
        <h3 id="easy-switch-shortcuts" class="text-xs font-medium text-ink-3">Keyboard shortcuts</h3>
        <div v-for="channel in others" :key="channel.index" class="flex items-center gap-2">
          <ChannelBadge :index="channel.index" :current="false" class="size-6 shrink-0 text-xs" />
          <span class="w-24 shrink-0 truncate text-sm" :title="label(channel)">{{ label(channel) }}</span>
          <div class="min-w-0 flex-1">
            <ShortcutCapture
              :model-value="shortcut(channel)"
              :label="`Shortcut to switch to ${label(channel)}`"
              @update:model-value="setShortcut(channel, $event)" />
          </div>
          <button
            v-if="shortcut(channel)"
            type="button"
            class="flex size-8 shrink-0 items-center justify-center rounded-lg text-ink-3 transition-colors duration-150 hover:bg-fill hover:text-ink"
            :aria-label="`Remove the shortcut for ${label(channel)}`"
            @click="setShortcut(channel, null)">
            <PhX class="size-4" />
          </button>
        </div>
        <p class="text-xs text-ink-3">
          These work from any app while the mouse is on this computer. To switch back, set shortcuts in MX Studio on the
          other computer too.
        </p>
      </section>
      <p class="px-1 pb-1 text-xs text-ink-3">
        To pair another computer, hold the Easy-Switch button under the mouse until its light blinks fast, then connect
        from that computer.
      </p>
    </UiPopoverContent>
  </PopoverRoot>
</template>

<script setup lang="ts">
import { PhArrowsLeftRight, PhCaretDown, PhLaptop, PhPencilSimple, PhPlus, PhX } from "@phosphor-icons/vue";
import { PopoverAnchor, PopoverRoot, PopoverTrigger } from "reka-ui";
import type { HostsInfo } from "~/composables/useAgent";
import type { MenuEntry } from "~/utils/menu";

type Channel = HostsInfo["channels"][number];

const { hosts } = defineProps<{ hosts: HostsInfo }>();
const { attempt, state } = useAgent();

const open = ref(false);
const trigger = useTemplateRef("trigger");
const renaming = ref(false);
watch(open, (isOpen) => !isOpen && (renaming.value = false));

const current = computed(() => hosts.channels.find((c) => c.index === hosts.current));
const label = (channel: Channel) => channel.name || `channel ${channel.index + 1}`;

function status(channel: Channel) {
  if (channel.index === hosts.current) return "This computer";
  return channel.paired ? "Paired" : "No computer";
}

function action(channel: Channel) {
  if (channel.index === hosts.current) return hosts.canRename ? "Rename this computer" : "This computer";
  return `Switch the mouse to ${label(channel)}`;
}

function activate(channel: Channel) {
  if (channel.index !== hosts.current) void switchTo(channel);
  else if (hosts.canRename) startRename();
}

function startRename() {
  open.value = true;
  renaming.value = true;
}

async function switchTo(channel: Channel) {
  open.value = false;
  await attempt("switchHost", { host: channel.index }, "Couldn't switch the mouse");
}

// The mouse is out of reach from here once it's on another channel, so this computer's own channel needs no shortcut.
const others = computed(() => hosts.channels.filter((c) => c.paired && c.index !== hosts.current));

const shortcut = (channel: Channel) => state.value?.hostShortcuts?.find((s) => s.host === channel.index)?.keys ?? null;

async function setShortcut(channel: Channel, keys: string[] | null) {
  await attempt("setHostShortcut", { host: channel.index, keys }, "Couldn't set that shortcut");
}

async function rename(name: string) {
  renaming.value = false;
  await attempt("setHostName", { name }, "Couldn't rename this computer");
}

const renameItem = computed<MenuEntry[]>(() =>
  hosts.canRename ? [{ label: "Rename this computer…", icon: PhPencilSimple, select: startRename }] : [],
);

function channelMenu(channel: Channel): MenuEntry[] {
  if (channel.index === hosts.current) return renameItem.value;
  if (!channel.paired) return [];
  return [{ label: `Switch to ${label(channel)}`, icon: PhArrowsLeftRight, select: () => switchTo(channel) }];
}

const triggerMenu = computed<MenuEntry[]>(() => {
  const others = hosts.channels
    .filter((c) => c.paired && c.index !== hosts.current)
    .map((c): MenuEntry => ({ label: `Switch to ${label(c)}`, icon: PhArrowsLeftRight, select: () => switchTo(c) }));
  return [...others, ...(others.length && renameItem.value.length ? ["separator" as const] : []), ...renameItem.value];
});
</script>
