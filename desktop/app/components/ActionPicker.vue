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
          <ListboxContent class="min-h-0 overflow-y-auto p-1">
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
import type { ButtonAction, Macro } from "~/composables/useAgent";

/**
 * Picks an action by what it does: a preset such as "Copy", a saved macro as "macro:<id>", or one of the custom
 * choices: "newMacro", "hold" (a key), "none", and "shortcut" for keys recorded before macros. `modelValue` is the
 * ID of the choice shown.
 */
const { modelValue, label, allowHold = true, defaultLabel } = defineProps<{
  modelValue: string;
  label: string;
  allowHold?: boolean;
  /** Offers "default", what the button does without MX Controls, under this name. */
  defaultLabel?: string;
}>();
const emit = defineEmits<{
  "update:modelValue": [id: string];
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

// A macro plays back what it was given; a key stays down as long as the button.
const CUSTOM: Choice[] = [
  { id: "newMacro", label: "New macro…", keywords: "custom keys press record keyboard shortcut" },
  { id: "hold", label: "Key…", keywords: "custom keys modifier hold down while held" },
  { id: "none", label: "Do nothing", keywords: "off disable none" },
];
// Keys recorded straight onto an action, from before macros; only shown for an action that still has them.
const RECORDED: Choice = { id: "shortcut", label: "Recorded keys…", keywords: "custom keys" };

const { state } = useAgent();

const choices = computed(() => {
  const all: (Choice & { group: string })[] = defaultLabel
    ? [{ id: "default", group: "This button", label: defaultLabel, hint: "default", keywords: "reset own" }]
    : [];
  all.push(...PRESETS.map((preset) => ({
    id: preset.id,
    group: preset.group,
    label: preset.label,
    hint: hintFor(preset.action),
    keywords: preset.keywords,
  })));
  for (const macro of state.value?.macros ?? [])
    all.push({ id: `macro:${macro.id}`, group: "Macros", label: macro.name, hint: macroHint(macro), keywords: "macro" });
  for (const choice of CUSTOM) if (allowHold || choice.id !== "hold") all.push({ ...choice, group: "Custom" });
  if (modelValue === "shortcut") all.push({ ...RECORDED, group: "Custom" });
  return all;
});

const query = ref("");
const open = ref(false);
watch(open, (isOpen) => isOpen && (query.value = ""));

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

/** A one-step macro shows its keys, like a built-in action does. */
function macroHint(macro: Macro) {
  const [only] = macro.steps;
  return macro.steps.length === 1 && only?.type === "keys" && only.keys ? hintFor({ type: "press", keys: only.keys }) : undefined;
}

let recordAfterClose = false;

function pick(value: string) {
  recordAfterClose = value === "shortcut" || value === "hold" || value === "newMacro";
  emit("update:modelValue", value);
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
