<template>
  <PopoverRoot v-model:open="open">
    <PopoverTrigger
      :aria-label="`${label}: ${current.label}`"
      class="flex h-10 w-full items-center gap-2 rounded-lg bg-field px-3 text-left text-sm ring-1 ring-line ring-inset hover:ring-fill-strong data-[state=open]:ring-ink">
      <span class="min-w-0 flex-1 truncate">{{ current.label }}</span>
      <span v-if="current.hint" class="shrink-0 text-xs text-ink-3">{{ current.hint }}</span>
      <PhCaretDown class="size-4 shrink-0 text-ink-3" />
    </PopoverTrigger>
    <PopoverPortal>
      <PopoverContent
        side="bottom"
        align="start"
        :side-offset="4"
        :collision-padding="12"
        class="z-50 flex max-h-[min(22rem,var(--reka-popover-content-available-height))] w-(--reka-popover-trigger-width) min-w-64 origin-(--reka-popover-content-transform-origin) flex-col overflow-hidden rounded-xl bg-field shadow-lg ring-1 ring-line data-[state=closed]:animate-menu-out data-[state=open]:animate-menu-in"
        @close-auto-focus="onCloseAutoFocus">
        <!-- Picking happens on each item's select, so choosing the current one again still closes the menu. -->
        <ListboxRoot :model-value="modelValue" highlight-on-hover class="flex min-h-0 flex-col">
          <ListboxFilter
            v-model="query"
            auto-focus
            placeholder="Search actions"
            :aria-label="`${label}: search actions`"
            class="h-10 shrink-0 border-b border-line bg-transparent px-3 text-sm outline-none! placeholder:text-ink-3" />
          <UiScrollArea class="max-h-[calc(min(22rem,var(--reka-popover-content-available-height))-2.5rem)]" :viewport-attrs="{ tabindex: -1 }">
          <ListboxContent class="p-1">
            <ListboxGroup v-for="group in groups" :key="group.name">
              <ListboxGroupLabel class="px-3 pt-2 pb-1 text-xs font-medium text-ink-3">{{ group.name }}</ListboxGroupLabel>
              <ListboxItem
                v-for="choice in group.choices"
                :key="choice.id"
                :value="choice.id"
                @select="pick(choice.id)"
                class="relative flex h-9 cursor-default items-center gap-3 rounded-lg pr-8 pl-3 text-sm outline-none data-highlighted:bg-fill">
                <span class="min-w-0 flex-1 truncate">{{ choice.label }}</span>
                <span v-if="choice.hint" class="shrink-0 text-xs text-ink-3">{{ choice.hint }}</span>
                <ListboxItemIndicator class="absolute right-2.5">
                  <PhCheck class="size-4" />
                </ListboxItemIndicator>
              </ListboxItem>
            </ListboxGroup>
            <p v-if="!groups.length" class="px-3 py-6 text-center text-sm text-ink-3">No actions match “{{ query }}”</p>
          </ListboxContent>
          </UiScrollArea>
        </ListboxRoot>
      </PopoverContent>
    </PopoverPortal>
  </PopoverRoot>
</template>

<script setup lang="ts">
import { PhCaretDown, PhCheck } from "@phosphor-icons/vue";
import {
  ListboxContent,
  ListboxFilter,
  ListboxGroup,
  ListboxGroupLabel,
  ListboxItem,
  ListboxItemIndicator,
  ListboxRoot,
  PopoverContent,
  PopoverPortal,
  PopoverRoot,
  PopoverTrigger,
} from "reka-ui";
import type { ButtonAction, ShortcutInfo } from "~/composables/useAgent";

/**
 * Picks an action by what it does: a preset such as "Copy", a saved macro as "macro:<id>", a layer as
 * "layer:momentary" or "layer:toggle", a profile as "profile:<id>", a macOS Shortcut as "appleShortcut:<id>",
 * something to open as "open:app", "open:url" or "open:path", or one of the custom choices: "shortcut" (an inline
 * combination), "hold" (keys held down), and "none". `modelValue` is the ID of the choice shown.
 */
const { modelValue, label, allowHold = true, defaultLabel, layers = "none", profiles = false, currentName } = defineProps<{
  modelValue: string;
  /** The name of the app or Shortcut chosen, for a choice the menu can't list right now. */
  currentName?: string;
  label: string;
  allowHold?: boolean;
  /** Offers "default", what the button does without MX Studio, under this name. */
  defaultLabel?: string;
  /** Which second-layer actions fit here: holding it needs a press that lasts. */
  layers?: "hold" | "toggle" | "none";
  /** Offers switching to each profile that follows no programs. */
  profiles?: boolean;
}>();
const emit = defineEmits<{
  /** For a Shortcut, its name comes along, so the action can say it without looking it up. */
  "update:modelValue": [id: string, name?: string];
  /** A custom choice was picked and needs its keys; the editor records them once the menu has closed. */
  record: [];
}>();

interface Choice {
  id: string;
  label: string;
  hint?: string;
  keywords?: string;
}

const IS_MAC = navigator.userAgent.includes("Mac");
const SHORT_KEYS: Record<string, string> = {
  ControlLeft: "Ctrl",
  ShiftLeft: "Shift",
  AltLeft: IS_MAC ? "⌥" : "Alt",
  MetaLeft: IS_MAC ? "⌘" : "Win",
};

/** The keys a preset presses, shortened, so people learn what it does without it taking over the row. */
function hintFor(action: ButtonAction) {
  if (action.type !== "press" || !action.keys) return undefined;
  if (action.keys.length === 1 && !action.keys[0]!.startsWith("Key")) return undefined;
  return action.keys.map((key) => SHORT_KEYS[key] ?? keyLabel(key)).join(" ");
}

const KEYBOARD: Choice[] = [
  { id: "shortcut", label: "Keyboard shortcut…", hint: "This action", keywords: "custom keys press record combination chord once" },
  { id: "hold", label: "Hold keys…", keywords: "custom keyboard modifier down while held" },
];

const { state } = useAgent();

const choices = computed(() => {
  const all: (Choice & { group: string })[] = defaultLabel
    ? [{ id: "default", group: "This button", label: defaultLabel, hint: "default", keywords: "reset own" }]
    : [];
  for (const choice of KEYBOARD) if (allowHold || choice.id !== "hold") all.push({ ...choice, group: "Keyboard" });
  all.push(...PRESETS.filter(presetAvailable).map((preset) => ({
    id: preset.id,
    group: preset.group,
    label: preset.label,
    hint: hintFor(preset.action),
    keywords: preset.keywords,
  })));
  if (state.value?.opensApps) {
    all.push(
      { id: "open:app", group: "Open", label: "Open app…", keywords: "launch program application start run" },
      { id: "open:url", group: "Open", label: "Open website…", keywords: "url link web browser address page" },
      { id: "open:path", group: "Open", label: "Open file or folder…", keywords: "document directory path explorer finder" },
    );
  }
  if (state.value?.runsShortcuts) {
    for (const shortcut of shortcuts.value ?? [])
      all.push({ id: `appleShortcut:${shortcut.id}`, group: "Shortcuts", label: shortcut.name, hint: "Shortcut", keywords: "shortcuts app automation run" });
    if (modelValue.startsWith("appleShortcut:") && !all.some((c) => c.id === modelValue))
      all.push({ id: modelValue, group: "Shortcuts", label: currentName ?? "Shortcut", hint: shortcuts.value ? "Not found" : "Shortcut" });
    if (shortcuts.value && !shortcuts.value.length)
      all.push({ id: "noShortcuts", group: "Shortcuts", label: "No Shortcuts yet", hint: "Make one in Shortcuts", keywords: "shortcuts" });
  }
  for (const macro of state.value?.macros ?? [])
    all.push({ id: `macro:${macro.id}`, group: "Shared macros", label: macro.name, hint: "Shared", keywords: "macro saved sequence" });
  const group = "Layers & profiles";
  if (layers === "hold")
    all.push({ id: "layer:momentary", group, label: "Hold second layer", hint: "While held", keywords: "layer shift fn momentary" });
  if (layers !== "none")
    all.push({ id: "layer:toggle", group, label: "Toggle second layer", hint: "On / off", keywords: "layer shift fn lock" });
  if (profiles) {
    for (const profile of state.value?.profiles ?? []) {
      if (profile.programs.length) continue;
      all.push({ id: `profile:${profile.id}`, group, label: `Switch to ${profile.name}`, hint: "Profile", keywords: "profile change" });
    }
    // A profile that's gone or follows programs now can't be picked again, but the action still says what it was.
    if (modelValue.startsWith("profile:") && !all.some((c) => c.id === modelValue)) {
      const name = state.value?.profiles.find((p) => `profile:${p.id}` === modelValue)?.name;
      all.push({
        id: modelValue,
        group,
        label: name ? `Switch to ${name}` : "Switch to a deleted profile",
        hint: name ? "Follows programs" : undefined,
      });
    }
  }
  all.push({ id: "none", group: "Other", label: "Do nothing", keywords: "off disable none" });
  return all;
});

const query = ref("");
const open = ref(false);
watch(open, (isOpen) => isOpen && (query.value = ""));

// The Shortcuts app is read each time the menu opens, so one made a moment ago is there.
const { call } = useAgent();
const shortcuts = ref<ShortcutInfo[] | null>(null);
watch(open, async (isOpen) => {
  if (!isOpen || !state.value?.runsShortcuts) return;
  try {
    shortcuts.value = await call<ShortcutInfo[]>("listShortcuts");
  } catch {
    shortcuts.value ??= [];
  }
});

const groups = computed(() => {
  const words = query.value.toLowerCase().split(/\s+/).filter(Boolean);
  const found = choices.value.filter((choice) => {
    const text = `${choice.label} ${choice.group} ${choice.hint ?? ""} ${choice.keywords ?? ""}`.toLowerCase();
    return words.every((word) => text.includes(word));
  });
  const byGroup = new Map<string, Choice[]>();
  for (const choice of found) byGroup.set(choice.group, [...(byGroup.get(choice.group) ?? []), choice]);
  return [...byGroup].map(([name, list]) => ({ name, choices: list }));
});

const current = computed(() => {
  const choice = choices.value.find((c) => c.id === modelValue);
  return { label: choice?.label.replace(/…$/, "") ?? "Choose an action", hint: choice?.hint };
});

let recordAfterClose = false;

function pick(value: string) {
  if (value === "noShortcuts") return;
  recordAfterClose = value === "shortcut" || value === "hold";
  emit("update:modelValue", value, choices.value.find((c) => c.id === value)?.label);
  open.value = false;
}

// Focus would go back to this trigger; when keys are wanted next, the editor's key field takes it instead.
function onCloseAutoFocus(event: Event) {
  if (!recordAfterClose) return;
  recordAfterClose = false;
  event.preventDefault();
  emit("record");
}
</script>
