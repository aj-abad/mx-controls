<template>
  <div class="flex h-full min-w-0 gap-3">
    <section aria-label="Gesture presets" class="flex w-44 shrink-0 flex-col rounded-2xl bg-panel p-2">
      <div class="flex items-center justify-between px-2 py-2">
        <h2 class="text-xs font-semibold text-ink-2">Presets</h2>
        <span class="text-[11px] text-ink-3">{{ cards.length }}</span>
      </div>
      <UiScrollArea class="min-h-0 flex-1">
        <button
          v-for="card in cards"
          :key="card.preset.id"
          type="button"
          :aria-current="card.preset.id === preset?.id ? 'true' : undefined"
          class="mb-1 flex w-full items-start gap-2 rounded-xl px-2 py-2.5 text-left"
          :class="card.preset.id === preset?.id ? 'bg-field ring-1 ring-line ring-inset' : 'hover:bg-fill/60'"
          @click="openId = card.preset.id"
        >
          <PresetCompass :preset="card.preset" :size="28" class="mt-0.5 shrink-0" />
          <span class="flex min-w-0 flex-1 flex-col gap-0.5">
            <span class="truncate text-[13px] font-medium">{{ card.preset.name }}</span>
            <span class="text-[11px] text-ink-3">{{ card.tag }}{{ card.used ? ` · ${card.used}` : "" }}</span>
          </span>
        </button>
      </UiScrollArea>
      <button
        type="button"
        class="mt-2 flex h-9 shrink-0 items-center justify-center gap-1.5 rounded-xl border border-dashed border-fill-strong text-xs font-medium text-ink-2 hover:bg-fill"
        @click="createNew"
      >
        <PhPlus class="size-3.5" /> New preset
      </button>
    </section>

    <section
      v-if="preset && draft"
      :aria-label="preset.name"
      class="flex min-w-0 flex-1 flex-col overflow-hidden rounded-2xl border border-line bg-panel"
    >
      <header class="flex shrink-0 items-center gap-2 border-b border-line px-5 py-3">
        <div class="min-w-0 flex-1">
          <h2 v-if="isBuiltIn" class="text-base font-semibold">{{ preset.name }}</h2>
          <input
            v-else
            :value="preset.name"
            aria-label="Preset name"
            maxlength="60"
            class="-ml-1.5 w-full rounded-md bg-transparent px-1.5 text-base font-semibold outline-none hover:ring-1 hover:ring-line focus:ring-1 focus:ring-accent"
            @change="rename"
          />
          <p class="mt-0.5 truncate text-[11px] text-ink-3">
            {{ countsText(draft) }} · {{ uses.length }} {{ uses.length === 1 ? "use" : "uses" }} ·
            {{ unfinished ? "Unfinished changes" : "Changes save automatically" }}
          </p>
        </div>
        <UiButton
          variant="ghost"
          size="sm"
          class="px-2.5! text-xs!"
          :disabled="!!unfinished"
          @click="duplicate"
          >Duplicate</UiButton
        >
        <UiButton
          v-if="isBuiltIn"
          variant="ghost"
          size="sm"
          class="px-2.5! text-xs!"
          :disabled="!isChanged"
          @click="presets.discard(preset)"
          >Reset</UiButton
        >
        <UiButton v-else variant="ghost" size="sm" class="px-2.5! text-xs!" @click="askDelete"
          >Delete</UiButton
        >
        <PopoverRoot v-model:open="settingsOpen">
          <PopoverTrigger
            aria-label="Movement settings"
            class="grid size-7 shrink-0 place-items-center rounded-md text-ink-3 hover:bg-fill"
            ><PhSlidersHorizontal class="size-4"
          /></PopoverTrigger>
          <UiPopoverContent :open="settingsOpen" align="end" class="w-76 p-4"
            ><UiSlider
              v-model="spacing"
              label="Distance between sequence steps"
              description="Applies to continued movements in a sequence. The first movement and each turn use 2 mm."
              :min="MIN_SPACING_MM"
              :max="MAX_SPACING_MM"
              :step="0.5"
              :default-value="DEFAULT_SPACING_MM"
              :format="(mm) => `${mm} mm`"
              @commit="(mm) => (draft!.spacingMm = mm)"
          /></UiPopoverContent>
        </PopoverRoot>
      </header>

      <div class="gesture-workspace min-h-0 flex-1 overflow-hidden">
        <UiScrollArea content-class="flex flex-col gap-3 px-5 py-4">
          <div class="flex items-center justify-between">
            <h3 class="text-xs font-medium text-ink-2">Mouse movement</h3>
            <span class="text-[11px] text-ink-3">{{
              totalPaths ? "Includes sequences" : "4 simple movements"
            }}</span>
          </div>
          <GestureMap :preset="draft" :selected="selectedPath" @select="selectPath" />
          <p class="text-center text-[11px] leading-relaxed text-ink-3">
            Hold the button, then move the mouse.
          </p>

          <div v-if="totalPaths" class="flex flex-col gap-1.5 border-t border-line pt-3">
            <h3 class="text-xs font-medium text-ink-2">Sequences</h3>
            <button
              v-for="item in sequences"
              :key="pathKey(item.path)"
              type="button"
              class="flex items-center gap-2 rounded-lg px-2 py-1.5 text-left text-xs hover:bg-fill"
              :class="pathKey(item.path) === selectedKey && 'bg-accent/10'"
              @click="selectPath(item.path)"
            >
              <span
                class="shrink-0 font-medium text-accent"
                :aria-label="item.path.map((d) => DIRECTION_LABELS[d]).join(' then ')"
                >{{ pathText(item.path) }}</span
              >
              <span class="truncate text-ink-2">{{
                complete(actionOf(item.step)) ? describeAction(actionOf(item.step)) : "Choose an action"
              }}</span>
            </button>
          </div>
          <details class="border-t border-line pt-3">
            <summary class="cursor-pointer text-xs font-medium text-ink-2">
              {{
                uses.length
                  ? `Used by ${uses.length} ${uses.length === 1 ? "control" : "controls"}`
                  : "How to use this preset"
              }}
            </summary>
            <ul v-if="uses.length" class="mt-2 flex flex-col gap-1 text-xs text-ink-3">
              <li v-for="use in uses" :key="use">{{ use }}</li>
            </ul>
            <p class="mt-2 text-xs leading-relaxed text-ink-3">
              {{
                uses.length
                  ? "Editing this shared preset updates every control listed above."
                  : "In Profiles, select a button and choose this preset under Gestures while held."
              }}
            </p>
            <button
              type="button"
              class="mt-2 text-xs font-medium text-ink hover:underline"
              @click="goTo('profiles')"
            >
              Go to Profiles →
            </button>
          </details>
        </UiScrollArea>

        <UiScrollArea class="border-l border-line" content-class="px-5 py-4">
          <GestureMoveEditor
            ref="moveEditor"
            :preset="draft"
            :path="selectedPath"
            @select="selectPath"
            @change="setStep"
            @extend="extendPath"
            @remove="removePath"
          />
        </UiScrollArea>
      </div>
      <footer
        class="flex h-12 shrink-0 items-center border-t border-line px-5 text-xs"
        :class="unfinished ? 'text-warn' : 'text-ink-3'"
        role="status"
      >
        <p class="line-clamp-2">
          {{
            unfinished
              ? `Choose an action for ${unfinished} to save. Assigned buttons keep the last saved version until then.`
              : note || "Changes update every control that uses this preset."
          }}
        </p>
      </footer>
      <DialogRoot v-model:open="confirming"
        ><DialogPortal
          ><DialogOverlay class="fixed inset-0 z-50 bg-black/25" /><DialogContent
            class="fixed top-1/2 left-1/2 z-50 w-90 -translate-x-1/2 -translate-y-1/2 rounded-2xl bg-field p-5 shadow-xl ring-1 ring-line"
            ><DialogTitle class="text-base font-semibold">Delete “{{ preset.name }}”?</DialogTitle
            ><DialogDescription class="mt-2 text-sm leading-relaxed text-ink-3"
              >{{ uses.join(" and ") }}
              {{
                uses.length === 1
                  ? "keeps its clicks and loses its movements"
                  : "keep their clicks and lose their movements"
              }}.</DialogDescription
            >
            <div class="mt-5 flex justify-end gap-2">
              <UiButton variant="secondary" size="sm" @click="confirming = false">Cancel</UiButton
              ><UiButton size="sm" @click="deletePreset">Delete preset</UiButton>
            </div></DialogContent
          ></DialogPortal
        ></DialogRoot
      >
    </section>
  </div>
</template>

<script setup lang="ts">
import { PhPlus, PhSlidersHorizontal } from "@phosphor-icons/vue";
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
import type { ButtonAction, Direction, GesturePreset } from "~/composables/useAgent";

const { openPreset: openId, goTo } = usePage();
const { lastGesture, state } = useAgent();
const presets = useGesturePresets();
const preset = computed(() => presets.find(openId.value) ?? presets.presets.value[0]);
const isBuiltIn = computed(() => !!preset.value && !!presets.builtIn(preset.value.id));
const isChanged = computed(() => !!preset.value && presets.changed(preset.value));
const uses = computed(() => (preset.value ? presets.usedBy(preset.value.id) : []));
const cards = computed(() =>
  presets.presets.value.map((p) => {
    const count = presets.usedBy(p.id).length;
    return {
      preset: p,
      tag: presets.builtIn(p.id) ? (presets.changed(p) ? "Built-in, edited" : "Built-in") : "Custom",
      used: count ? `${count} ${count === 1 ? "use" : "uses"}` : "",
    };
  }),
);

const draft = ref<GesturePreset | null>(null);
const note = ref("");
const confirming = ref(false);
const settingsOpen = ref(false);
const selectedKey = ref("up");
const spacing = ref(DEFAULT_SPACING_MM);
const moveEditor = useTemplateRef("moveEditor");
const copy = (p: GesturePreset): GesturePreset => JSON.parse(JSON.stringify(p));
function resetDraft() {
  draft.value = preset.value ? copy(preset.value) : null;
  spacing.value = preset.value?.spacingMm ?? DEFAULT_SPACING_MM;
  // Undo or an external edit may remove the selected step.
  if (draft.value && !stepAt(draft.value, selectedKey.value.split(",") as Direction[]))
    selectedKey.value = "up";
}
watch(() => canonical(preset.value ?? null), resetDraft, { immediate: true });
watch(
  () => preset.value?.id,
  () => {
    note.value = "";
    confirming.value = false;
    selectedKey.value = "up";
  },
);

const rows = computed(() => (draft.value ? pathRows(draft.value) : []));
const selectedPath = computed(() => selectedKey.value.split(",") as Direction[]);
const sequences = computed(() => rows.value.filter((row) => row.path.length > 1 && !row.leadsOn));
const totalPaths = computed(() => sequences.value.length);
const unfinished = computed(() => {
  const row = rows.value.find((r) => !complete(actionOf(r.step)));
  return row ? row.path.map((d) => DIRECTION_LABELS[d]).join(" → ") : null;
});
watch(
  () => canonical(draft.value),
  async (now) => {
    const d = draft.value;
    if (!d || unfinished.value || now === canonical(preset.value ?? null)) return;
    if (!(await presets.change(d))) resetDraft();
  },
);

function selectPath(path: Direction[]) {
  selectedKey.value = pathKey(path);
  note.value = "";
}
function setStep(path: Direction[], action: ButtonAction) {
  draft.value = withStep(draft.value!, path, (step) => withAction(step, action));
  selectedKey.value = pathKey(path);
}
function removePath(path: Direction[]) {
  draft.value = withStep(draft.value!, path, () => null);
  selectedKey.value = pathKey(path.slice(0, -1));
  void nextTick(() => moveEditor.value?.focusAction());
}
function extendPath(path: Direction[]) {
  if (shapeProblem(path) || fitProblem(draft.value!, path)) return;
  const changed = addingChanges(draft.value!, path);
  draft.value = addPath(draft.value!, path);
  selectedKey.value = pathKey(path);
  void nextTick(() => moveEditor.value?.focusAction());
  note.value = changed
    ? `${pathText(path.slice(0, -1))} now leads on, so its held key became a single action, or it stopped repeating.`
    : "";
}
function rename(event: Event) {
  const input = event.target as HTMLInputElement;
  const name = input.value.trim();
  if (name) draft.value = { ...draft.value!, name };
  else input.value = draft.value!.name;
}
async function createNew() {
  const id = await presets.create(presets.blank());
  if (id) openId.value = id;
}
async function duplicate() {
  const { id: _, ...rest } = draft.value!;
  const id = await presets.create({ ...rest, name: `${rest.name} copy` });
  if (id) openId.value = id;
}
function askDelete() {
  if (uses.value.length) confirming.value = true;
  else void deletePreset();
}
async function deletePreset() {
  if (await presets.discard(preset.value!)) openId.value = null;
}
watch(lastGesture, (gesture) => {
  if (!preset.value || !gesture?.path.length || !draft.value || !stepAt(draft.value, gesture.path)) return;
  const follows = state.value?.presetUses.some(
    (use) =>
      use.preset === preset.value!.id &&
      use.profile === state.value?.activeProfile &&
      use.button === gesture.button,
  );
  if (follows) selectedKey.value = pathKey(gesture.path);
});
</script>

<style scoped>
.gesture-workspace {
  display: grid;
  grid-template-columns: minmax(0, 1.05fr) minmax(0, 1fr);
  grid-template-rows: minmax(0, 1fr);
}
</style>
