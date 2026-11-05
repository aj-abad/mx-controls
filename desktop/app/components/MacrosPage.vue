<template>
  <div class="flex h-full min-w-0 gap-3" @keydown="onKey">
    <section aria-label="Macros" class="flex w-44 shrink-0 flex-col rounded-2xl bg-panel p-2">
      <h2 class="px-2 py-2 text-xs font-semibold text-ink-2">Saved macros</h2>
      <UiScrollArea class="min-h-0 flex-1">
        <UiContextMenu v-for="item in list" :key="item.id" :items="macroMenu(item.id)">
          <button
            type="button"
            :disabled="recording"
            :aria-current="item.id === currentId ? 'true' : undefined"
            class="mb-1 flex w-full flex-col gap-1 rounded-xl px-2 py-2.5 text-left disabled:opacity-50"
            :class="item.id === currentId ? 'bg-field ring-1 ring-line ring-inset' : 'hover:bg-fill/60'"
            @click="openId = item.id"
          >
            <span class="flex w-full items-center gap-1 text-[13px] font-medium"
              ><span class="truncate">{{ item.name }}</span
              ><span
                v-if="item.draft"
                class="ml-auto size-1.5 shrink-0 rounded-full bg-accent"
                aria-label="Unsaved draft"
            /></span>
            <span class="w-full truncate text-[11px] text-ink-3">{{ item.summary }}</span>
          </button>
        </UiContextMenu>
      </UiScrollArea>
      <button
        type="button"
        :disabled="recording"
        class="mt-2 flex h-9 shrink-0 items-center justify-center gap-1.5 rounded-xl border border-dashed border-fill-strong text-xs font-medium text-ink-2 hover:bg-fill disabled:opacity-30"
        @click="create"
      >
        <PhPlus class="size-3.5" /> New macro
      </button>
    </section>

    <section
      v-if="draft"
      :aria-label="draft.name"
      class="flex min-h-0 min-w-0 flex-1 flex-col overflow-hidden rounded-2xl border border-line bg-panel"
    >
      <header class="flex h-15 shrink-0 items-center gap-2 px-4">
        <div class="min-w-0 flex-1">
          <input
            ref="nameField"
            :value="draft.name"
            aria-label="Macro name"
            :disabled="recording"
            maxlength="60"
            class="-ml-1.5 w-full rounded-md bg-transparent px-1.5 text-base font-semibold outline-none hover:ring-1 hover:ring-line focus:ring-1 focus:ring-accent"
            @change="rename"
          />
          <p class="mt-0.5 truncate text-[11px] text-ink-3" :title="usage">
            {{ draft.steps.length }} {{ draft.steps.length === 1 ? "step" : "steps" }} ·
            {{ macroMs(draft) }} ms of delays · {{ usage }}
          </p>
        </div>
        <PopoverRoot v-model:open="moreOpen">
          <PopoverTrigger
            :disabled="recording"
            aria-label="Macro actions"
            class="grid size-7 shrink-0 place-items-center rounded-md text-ink-2 hover:bg-fill disabled:opacity-30"
            ><PhDotsThree class="size-5" weight="bold"
          /></PopoverTrigger>
          <UiPopoverContent :open="moreOpen" align="end" class="w-48 p-1">
            <button
              v-for="item in headerActions"
              :key="item.label"
              type="button"
              :disabled="item.disabled"
              class="flex h-8 w-full items-center gap-2 rounded-lg px-2 text-left text-xs hover:bg-fill disabled:opacity-30"
              :class="item.danger && 'text-danger'"
              @click="
                moreOpen = false;
                item.select();
              "
            >
              <component :is="item.icon" class="size-4" />{{ item.label }}
            </button>
          </UiPopoverContent>
        </PopoverRoot>
      </header>

      <MacroToolbar
        :undo-label="undoLabel"
        :redo-label="redoLabel"
        :recording="recording"
        :paused="paused"
        :playing="playing"
        :speed="speed"
        :can-preview="!problem"
        :has-steps="!!draft.steps.length"
        :keep-pauses="keepPauses"
        @undo="undoEdit"
        @redo="redoEdit"
        @previous="jump(-1)"
        @next="jump(1)"
        @preview="togglePreview"
        @speed="speed = speed === 2 ? 0.5 : speed * 2"
        @add="add"
        @record="startRecording"
        @stop="recorder.stop(true)"
        @pause="paused ? recorder.resume() : recorder.pause()"
        @cancel="recorder.stop(false)"
        @delays="(value) => (keepPauses = value)"
      />
      <MacroTimeline
        :steps="shownSteps"
        :selected="selection"
        :playhead="playhead"
        :disabled="recording"
        :menu="stepMenu"
        @select="select"
        @context="context"
        @seek="seek"
        @retime="retime"
        @resize="resizeDelay"
      />

      <div class="grid min-h-0 flex-1 grid-cols-[minmax(0,1fr)_14rem]">
        <MacroStepList
          :steps="shownSteps"
          :selected="selection"
          :cursor="selectedIndex"
          :playing="previewIndex"
          :disabled="recording"
          :menu="stepMenu"
          @select="select"
          @context="context"
          @move="move"
          @edit="inspector?.focus()"
        />
        <MacroStepInspector
          ref="inspector"
          :steps="draft.steps"
          :selected="selection"
          :disabled="recording"
          @update="update"
          @scale="scaleDelays"
        />
      </div>
      <footer
        class="flex h-8 shrink-0 items-center gap-2 border-t border-line px-3 text-[11px]"
        role="status"
        aria-live="polite"
      >
        <span
          class="size-1.5 shrink-0 rounded-full"
          :class="recording ? 'bg-danger' : problem ? 'bg-warn' : 'bg-good'"
        />
        <span
          class="min-w-0 flex-1 truncate"
          :class="problem ? 'text-warn' : 'text-ink-3'"
          :title="feedback"
          >{{ feedback }}</span
        >
        <button
          v-if="document?.error && !recording"
          type="button"
          aria-label="Retry saving macro"
          class="w-24 shrink-0 text-right font-medium text-ink hover:underline"
          @click="editor.save(document!)"
        >
          Retry save
        </button>
        <span v-else class="w-24 shrink-0 truncate text-right text-ink-3" :title="status">{{
          recording ? (paused ? "Paused" : "Recording") : status
        }}</span>
      </footer>
    </section>

    <section v-else class="grid min-w-0 flex-1 place-items-center rounded-2xl border border-line bg-panel">
      <div class="max-w-xs px-5 text-center">
        <PhKeyboard class="mx-auto mb-3 size-8 text-ink-3" />
        <h2 class="text-base font-semibold">Make a macro</h2>
        <p class="mt-2 text-sm leading-relaxed text-ink-3">
          Record key combinations, add text and clicks, and adjust their timing.
        </p>
        <div class="mt-4 flex justify-center gap-2">
          <UiButton variant="secondary" :disabled="!undoLabel" @click="undoEdit">Undo</UiButton
          ><UiButton @click="create">New macro</UiButton>
        </div>
      </div>
    </section>

    <DialogRoot v-model:open="confirming">
      <DialogPortal
        ><DialogOverlay class="fixed inset-0 z-50 bg-black/25" /><DialogContent
          class="fixed top-1/2 left-1/2 z-50 w-90 -translate-x-1/2 -translate-y-1/2 rounded-2xl bg-field p-5 shadow-xl ring-1 ring-line"
        >
          <DialogTitle class="text-base font-semibold">Delete “{{ draft?.name }}”?</DialogTitle>
          <DialogDescription class="mt-2 text-sm leading-relaxed text-ink-3"
            >{{ usage }}. Assigned controls will do nothing until you undo the deletion.</DialogDescription
          >
          <div class="mt-5 flex justify-end gap-2">
            <UiButton variant="secondary" size="sm" @click="confirming = false">Cancel</UiButton
            ><UiButton size="sm" @click="deleteMacro">Delete macro</UiButton>
          </div>
        </DialogContent></DialogPortal
      >
    </DialogRoot>
  </div>
</template>

<script setup lang="ts">
import { PhDotsThree, PhKeyboard, PhPlus } from "@phosphor-icons/vue";
import {
  DialogContent,
  DialogDescription,
  DialogOverlay,
  DialogPortal,
  DialogRoot,
  DialogTitle,
  PopoverRoot,
  PopoverTrigger,
} from "reka-ui";
const {
  editor,
  currentId,
  document,
  draft,
  selection,
  selectedIndex,
  problem,
  status,
  openId,
  inspector,
  nameField,
  moreOpen,
  confirming,
  usage,
  list,
  undoLabel,
  redoLabel,
  undoEdit,
  redoEdit,
  headerActions,
  macroMenu,
  recording,
  paused,
  keepPauses,
  recorder,
  shownSteps,
  feedback,
  create,
  playing,
  speed,
  previewIndex,
  playhead,
  jump,
  togglePreview,
  add,
  startRecording,
  stepMenu,
  select,
  context,
  seek,
  retime,
  resizeDelay,
  move,
  update,
  scaleDelays,
  onKey,
  rename,
  deleteMacro,
} = useMacroWorkspace();
</script>
