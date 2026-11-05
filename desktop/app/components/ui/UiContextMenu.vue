<template>
  <ContextMenuRoot :modal="false">
    <!-- Its own box-less element rather than as-child, so the slot can be a component (a tooltip, say) and not
         only an element. -->
    <ContextMenuTrigger class="contents" :disabled="!items.length">
      <slot />
    </ContextMenuTrigger>
    <ContextMenuPortal>
      <!-- Focus stays where the right-click left it; handing it back to the trigger would close a popover the
           item just opened. -->
      <ContextMenuContent
        :collision-padding="8"
        class="z-50 min-w-48 origin-(--reka-context-menu-content-transform-origin) rounded-xl bg-field p-1 shadow-lg ring-1 ring-line data-[state=closed]:animate-menu-out data-[state=open]:animate-menu-in"
        @close-auto-focus.prevent>
        <UiScrollArea class="max-h-[calc(var(--reka-context-menu-content-available-height,100dvh)-0.5rem)]" :viewport-attrs="{ tabindex: -1 }">
        <template v-for="(entry, i) in items" :key="i">
          <ContextMenuSeparator v-if="entry === 'separator'" class="mx-2 my-1 h-px bg-line" />
          <ContextMenuItem
            v-else
            :disabled="entry.disabled"
            class="flex h-8 cursor-default items-center gap-2.5 rounded-lg pr-3 pl-2.5 text-sm outline-none data-disabled:opacity-40"
            :class="entry.danger ? 'text-danger data-highlighted:bg-danger data-highlighted:text-white' : 'data-highlighted:bg-fill'"
            @select="run(entry)">
            <component :is="entry.icon ?? 'span'" class="size-4 shrink-0" :class="!entry.danger && 'text-ink-2'" />
            <span class="flex-1 truncate">{{ entry.label }}</span>
            <span v-if="entry.shortcut" class="ml-4 text-xs text-ink-3">{{ entry.shortcut }}</span>
            <PhCheck v-if="entry.checked" class="size-4 shrink-0" />
          </ContextMenuItem>
        </template>
        </UiScrollArea>
      </ContextMenuContent>
    </ContextMenuPortal>
  </ContextMenuRoot>
</template>

<script setup lang="ts">
import { PhCheck } from "@phosphor-icons/vue";
import {
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuPortal,
  ContextMenuRoot,
  ContextMenuSeparator,
  ContextMenuTrigger,
} from "reka-ui";
import type { MenuAction, MenuEntry } from "~/utils/menu";

/** The slot is the trigger, and takes the menu's right-click and context-menu key. No items, no menu. */
defineProps<{ items: MenuEntry[] }>();

// After the click that chose it has finished, so a popover it opens doesn't take that click as one outside it.
function run(entry: MenuAction) {
  setTimeout(entry.select);
}
</script>
