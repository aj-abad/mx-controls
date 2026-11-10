<template>
  <PopoverRoot v-model:open="open">
    <PopoverTrigger
      :aria-label="`${label}: ${name || 'choose an app'}`"
      class="flex h-10 w-full items-center gap-2 rounded-lg bg-field px-3 text-left text-sm ring-1 ring-line ring-inset hover:ring-fill-strong data-[state=open]:ring-ink">
      <PhAppWindow class="size-4 shrink-0 text-ink-3" />
      <span class="min-w-0 flex-1 truncate" :class="!name && 'text-ink-3'">{{ name || "Choose an app" }}</span>
      <PhCaretDown class="size-4 shrink-0 text-ink-3" />
    </PopoverTrigger>
    <PopoverPortal>
      <PopoverContent
        side="bottom"
        align="start"
        :side-offset="4"
        :collision-padding="12"
        class="z-50 flex max-h-[min(22rem,var(--reka-popover-content-available-height))] w-(--reka-popover-trigger-width) min-w-64 origin-(--reka-popover-content-transform-origin) flex-col overflow-hidden rounded-xl bg-field shadow-lg ring-1 ring-line data-[state=closed]:animate-menu-out data-[state=open]:animate-menu-in">
        <ListboxRoot :model-value="id" highlight-on-hover class="flex min-h-0 flex-col">
          <ListboxFilter
            v-model="query"
            auto-focus
            placeholder="Search apps"
            :aria-label="`${label}: search apps`"
            class="h-10 shrink-0 border-b border-line bg-transparent px-3 text-sm outline-none! placeholder:text-ink-3" />
          <UiScrollArea class="max-h-[calc(min(22rem,var(--reka-popover-content-available-height))-2.5rem)]" :viewport-attrs="{ tabindex: -1 }">
            <ListboxContent class="p-1">
              <p v-if="!apps" class="px-3 py-6 text-center text-sm text-ink-3">Looking for apps…</p>
              <ListboxItem
                v-for="app in found"
                :key="app.id"
                :value="app.id"
                class="relative flex h-9 cursor-default items-center gap-3 rounded-lg pr-8 pl-3 text-sm outline-none data-highlighted:bg-fill"
                @select="pick(app)">
                <span class="min-w-0 flex-1 truncate">{{ app.name }}</span>
                <ListboxItemIndicator class="absolute right-2.5">
                  <PhCheck class="size-4" />
                </ListboxItemIndicator>
              </ListboxItem>
              <p v-if="apps && !found.length" class="px-3 py-6 text-center text-sm text-ink-3">
                {{ query ? `No apps match “${query}”` : "No apps found. Use Open file or folder with the app's path." }}
              </p>
            </ListboxContent>
          </UiScrollArea>
        </ListboxRoot>
      </PopoverContent>
    </PopoverPortal>
  </PopoverRoot>
</template>

<script setup lang="ts">
import { PhAppWindow, PhCaretDown, PhCheck } from "@phosphor-icons/vue";
import {
  ListboxContent,
  ListboxFilter,
  ListboxItem,
  ListboxItemIndicator,
  ListboxRoot,
  PopoverContent,
  PopoverPortal,
  PopoverRoot,
  PopoverTrigger,
} from "reka-ui";
import type { AppInfo } from "~/composables/useAgent";

/** Picks one of the apps the agent lists; `id` is the app's ID, without the `app:` an action stores. */
const { id, name, label, autoOpen = false } = defineProps<{
  id: string;
  name?: string | null;
  label: string;
  /** Opens the list as soon as it appears, for an action that has just become "open an app". */
  autoOpen?: boolean;
}>();
const emit = defineEmits<{ pick: [app: AppInfo] }>();

const open = ref(false);
const query = ref("");
const { call } = useAgent();

// Listing apps can take a second, so the list is kept for the session once read.
const apps = shallowRef<AppInfo[] | null>(cachedApps);
watch(open, async (isOpen) => {
  if (!isOpen) return;
  query.value = "";
  if (cachedApps) return;
  try {
    cachedApps = await call<AppInfo[]>("listApps");
  } catch {
    cachedApps = [];
  }
  apps.value = cachedApps;
});
// Waits for the action menu to finish closing; opening during its exit counts as a click outside and closes again.
let opening: ReturnType<typeof setTimeout> | undefined;
onMounted(() => {
  if (autoOpen && !id) opening = setTimeout(() => (open.value = true), 160);
});
onBeforeUnmount(() => clearTimeout(opening));

const found = computed(() => {
  const words = query.value.toLowerCase().split(/\s+/).filter(Boolean);
  return (apps.value ?? []).filter((app) => words.every((word) => app.name.toLowerCase().includes(word)));
});

function pick(app: AppInfo) {
  emit("pick", app);
  open.value = false;
}
</script>

<script lang="ts">
let cachedApps: AppInfo[] | null = null;
</script>
