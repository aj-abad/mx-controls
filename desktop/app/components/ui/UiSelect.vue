<template>
  <SelectRoot v-model="value">
    <SelectTrigger
      :aria-label="label"
      class="flex h-10 w-full items-center justify-between gap-2 rounded-lg bg-field px-3 text-sm ring-1 ring-line ring-inset hover:ring-fill-strong data-[state=open]:ring-ink">
      <SelectValue :placeholder="placeholder" />
      <PhCaretDown class="size-4 text-ink-3" />
    </SelectTrigger>
    <SelectPortal>
      <SelectContent
        position="popper"
        :side-offset="4"
        class="z-50 max-h-72 min-w-(--reka-select-trigger-width) origin-(--reka-select-content-transform-origin) overflow-hidden rounded-xl bg-field p-1 shadow-lg ring-1 ring-line data-[state=closed]:animate-menu-out data-[state=open]:animate-menu-in">
        <SelectViewport>
          <SelectItem
            v-for="option in options"
            :key="option.value"
            :value="option.value"
            class="relative flex h-9 cursor-default items-center rounded-lg pr-8 pl-3 text-sm outline-none data-highlighted:bg-fill">
            <SelectItemText>{{ option.label }}</SelectItemText>
            <SelectItemIndicator class="absolute right-2.5">
              <PhCheck class="size-4" />
            </SelectItemIndicator>
          </SelectItem>
        </SelectViewport>
      </SelectContent>
    </SelectPortal>
  </SelectRoot>
</template>

<script setup lang="ts">
import { PhCaretDown, PhCheck } from "@phosphor-icons/vue";
import {
  SelectContent,
  SelectItem,
  SelectItemIndicator,
  SelectItemText,
  SelectPortal,
  SelectRoot,
  SelectTrigger,
  SelectValue,
  SelectViewport,
} from "reka-ui";

/** The accessible name; the trigger shows the chosen option instead. */
defineProps<{ options: { value: string; label: string }[]; label: string; placeholder?: string }>();
const value = defineModel<string>({ required: true });
</script>
