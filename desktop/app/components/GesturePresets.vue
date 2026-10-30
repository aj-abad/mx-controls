<template>
  <div class="flex h-full gap-4">
    <section aria-label="Gesture presets" class="flex w-72 shrink-0 flex-col gap-1 overflow-y-auto rounded-2xl bg-panel p-2.5">
      <button
        v-for="card in cards"
        :key="card.preset.id"
        type="button"
        :aria-current="card.preset.id === preset?.id ? 'true' : undefined"
        class="flex w-full items-center gap-3 rounded-xl py-1.5 pr-2.5 pl-1.5 text-left"
        :class="card.preset.id === preset?.id ? 'bg-fill ring-1 ring-line ring-inset' : 'hover:bg-fill/60'"
        @click="openId = card.preset.id">
        <PresetCompass :preset="card.preset" :size="40" class="shrink-0" />
        <span class="flex min-w-0 flex-1 flex-col">
          <span class="flex items-center gap-1.5 text-sm font-medium">
            <span class="truncate">{{ card.preset.name }}</span>
            <span v-if="card.tag" class="shrink-0 rounded-md bg-fill px-1.5 py-px text-[10.5px] font-semibold text-ink-3">{{ card.tag }}</span>
          </span>
          <span class="truncate text-xs text-ink-3">{{ card.used }}</span>
        </span>
      </button>
      <button type="button" :class="GHOST" @click="createNew">+ New preset</button>
    </section>

    <section v-if="preset && draft" :aria-label="preset.name" class="flex min-w-0 flex-1 flex-col rounded-2xl bg-panel">
      <header class="flex items-start gap-2 px-6 pt-5 pb-4">
        <div class="min-w-0 flex-1">
          <h2 v-if="isBuiltIn" class="text-xl font-semibold">{{ preset.name }}</h2>
          <input
            v-else
            :value="preset.name"
            aria-label="Preset name"
            class="-ml-1.5 w-full rounded-md bg-transparent px-1.5 py-0.5 text-xl font-semibold outline-none hover:ring-1 hover:ring-line focus:ring-1 focus:ring-accent"
            @change="rename" />
          <p class="mt-0.5 text-sm text-ink-3">
            {{ countsText(draft) }} · {{ isBuiltIn ? (isChanged ? "built-in, changed" : "built-in") : "yours" }}. {{ usage }}
          </p>
        </div>
        <UiButton variant="ghost" size="sm" @click="duplicate">Duplicate</UiButton>
        <UiButton v-if="isBuiltIn" variant="ghost" size="sm" :disabled="!isChanged" @click="presets.discard(preset)">Reset to built-in</UiButton>
        <UiButton v-else variant="ghost" size="sm" @click="askDelete">Delete</UiButton>
      </header>

      <div class="flex min-h-0 flex-1 gap-8 overflow-y-auto px-6 pb-6">
        <div class="flex w-56 shrink-0 flex-col gap-5">
          <PresetCompass :preset="draft" :size="224" :weight="4" :highlight="selectedPath" />
          <UiSlider
            v-model="spacing"
            label="Spacing between steps"
            description="How far apart the steps of a run are. Wider is easier to hit; narrower is quicker."
            :min="MIN_SPACING_MM"
            :max="MAX_SPACING_MM"
            :step="0.5"
            :default-value="DEFAULT_SPACING_MM"
            :format="(mm) => `${mm} mm`"
            @commit="(mm) => (draft!.spacingMm = mm)" />
        </div>

        <section class="flex min-w-0 flex-1 flex-col gap-1.5" aria-label="Moves">
          <TriggerRow
            v-for="row in rows"
            :key="pathKey(row.path)"
            :label="row.path.length === 1 ? DIRECTION_LABELS[row.path[0]!] : ''"
            :path="row.path"
            :indent="row.path.length - 1"
            :caption="row.instant ? 'instant' : `on release${row.leadsOn ? ' · leads on' : ''}`"
            :selected="pathKey(row.path) === selectedKey"
            selectable
            :select-label="`Show ${pathText(row.path)} on the compass`"
            @select="selectedKey = pathKey(row.path)">
            <div class="flex items-start gap-1">
              <ActionEditor
                :model-value="actionOf(row.step)"
                class="min-w-0 flex-1"
                :label="`${draft.name} ${pathText(row.path)}`"
                :allow-hold="row.instant"
                :allow-repeat="row.instant"
                repeat-label="Repeat as you keep moving"
                @update:model-value="(action) => setStep(row.path, action)" />
              <UiButton
                v-if="row.path.length > 1"
                variant="ghost"
                size="sm"
                class="mt-1"
                :aria-label="`Remove ${pathText(row.path)} and the steps beyond it`"
                @click="removePath(row.path)">
                <PhX class="size-4" />
              </UiButton>
            </div>
          </TriggerRow>

          <div
            v-if="recording"
            class="mt-2 flex flex-col gap-2.5 rounded-xl bg-fill/60 p-3"
            role="group"
            aria-label="Draw a path"
            @keydown="onRecorderKey">
            <div class="flex min-h-6 items-center gap-2">
              <span class="flex gap-0.5" aria-live="polite">
                <b v-for="(d, i) in recording.path" :key="i" class="flex size-5 items-center justify-center rounded-md bg-fill-strong text-xs font-semibold">
                  {{ GLYPHS[d] }}
                </b>
              </span>
              <span class="h-4.5 w-0.5 bg-accent" />
              <span class="ml-auto text-xs text-ink-3">{{ recording.path.length ? "Runs when you let go" : "Draw the path with the arrows" }}</span>
            </div>
            <div class="flex items-center gap-1.5">
              <UiButton v-for="d in DIRECTIONS" :key="d" size="sm" :aria-label="DIRECTION_LABELS[d]" @click="record(d)">{{ GLYPHS[d] }}</UiButton>
              <UiButton variant="ghost" size="sm" aria-label="Remove the last step" @click="unrecord"><PhBackspace class="size-4" /></UiButton>
              <span class="flex-1" />
              <UiButton variant="ghost" size="sm" class="whitespace-nowrap" @click="recording = null">Cancel</UiButton>
              <UiButton size="sm" class="whitespace-nowrap" @click="addRecorded">Add path</UiButton>
            </div>
            <p v-if="recording.problem" class="text-xs text-warn" role="alert">{{ recording.problem }}</p>
          </div>
          <div class="mt-2 flex flex-wrap items-center gap-2">
            <UiButton v-if="!recording" size="sm" @click="startRecording">+ Add a path</UiButton>
          </div>

          <p v-if="unfinished" class="mt-2 text-sm text-warn">
            Choose an action for “{{ unfinished }}” to save this preset.{{ uses.length ? " Until then, its buttons keep the last saved version." : "" }}
          </p>
          <p v-if="note" class="mt-2 text-sm text-ink-3" role="status">{{ note }}</p>
          <div v-if="confirming" class="mt-2 flex flex-col gap-2 rounded-xl bg-warn-fill p-3 text-[13px] text-warn" role="alertdialog" aria-label="Delete preset">
            <p>Delete “{{ preset.name }}”? {{ uses.join(" and ") }} {{ uses.length === 1 ? "keeps its clicks and loses its moves" : "keep their clicks and lose their moves" }}.</p>
            <div class="flex justify-end gap-1.5">
              <UiButton variant="ghost" size="sm" @click="confirming = false">Cancel</UiButton>
              <UiButton size="sm" @click="deletePreset">Delete preset</UiButton>
            </div>
          </div>
        </section>
      </div>
    </section>
  </div>
</template>

<script setup lang="ts">
import { PhBackspace, PhX } from "@phosphor-icons/vue";
import type { ButtonAction, Direction, GesturePreset } from "~/composables/useAgent";

/** The Gestures page: every preset on the left, the one open on the right. */
const { openPreset: openId } = usePage();

const GHOST =
  "flex h-11 w-full shrink-0 items-center rounded-xl border border-dashed border-fill-strong px-3.5 text-sm text-ink-3 hover:border-ink-3 hover:text-ink";

const { lastGesture, state } = useAgent();
const presets = useGesturePresets();

const preset = computed(() => presets.find(openId.value) ?? presets.presets.value[0]);
const isBuiltIn = computed(() => !!preset.value && !!presets.builtIn(preset.value.id));
const isChanged = computed(() => !!preset.value && presets.changed(preset.value));
const uses = computed(() => (preset.value ? presets.usedBy(preset.value.id) : []));
const usage = computed(() => {
  const list = uses.value;
  if (!list.length) return "No button uses it yet; pick it under “Move while holding” on a button.";
  return `Used by ${list.join(" and ")}. Changes apply to ${list.length === 1 ? "it" : "all of them"} straight away.`;
});

const cards = computed(() =>
  presets.presets.value.map((p) => {
    const used = presets.usedBy(p.id);
    return {
      preset: p,
      tag: presets.builtIn(p.id) ? (presets.changed(p) ? "Built-in, changed" : "Built-in") : "",
      used: !used.length ? "Not used yet" : used.length === 1 ? `Used by ${used[0]}` : `Used by ${used[0]} and ${used.length - 1} more`,
      counts: countsText(p),
    };
  }),
);

async function createNew() {
  const id = await presets.create(presets.blank());
  if (id) openId.value = id;
}

// The preset as edited: changes go to the agent as they're made once nothing is left unset, as a button's do.
const draft = ref<GesturePreset | null>(null);
const note = ref("");
const confirming = ref(false);
const selectedKey = ref("");
const recording = ref<{ path: Direction[]; problem: string } | null>(null);
const spacing = ref(DEFAULT_SPACING_MM);

// The agent's state can hold the reactive draft of an earlier edit, which structuredClone refuses.
const copy = (p: GesturePreset): GesturePreset => JSON.parse(JSON.stringify(p));

watch(
  () => canonical(preset.value ?? null),
  () => {
    draft.value = preset.value ? copy(preset.value) : null;
    spacing.value = preset.value?.spacingMm ?? DEFAULT_SPACING_MM;
  },
  { immediate: true },
);
watch(() => preset.value?.id, () => {
  note.value = "";
  confirming.value = false;
  recording.value = null;
  selectedKey.value = "";
});

const rows = computed(() => (draft.value ? pathRows(draft.value) : []));
const selectedPath = computed(() => (selectedKey.value ? (selectedKey.value.split(",") as Direction[]) : null));

const unfinished = computed(() => {
  const row = rows.value.find((r) => !complete(actionOf(r.step)));
  return row && pathText(row.path);
});

watch(
  () => canonical(draft.value),
  async (now) => {
    const d = draft.value;
    if (!d || unfinished.value || now === canonical(preset.value ?? null)) return;
    if (!(await presets.change(d))) draft.value = preset.value ? copy(preset.value) : null;
  },
);

function setStep(path: Direction[], action: ButtonAction) {
  draft.value = withStep(draft.value!, path, (step) => withAction(step, action));
  selectedKey.value = pathKey(path);
}

function removePath(path: Direction[]) {
  draft.value = withStep(draft.value!, path, () => null);
  if (selectedKey.value.startsWith(pathKey(path))) selectedKey.value = "";
}

function rename(event: Event) {
  const name = (event.target as HTMLInputElement).value.trim();
  if (name) draft.value = { ...draft.value!, name };
  else (event.target as HTMLInputElement).value = draft.value!.name;
}

function startRecording() {
  recording.value = { path: [], problem: "" };
  selectedKey.value = "";
}

function record(d: Direction) {
  const r = recording.value!;
  const path = [...r.path, d];
  const problem = shapeProblem(path);
  recording.value = problem ? { ...r, problem } : { path, problem: "" };
}

function unrecord() {
  recording.value = { path: recording.value!.path.slice(0, -1), problem: "" };
}

const ARROWS: Record<string, Direction> = { ArrowUp: "up", ArrowDown: "down", ArrowLeft: "left", ArrowRight: "right" };
function onRecorderKey(event: KeyboardEvent) {
  const d = ARROWS[event.key];
  if (d) {
    event.preventDefault();
    record(d);
  } else if (event.key === "Backspace") {
    event.preventDefault();
    unrecord();
  } else if (event.key === "Enter") {
    event.preventDefault();
    addRecorded();
  }
}

function addRecorded() {
  const r = recording.value!;
  if (!r.path.length) return void (recording.value = { ...r, problem: "Draw at least one step first." });
  const problem = fitProblem(draft.value!, r.path);
  if (problem) return void (recording.value = { ...r, problem });
  // A first direction that already led on couldn't hold or repeat, so only one that didn't can lose it.
  const first = stepAt(draft.value!, r.path.slice(0, 1))!;
  const tapped = r.path.length > 1 && (first.type === "hold" || !!first.repeat);
  draft.value = addPath(draft.value!, r.path);
  note.value = tapped
    ? `${DIRECTION_LABELS[r.path[0]!]} now runs when you let go, so it no longer holds its key or repeats.`
    : "";
  selectedKey.value = pathKey(r.path);
  recording.value = null;
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

// Using a button that follows this preset picks out the path it took.
watch(lastGesture, (gesture) => {
  const p = preset.value;
  if (!p || !gesture?.path.length) return;
  const follows = state.value?.presetUses.some(
    (use) => use.preset === p.id && use.profile === state.value?.activeProfile && use.button === gesture.button,
  );
  if (follows) selectedKey.value = pathKey(gesture.path);
});
</script>
