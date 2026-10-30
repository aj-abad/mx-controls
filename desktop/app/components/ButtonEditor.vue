<template>
  <div class="flex flex-col gap-6">
    <section class="flex flex-col gap-1.5" :aria-label="`${button.name} clicks`">
      <h3 class="text-xs font-medium text-ink-3">{{ preset ? "Press without moving" : "Press" }}</h3>
      <TriggerRow label="Click" :dots="1" :caption="preset || waits ? 'on release' : undefined" :fired="fired === 1">
        <ActionEditor
          :model-value="draft.click"
          :label="`${button.name} click`"
          :default-label="defaultLabel"
          :allow-hold="!waits"
          :allow-repeat="!waits"
          @update:model-value="(action) => setClick(action)"
          @default="setClick(null)" />
      </TriggerRow>
      <TriggerRow v-if="downgraded" label="">
        <p class="rounded-lg bg-warn-fill px-2.5 py-1.5 text-[13px] text-warn" role="status">{{ downgraded }}</p>
      </TriggerRow>

      <TriggerRow label="Double click" :dots="2" :muted="!draft.double" :fired="fired === 2">
        <div v-if="draft.double" class="flex items-start gap-1">
          <ActionEditor
            v-model="draft.double"
            class="min-w-0 flex-1"
            :label="`${button.name} double click`"
            :allow-hold="false"
            :allow-repeat="false" />
          <UiButton variant="ghost" size="sm" aria-label="Remove the double click" class="mt-1" @click="removeDouble">
            <PhX class="size-4" />
          </UiButton>
        </div>
        <button v-else type="button" :class="GHOST" @click="addDouble">+ Add an action</button>
      </TriggerRow>

      <template v-if="draft.double">
        <TriggerRow label="">
          <p class="text-[13px] text-ink-3">
            A single click now waits
            <PopoverRoot v-model:open="waitOpen">
              <PopoverTrigger class="border-b border-dotted border-ink-3 text-ink hover:border-ink">{{ speed }} ms</PopoverTrigger>
              <UiPopoverContent :open="waitOpen" class="flex w-72 flex-col gap-2 p-4">
                <UiSlider
                  v-model="speed"
                  label="Click wait"
                  :min="150"
                  :max="600"
                  :step="25"
                  :default-value="300"
                  :format="(ms) => `${ms} ms`"
                  @commit="(ms) => attempt('setMultiClickMs', { ms }, 'Couldn\'t change how long clicks wait')" />
                <p class="text-xs text-ink-3">Applies to every button. Shorter feels snappier; longer is easier to hit.</p>
              </UiPopoverContent>
            </PopoverRoot>
            for a second one.
          </p>
        </TriggerRow>
        <TriggerRow label="Triple click" :dots="3" :muted="!draft.triple" :fired="fired === 3">
          <div v-if="draft.triple" class="flex items-start gap-1">
            <ActionEditor
              v-model="draft.triple"
              class="min-w-0 flex-1"
              :label="`${button.name} triple click`"
              :allow-hold="false"
              :allow-repeat="false" />
            <UiButton variant="ghost" size="sm" aria-label="Remove the triple click" class="mt-1" @click="draft.triple = null">
              <PhX class="size-4" />
            </UiButton>
          </div>
          <button v-else type="button" :class="GHOST" @click="draft.triple = { type: 'press', keys: [] }">+ Add an action</button>
        </TriggerRow>
      </template>
    </section>

    <section class="flex flex-col gap-1.5" :aria-label="`${button.name} moves`">
      <div class="flex items-center justify-between">
        <h3 class="text-xs font-medium text-ink-3">Move while holding</h3>
        <button type="button" class="text-xs text-ink-3 hover:text-ink" @click="emit('openPresets', null)">Gesture presets ›</button>
      </div>
      <TriggerRow label="Preset">
        <UiSelect v-model="presetChoice" :options="presetOptions" :label="`${button.name} gesture preset`" />
      </TriggerRow>
      <TriggerRow v-if="!preset" label="">
        <p class="text-[13px] text-ink-3">
          Hold the button and move the mouse to run an action. Presets are made on their own page, so any button can
          share them.
        </p>
      </TriggerRow>
      <TriggerRow v-else label="">
        <div class="flex flex-col gap-2.5 rounded-xl bg-fill/60 p-3 ring-1 ring-line ring-inset">
          <div class="flex items-start gap-3">
            <PresetCompass :preset="preset" :highlight="firedPath" class="shrink-0" />
            <ul class="flex min-w-0 flex-col gap-0.5 text-[13px]">
              <li v-for="line in presetLines" :key="line.key" class="flex min-w-0 gap-2">
                <span class="w-12 shrink-0 text-ink-3" aria-hidden="true">{{ line.glyphs }}</span>
                <span class="sr-only">{{ line.glyphs }}:</span>
                <span class="truncate">{{ line.action }}</span>
              </li>
              <li v-if="moreLines" class="text-ink-3">and {{ moreLines }} more</li>
            </ul>
          </div>
          <div class="flex items-center gap-2 border-t border-line pt-2.5">
            <p class="min-w-0 flex-1 text-xs text-ink-3">{{ presetUsed }}</p>
            <UiButton size="sm" @click="emit('openPresets', preset.id)">Edit preset ›</UiButton>
          </div>
        </div>
      </TriggerRow>
    </section>

    <p v-if="unfinished" class="text-sm text-warn">Choose an action for “{{ unfinished }}” to save this button.</p>
  </div>
</template>

<script setup lang="ts">
import { PhX } from "@phosphor-icons/vue";
import { PopoverRoot, PopoverTrigger } from "reka-ui";
import type { ButtonAction, ButtonBinding, ButtonState, Direction } from "~/composables/useAgent";

const { button, binding, apply } = defineProps<{
  button: ButtonState;
  binding: ButtonBinding | undefined;
  /** Sends the binding, null for the default; resolves false if the agent refused it. */
  apply: (binding: ButtonBinding | null) => Promise<boolean>;
}>();
const emit = defineEmits<{
  /** Opens the presets page, on this preset if one is given. */
  openPresets: [id: string | null];
}>();

const GHOST =
  "flex h-10 w-full items-center rounded-lg border border-dashed border-fill-strong px-3 text-sm text-ink-3 hover:border-ink-3 hover:text-ink";

/** What the button does without MX Controls, as an action, for the buttons whose own action is known. */
const OWN_ACTIONS: Record<string, ButtonAction> = {
  back: { type: "click", mouse: "back" },
  forward: { type: "click", mouse: "forward" },
  middle: { type: "click", mouse: "middle" },
};
const ownAction = computed(() => OWN_ACTIONS[button.id] ?? NOTHING);
const defaultLabel = computed(() => MOUSE_BUTTONS[button.id] ?? "What it does on its own");

/** Null clicks are unset; a null click is the button's default. */
interface Draft {
  click: ButtonAction | null;
  double: ButtonAction | null;
  triple: ButtonAction | null;
  preset: string | null;
}
const draft = ref<Draft>({ click: null, double: null, triple: null, preset: null });
/** Said on the click's row when a double click or a preset turned its key into a macro, or its repeat off. */
const downgraded = ref("");

const { state, attempt, lastGesture, lastClick } = useAgent();
const presets = useGesturePresets();
const preset = computed(() => presets.find(draft.value.preset));

/** Clicks have to wait or run on release, so a click can't keep a key down or repeat. */
const waits = computed(() => !!draft.value.double || !!preset.value);

function reset() {
  const b = binding;
  const legacy = b?.gesture;
  draft.value = {
    click: legacy ? legacy.click : (b?.action ?? null),
    double: (legacy ? legacy.doubleClick : b?.doubleClick) ?? null,
    triple: (legacy ? legacy.tripleClick : b?.tripleClick) ?? null,
    preset: b?.mode === "gesture" ? (b.preset ?? null) : null,
  };
  downgraded.value = "";
}

// Every state the agent pushes, a battery update included, brings new binding objects, so only a
// different button or a binding that really changed throws away an unsaved draft.
watch(() => `${button.id}|${canonical(binding ?? null)}`, reset, { immediate: true });

function setClick(action: ButtonAction | null) {
  draft.value.click = action;
  downgraded.value = "";
}

/** A click can't stay the default once something else is set, since the button's own action is replaced; it's shown as what it does. */
const ensureClick = () => (draft.value.click ??= ownAction.value);

function addDouble() {
  ensureClick();
  draft.value.double = { type: "press", keys: [] };
}

function removeDouble() {
  draft.value.double = null;
  draft.value.triple = null;
}

// The consequence is said on the row it lands on, and Ctrl+Z puts the click back once it has applied.
watch(waits, (now) => {
  const click = draft.value.click;
  if (!now || !click) return void (downgraded.value = "");
  const why = draft.value.double ? "a click now waits for a second one" : "the click now runs when you let go";
  if (click.type === "hold") {
    draft.value.click = { ...click, type: "press", repeat: false };
    downgraded.value = `Since ${why}, the key became a macro that taps ${chordLabel(click.keys)} once.`;
  } else if (click.repeat) {
    draft.value.click = { ...click, repeat: false };
    downgraded.value = `Since ${why}, the click no longer repeats.`;
  }
});

const presetOptions = computed(() => [
  { value: "none", label: "None" },
  ...presets.presets.value.map((p) => ({ value: p.id, label: p.name })),
  { value: "new", label: "New preset…" },
]);
const presetChoice = computed({
  get: () => draft.value.preset ?? "none",
  set: (value) => {
    if (value === "new") return void newPreset();
    if (value !== "none") ensureClick();
    draft.value.preset = value === "none" ? null : value;
  },
});

/** A blank preset this button follows straight away, opened to fill in. */
async function newPreset() {
  const id = await presets.create(presets.blank());
  if (!id) return;
  ensureClick();
  draft.value.preset = id;
  emit("openPresets", id);
}

const MAX_LINES = 6;
const presetRows = computed(() =>
  preset.value ? pathRows(preset.value).filter((row) => row.step.type !== "none") : [],
);
const presetLines = computed(() =>
  presetRows.value.slice(0, MAX_LINES).map((row) => ({
    key: pathKey(row.path),
    glyphs: pathText(row.path),
    action: describeAction(actionOf(row.step)),
  })),
);
const moreLines = computed(() => Math.max(0, presetRows.value.length - MAX_LINES));
const presetUsed = computed(() => {
  if (!preset.value) return "";
  const others = presets.usedBy(preset.value.id, { profile: state.value?.activeProfile ?? "", button: button.id });
  if (!others.length) return "Only this button uses it.";
  return `Shared with ${others.join(", ")}. Editing it changes ${others.length === 1 ? "both" : "all of them"}.`;
});

const next = computed<ButtonBinding | null>(() => {
  const { click, double, triple, preset: presetId } = draft.value;
  if (!click && !double && !presetId) return null;
  const clicks = double ? { doubleClick: double, ...(triple && { tripleClick: triple }) } : {};
  const action = click ?? ownAction.value;
  return presetId
    ? { button: button.id, mode: "gesture", action, ...clicks, preset: presetId }
    : { button: button.id, mode: "action", action, ...clicks };
});

/** The first click still waiting for its keys or its choice. */
const unfinished = computed(() => {
  const { click, double, triple } = draft.value;
  const parts: [string, ButtonAction | null][] = [
    ["Click", click],
    ["Double click", double],
    ["Triple click", double ? triple : null],
  ];
  return parts.find(([, action]) => action && !complete(action))?.[0];
});

// Changes apply as they're made, with Ctrl+Z to put them back, once the agent would take them.
// A refused change goes back to what the button does, rather than staying on screen as if it applied.
watch(
  () => canonical(next.value),
  async (now) => {
    if (!unfinished.value && now !== canonical(binding ?? null) && !(await apply(next.value))) reset();
  },
);

const { speed, waitOpen } = useClickWait();

// Using the button lights up what it just ran.
const fired = ref(0);
const firedPath = ref<Direction[] | null>(null);
let unlight: ReturnType<typeof setTimeout> | undefined;
function light(count: number, path: Direction[] | null) {
  fired.value = count;
  firedPath.value = path;
  clearTimeout(unlight);
  unlight = setTimeout(() => {
    fired.value = 0;
    firedPath.value = null;
  }, 700);
}
watch(lastClick, (click) => click?.button === button.id && light(click.count, null));
watch(lastGesture, (gesture) => gesture?.button === button.id && gesture.path.length > 0 && light(0, gesture.path));
onBeforeUnmount(() => clearTimeout(unlight));

function useClickWait() {
  const speed = ref(state.value?.multiClickMs ?? 300);
  watch(
    () => state.value?.multiClickMs,
    (ms) => ms && (speed.value = ms),
  );
  return { speed, waitOpen: ref(false) };
}
</script>
