<template>
  <div
    role="toolbar"
    aria-label="Macro editor controls"
    class="flex h-11 shrink-0 items-center gap-1 border-t border-line px-3"
  >
    <UiTooltip :text="`Undo ${undoLabel || 'edit'} · ${command}Z`"
      ><button
        type="button"
        aria-label="Undo macro edit"
        :disabled="!undoLabel || recording"
        :class="TOOL"
        @click="emit('undo')"
      >
        <PhArrowCounterClockwise class="size-4" /></button
    ></UiTooltip>
    <UiTooltip :text="`Redo ${redoLabel || 'edit'} · ${command}Shift+Z`"
      ><button
        type="button"
        aria-label="Redo macro edit"
        :disabled="!redoLabel || recording"
        :class="TOOL"
        @click="emit('redo')"
      >
        <PhArrowClockwise class="size-4" /></button
    ></UiTooltip>
    <span class="mx-1 h-4 w-px bg-line" />
    <UiTooltip text="Previous step"
      ><button
        type="button"
        aria-label="Previous macro step"
        :class="TOOL"
        :disabled="!hasSteps || recording"
        @click="emit('previous')"
      >
        <PhSkipBack class="size-4" /></button
    ></UiTooltip>
    <UiTooltip text="Visual preview · Space · highlights steps without sending input"
      ><button
        type="button"
        :aria-label="playing ? 'Pause macro preview' : 'Preview macro'"
        :class="TOOL"
        :disabled="!canPreview || recording"
        @click="emit('preview')"
      >
        <component :is="playing ? PhPause : PhPlay" class="size-4" weight="fill" /></button
    ></UiTooltip>
    <UiTooltip text="Next step"
      ><button
        type="button"
        aria-label="Next macro step"
        :class="TOOL"
        :disabled="!hasSteps || recording"
        @click="emit('next')"
      >
        <PhSkipForward class="size-4" /></button
    ></UiTooltip>
    <UiTooltip text="Preview speed"
      ><button
        type="button"
        aria-label="Change preview speed"
        :disabled="recording"
        class="h-7 w-8 rounded text-[11px] text-ink-3 hover:bg-fill disabled:opacity-30"
        @click="emit('speed')"
      >
        {{ speed }}×
      </button></UiTooltip
    >
    <span class="mx-1 h-4 w-px bg-line" />
    <PopoverRoot v-model:open="adding">
      <PopoverTrigger
        :disabled="recording"
        class="flex h-7 items-center gap-1 rounded px-2 text-xs text-ink-2 hover:bg-fill disabled:opacity-30"
        ><PhPlus class="size-3.5" /> Add</PopoverTrigger
      >
      <UiPopoverContent :open="adding" class="w-44 p-1">
        <button
          v-for="item in ADDABLE"
          :key="item.type"
          type="button"
          class="flex h-8 w-full items-center gap-2 rounded-lg px-2 text-left text-xs hover:bg-fill"
          @click="add(item.type)"
        >
          <component :is="item.icon" class="size-4 text-ink-3" />{{ item.label }}
        </button>
      </UiPopoverContent>
    </PopoverRoot>
    <span class="flex-1" />
    <UiCheckbox
      :model-value="keepPauses"
      label="Keep delays"
      @update:model-value="(value) => emit('delays', value)"
    />
    <span class="mx-1 h-4 w-px bg-line" />
    <button
      type="button"
      class="flex h-7 w-22 shrink-0 items-center justify-center gap-1.5 rounded-md border border-line text-xs font-medium"
      :class="recording ? 'bg-danger/10 text-danger' : 'bg-field text-ink hover:bg-fill'"
      @click="recording ? emit('stop') : emit('record')"
    >
      <span :class="recording ? 'size-2 rounded-xs' : 'size-2 rounded-full'" class="bg-danger" />{{
        recording ? "Stop" : "Record"
      }}
    </button>
    <UiTooltip :text="paused ? 'Resume recording · F9' : 'Pause recording · F9'"
      ><button
        type="button"
        :aria-label="paused ? 'Resume recording' : 'Pause recording'"
        :disabled="!recording"
        :class="TOOL"
        @click="emit('pause')"
      >
        <component :is="paused ? PhPlay : PhPause" class="size-4" /></button
    ></UiTooltip>
    <UiTooltip text="Discard this recording"
      ><button
        type="button"
        aria-label="Discard recording"
        :disabled="!recording"
        :class="TOOL"
        @click="emit('cancel')"
      >
        <PhX class="size-4" /></button
    ></UiTooltip>
  </div>
</template>

<script setup lang="ts">
import {
  PhArrowClockwise,
  PhArrowCounterClockwise,
  PhCursorClick,
  PhHourglass,
  PhKeyboard,
  PhPause,
  PhPlay,
  PhPlus,
  PhSkipBack,
  PhSkipForward,
  PhTextT,
  PhX,
} from "@phosphor-icons/vue";
import { PopoverRoot, PopoverTrigger } from "reka-ui";
import type { MacroStep } from "~/composables/useAgent";
defineProps<{
  undoLabel: string;
  redoLabel: string;
  recording: boolean;
  paused: boolean;
  playing: boolean;
  speed: number;
  canPreview: boolean;
  hasSteps: boolean;
  keepPauses: boolean;
}>();
const emit = defineEmits<{
  undo: [];
  redo: [];
  previous: [];
  preview: [];
  next: [];
  speed: [];
  add: [type: MacroStep["type"]];
  record: [];
  stop: [];
  pause: [];
  cancel: [];
  delays: [value: boolean];
}>();
const TOOL =
  "grid size-7 shrink-0 place-items-center rounded-md text-ink-2 hover:bg-fill disabled:opacity-30";
const command = navigator.userAgent.includes("Mac") ? "⌘" : "Ctrl+";
const adding = ref(false);
const ADDABLE = [
  { type: "keys", label: "Key combination", icon: PhKeyboard },
  { type: "text", label: "Text", icon: PhTextT },
  { type: "wait", label: "Delay", icon: PhHourglass },
  { type: "click", label: "Mouse click", icon: PhCursorClick },
] as const;
function add(type: MacroStep["type"]) {
  adding.value = false;
  emit("add", type);
}
</script>
