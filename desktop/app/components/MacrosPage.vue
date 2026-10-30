<template>
  <div class="flex h-full gap-4">
    <section aria-label="Macros" class="flex w-72 shrink-0 flex-col gap-1 overflow-y-auto rounded-2xl bg-panel p-2.5">
      <button
        v-for="item in list"
        :key="item.id"
        type="button"
        :aria-current="item.id === current?.id ? 'true' : undefined"
        class="flex w-full flex-col gap-1 rounded-xl px-3 py-2 text-left"
        :class="item.id === current?.id ? 'bg-fill ring-1 ring-line ring-inset' : 'hover:bg-fill/60'"
        @click="openId = item.id">
        <span class="flex items-center gap-2 text-sm font-medium">
          <span class="truncate">{{ item.name }}</span>
          <span class="ml-auto shrink-0 text-xs font-normal text-ink-3">{{ item.note }}</span>
        </span>
        <span class="truncate text-xs text-ink-3">{{ item.summary }}</span>
      </button>
      <button type="button" :class="GHOST" @click="startNew">+ New macro</button>
    </section>

    <section v-if="draft" :aria-label="draft.name" class="flex min-w-0 flex-1 flex-col rounded-2xl bg-panel">
      <header class="flex items-start gap-2 px-6 pt-5 pb-4">
        <div class="min-w-0 flex-1">
          <input
            :value="draft.name"
            aria-label="Macro name"
            class="-ml-1.5 w-full rounded-md bg-transparent px-1.5 py-0.5 text-xl font-semibold outline-none hover:ring-1 hover:ring-line focus:ring-1 focus:ring-accent"
            @change="rename" />
          <p class="mt-0.5 text-sm text-ink-3">{{ summary }}</p>
        </div>
        <UiButton variant="ghost" size="sm" :disabled="isNew" @click="duplicate">Duplicate</UiButton>
        <UiButton variant="ghost" size="sm" @click="askDelete">{{ isNew ? "Discard" : "Delete" }}</UiButton>
      </header>

      <div class="flex min-h-0 flex-1 flex-col gap-5 overflow-y-auto px-6 pb-6">
        <MacroTimeline :steps="shownSteps" :selected="selectedIndex" @select="(i) => (selectedIndex = i)" />

        <div v-if="recording" class="flex flex-col gap-2.5 rounded-xl bg-fill/60 p-3" role="group" aria-label="Recording">
          <div class="flex items-center gap-2 text-sm">
            <span class="size-2.5 animate-pulse rounded-full bg-danger" aria-hidden="true" />
            <span class="font-medium">Recording</span>
            <span class="text-ink-3">{{ recorded.steps.length ? `${recorded.steps.length} steps so far` : "Press the keys to play back" }}</span>
            <span class="flex-1" />
            <UiCheckbox v-model="keepPauses" label="Keep the pauses" />
          </div>
          <p v-if="recorded.trimmed" class="text-xs text-warn">The pauses were cut to keep the macro within one second.</p>
          <div class="flex justify-end gap-1.5">
            <UiButton variant="ghost" size="sm" @click="stopRecording(false)">Cancel</UiButton>
            <UiButton size="sm" @click="stopRecording(true)">Stop and add</UiButton>
          </div>
        </div>

        <ol class="flex flex-col gap-1" aria-label="Steps">
          <li
            v-for="(step, i) in draft.steps"
            :key="i"
            class="flex items-start gap-2 rounded-xl p-1.5"
            :class="i === selectedIndex && 'bg-accent/8 ring-1 ring-accent/30'"
            @focusin="selectedIndex = i">
            <span class="mt-2.5 w-5 shrink-0 text-right text-xs text-ink-3 tabular-nums">{{ i + 1 }}</span>
            <span class="mt-2.5 w-14 shrink-0 text-sm font-medium">{{ STEP_LABELS[step.type] }}</span>
            <div class="min-w-0 flex-1">
              <ShortcutCapture
                v-if="step.type === 'keys'"
                :model-value="step.keys"
                :label="`Step ${i + 1} keys`"
                @update:model-value="(keys) => setStep(i, { ...step, keys })" />
              <textarea
                v-else-if="step.type === 'text'"
                :value="step.text ?? ''"
                rows="2"
                :aria-label="`Step ${i + 1} text`"
                placeholder="Text to type, as written"
                class="block w-full resize-y rounded-lg bg-field px-3 py-2 text-sm ring-1 ring-line outline-none ring-inset focus:ring-2 focus:ring-accent"
                @change="(e) => setStep(i, { ...step, text: (e.target as HTMLTextAreaElement).value })" />
              <label v-else-if="step.type === 'wait'" class="flex h-10 items-center gap-2 text-sm">
                <input
                  type="number"
                  :value="step.ms ?? 0"
                  min="1"
                  :max="MACRO_MAX_MS"
                  step="10"
                  :aria-label="`Step ${i + 1} wait in milliseconds`"
                  class="h-10 w-24 rounded-lg bg-field px-3 text-sm tabular-nums ring-1 ring-line outline-none ring-inset focus:ring-2 focus:ring-accent"
                  @change="(e) => setStep(i, { ...step, ms: Math.round(Number((e.target as HTMLInputElement).value)) })" />
                <span class="text-ink-3">ms</span>
              </label>
              <UiSelect
                v-else
                :model-value="step.mouse ?? 'middle'"
                :options="MOUSE_OPTIONS"
                :label="`Step ${i + 1} mouse button`"
                @update:model-value="(mouse) => setStep(i, { ...step, mouse })" />
              <p v-if="stepProblem(step)" class="mt-1 text-xs text-warn">{{ stepProblem(step) }}</p>
            </div>
            <div class="flex shrink-0 gap-0.5">
              <UiButton variant="ghost" size="sm" :disabled="i === 0" :aria-label="`Move step ${i + 1} earlier`" @click="move(i, -1)">
                <PhArrowUp class="size-4" />
              </UiButton>
              <UiButton
                variant="ghost"
                size="sm"
                :disabled="i === draft.steps.length - 1"
                :aria-label="`Move step ${i + 1} later`"
                @click="move(i, 1)">
                <PhArrowDown class="size-4" />
              </UiButton>
              <UiButton variant="ghost" size="sm" :aria-label="`Remove step ${i + 1}`" @click="removeStep(i)">
                <PhX class="size-4" />
              </UiButton>
            </div>
          </li>
        </ol>

        <div class="flex flex-wrap items-center gap-1.5">
          <UiButton size="sm" :disabled="!!recording" @click="startRecording">
            <span class="size-2 rounded-full bg-danger" aria-hidden="true" />
            Record
          </UiButton>
          <span class="mx-1 h-5 w-px bg-line" />
          <UiButton v-for="kind in ADDABLE" :key="kind.type" variant="ghost" size="sm" @click="addStep(kind.step())">+ {{ kind.label }}</UiButton>
        </div>

        <p v-if="problem" class="text-sm text-warn">{{ problem }}</p>
        <div v-if="confirming" class="flex flex-col gap-2 rounded-xl bg-warn-fill p-3 text-[13px] text-warn" role="alertdialog" aria-label="Delete macro">
          <p>Delete “{{ draft.name }}”? {{ uses.join(", ") }} will do nothing until you undo it.</p>
          <div class="flex justify-end gap-1.5">
            <UiButton variant="ghost" size="sm" @click="confirming = false">Cancel</UiButton>
            <UiButton size="sm" @click="deleteMacro">Delete macro</UiButton>
          </div>
        </div>
      </div>
    </section>

    <section v-else class="grid flex-1 place-items-center rounded-2xl bg-panel">
      <div class="flex max-w-sm flex-col items-center gap-3 text-center">
        <p class="text-sm text-ink-3">
          A macro plays back keys, text, waits and mouse clicks, within one second. Any button, gesture step or thumb
          wheel action can run it.
        </p>
        <UiButton @click="startNew">+ New macro</UiButton>
      </div>
    </section>
  </div>
</template>

<script setup lang="ts">
import { PhArrowDown, PhArrowUp, PhX } from "@phosphor-icons/vue";
import type { Macro, MacroStep } from "~/composables/useAgent";
import type { RecordedKey } from "~/utils/macroRecording";

const GHOST =
  "flex h-11 w-full shrink-0 items-center rounded-xl border border-dashed border-fill-strong px-3.5 text-sm text-ink-3 hover:border-ink-3 hover:text-ink";
const STEP_LABELS: Record<MacroStep["type"], string> = { keys: "Keys", text: "Text", wait: "Wait", click: "Click" };
const MOUSE_OPTIONS = [
  { value: "middle", label: "Middle click" },
  { value: "back", label: "Back click" },
  { value: "forward", label: "Forward click" },
];
const ADDABLE: { type: MacroStep["type"]; label: string; step: () => MacroStep }[] = [
  { type: "keys", label: "Keys", step: () => ({ type: "keys", keys: [] }) },
  { type: "text", label: "Text", step: () => ({ type: "text", text: "" }) },
  { type: "wait", label: "Wait", step: () => ({ type: "wait", ms: 50 }) },
  { type: "click", label: "Click", step: () => ({ type: "click", mouse: "middle" }) },
];

const { openMacro: openId } = usePage();
const macros = useMacros();

/** A macro made here but not saved yet, because it has nothing in it the agent would take. */
const fresh = ref<Macro | null>(null);
const isNew = computed(() => !!fresh.value && current.value?.id === fresh.value.id);
const current = computed<Macro | undefined>(() =>
  fresh.value && openId.value === fresh.value.id ? fresh.value : (macros.find(openId.value) ?? macros.macros.value[0]),
);

const list = computed(() => {
  const saved = macros.macros.value.map((m) => {
    const used = macros.usedBy(m.id).length;
    return { id: m.id, name: m.name, summary: stepsText(m.steps), note: used ? `${used} use${used === 1 ? "" : "s"}` : "unused" };
  });
  return fresh.value ? [...saved, { id: fresh.value.id, name: fresh.value.name, summary: "Not saved yet", note: "" }] : saved;
});

function stepsText(steps: MacroStep[]) {
  if (steps.length === 1 && steps[0]!.type === "keys") return chordLabel(steps[0]!.keys);
  const ms = macroMs({ steps });
  return `${steps.length} steps${ms ? ` · ${ms} ms` : ""}`;
}

// The macro as edited: changes go to the agent as they're made once it's complete, as a button's do.
const draft = ref<Macro | null>(null);
const selectedIndex = ref(-1);
const confirming = ref(false);
const copy = (m: Macro): Macro => JSON.parse(JSON.stringify(m));

watch(
  () => canonical(current.value ?? null),
  () => (draft.value = current.value ? copy(current.value) : null),
  { immediate: true },
);
watch(
  () => current.value?.id,
  () => {
    selectedIndex.value = -1;
    confirming.value = false;
    void stopRecording(false);
  },
);

const uses = computed(() => (current.value && !isNew.value ? macros.usedBy(current.value.id) : []));
const summary = computed(() => {
  const d = draft.value!;
  const ms = macroMs(d);
  const length = `${d.steps.length} step${d.steps.length === 1 ? "" : "s"}${ms ? `, waiting ${ms} of ${MACRO_MAX_MS} ms` : ""}`;
  if (isNew.value) return `${length}. Saved once every step is filled in.`;
  const list = uses.value;
  if (!list.length) return `${length}. Nothing runs it yet; pick it in any action menu.`;
  return `${length}. Run by ${list.join(", ")}.`;
});

/** What stops the agent taking it, or null. */
const problem = computed(() => {
  const d = draft.value;
  if (!d) return null;
  if (!d.steps.length) return "Add a step, or record some keys.";
  if (macroMs(d) > MACRO_MAX_MS) return `It waits ${macroMs(d)} ms in all; a macro lasts at most one second.`;
  const index = d.steps.findIndex((s) => stepProblem(s));
  return index >= 0 ? `Step ${index + 1} isn't finished, so changes wait until it is.` : null;
});

watch(
  () => canonical(draft.value),
  async (now) => {
    const d = draft.value;
    if (!d || problem.value || now === canonical(current.value ?? null)) return;
    const wasNew = isNew.value;
    if (!(await macros.change(d))) {
      draft.value = current.value ? copy(current.value) : null;
      return;
    }
    if (wasNew) fresh.value = null;
  },
);

function setStep(index: number, step: MacroStep) {
  const steps = [...draft.value!.steps];
  steps[index] = step;
  draft.value = { ...draft.value!, steps };
  if (isNew.value) fresh.value = draft.value;
}

function addStep(step: MacroStep, at = draft.value!.steps.length) {
  draft.value = { ...draft.value!, steps: [...draft.value!.steps.slice(0, at), step, ...draft.value!.steps.slice(at)] };
  if (isNew.value) fresh.value = draft.value;
  selectedIndex.value = at;
}

function removeStep(index: number) {
  draft.value = { ...draft.value!, steps: draft.value!.steps.filter((_, i) => i !== index) };
  if (isNew.value) fresh.value = draft.value;
  selectedIndex.value = -1;
}

function move(index: number, by: number) {
  const steps = [...draft.value!.steps];
  const [step] = steps.splice(index, 1);
  steps.splice(index + by, 0, step!);
  draft.value = { ...draft.value!, steps };
  if (isNew.value) fresh.value = draft.value;
  selectedIndex.value = index + by;
}

function rename(event: Event) {
  const input = event.target as HTMLInputElement;
  const name = input.value.trim();
  if (!name) return void (input.value = draft.value!.name);
  draft.value = { ...draft.value!, name };
  if (isNew.value) fresh.value = draft.value;
}

function startNew() {
  fresh.value = { id: macros.newId(), name: "New macro", steps: [] };
  openId.value = fresh.value.id;
  void startRecording();
}

async function duplicate() {
  const d = draft.value!;
  const twin = { ...copy(d), id: macros.newId(), name: `${d.name} copy` };
  if (await macros.change(twin)) openId.value = twin.id;
}

function askDelete() {
  if (isNew.value) {
    fresh.value = null;
    openId.value = null;
  } else if (uses.value.length) confirming.value = true;
  else void deleteMacro();
}

async function deleteMacro() {
  if (await macros.discard(current.value!)) openId.value = null;
}

// Recording: keys go to the agent while it has the keyboard, so shortcuts such as Win reach the recording rather
// than Windows; without it, the page listens itself.
const { captureKeys } = useAgent();
const recording = ref<{ keys: RecordedKey[] } | null>(null);
const keepPauses = ref(false);
let release: (() => void) | null = null;
let session = 0;

const shownSteps = computed(() => (recording.value ? [...draft.value!.steps, ...recorded.value.steps] : (draft.value?.steps ?? [])));
const recorded = computed(() =>
  recording.value
    ? stepsFrom(recording.value.keys, keepPauses.value, MACRO_MAX_MS - macroMs(draft.value!))
    : { steps: [], trimmed: false },
);

function hear(code: string | null, down: boolean) {
  if (!code || code === "Unidentified" || !recording.value) return;
  recording.value = { keys: [...recording.value.keys, { code, down, at: performance.now() }] };
}

const FLAGS = [
  ["ctrlKey", "ControlLeft"],
  ["shiftKey", "ShiftLeft"],
  ["altKey", "AltLeft"],
  ["metaKey", "MetaLeft"],
] as const;

function onKey(event: KeyboardEvent) {
  event.preventDefault();
  event.stopPropagation();
  if (release || event.repeat || !recording.value) return;
  const down = event.type === "keydown";
  // Some sources, such as remote desktops, send a modifier only as a flag on the next key, never as a key of its own.
  if (down) {
    const held = [...heldKeys(recording.value.keys)];
    for (const [flag, code] of FLAGS) {
      const same = held.filter((k) => k.startsWith(code.slice(0, -4)));
      if (event[flag] && !same.length) hear(code, true);
      else if (!event[flag] && event.code !== code) same.forEach((k) => hear(k, false));
    }
  }
  hear(event.code, down);
}

function heldKeys(keys: RecordedKey[]) {
  const held = new Set<string>();
  for (const { code, down } of keys) {
    if (down) held.add(code);
    else held.delete(code);
  }
  return held;
}

async function startRecording() {
  recording.value = { keys: [] };
  window.addEventListener("keydown", onKey, { capture: true });
  window.addEventListener("keyup", onKey, { capture: true });
  (document.activeElement as HTMLElement | null)?.blur();
  const mine = ++session;
  const handBack = await captureKeys({ key: ({ code, down }) => hear(code, down), ended: () => void stopRecording(true) });
  if (mine === session) release = handBack;
  else handBack?.();
}

function stopRecording(keep: boolean) {
  if (!recording.value) return;
  const steps = keep ? recorded.value.steps : [];
  session++;
  window.removeEventListener("keydown", onKey, { capture: true });
  window.removeEventListener("keyup", onKey, { capture: true });
  release?.();
  release = null;
  recording.value = null;
  if (steps.length) {
    draft.value = { ...draft.value!, steps: [...draft.value!.steps, ...steps] };
    if (isNew.value) fresh.value = draft.value;
  }
}

onBeforeUnmount(() => stopRecording(false));
</script>
