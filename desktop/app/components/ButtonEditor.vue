<template>
  <div class="flex flex-col gap-5">
    <section class="flex flex-col gap-3" :aria-label="`${button.name} clicks`">
      <h3 class="text-xs font-medium text-ink-3">
        {{ draft.rose ? "Quick press" : preset ? "Press without moving" : "Press" }}
      </h3>
      <div class="flex h-8 items-center gap-1" role="group" aria-label="Click count">
        <button
          v-for="count in [1, 2, 3]"
          :key="count"
          type="button"
          :disabled="(count === 3 && !draft.double) || (count > 1 && !!draft.rose)"
          :aria-pressed="clickTab === count"
          class="h-8 flex-1 rounded-lg px-2 text-xs font-medium disabled:opacity-30"
          :class="[
            clickTab === count ? 'bg-fill ring-1 ring-line ring-inset' : 'text-ink-3 hover:bg-fill/60',
            fired === count && 'ring-2 ring-accent',
          ]"
          @click="chooseClickTab(count)"
        >
          {{ CLICK_NAMES[count]
          }}{{ (count === 2 && !draft.double) || (count === 3 && !draft.triple) ? " +" : "" }}
        </button>
      </div>
      <div class="flex h-6 items-center justify-between text-xs text-ink-3">
        <span>{{
          clickTab === 1 && draft.rose
            ? "Runs on a short press that doesn't open the rose"
            : clickTab === 1 ? (preset || waits ? "Runs on release" : "Runs on press") : "Runs on release"
        }}</span>
        <button
          type="button"
          :disabled="clickTab === 1"
          class="rounded px-1.5 py-1 hover:bg-fill disabled:invisible"
          @click="removeSelectedClick"
        >
          Remove action
        </button>
      </div>
      <ActionEditor
        :key="`${button.id}:${clickTab}`"
        :model-value="selectedClick"
        :label="`${button.name} ${CLICK_NAMES[clickTab]}`"
        :default-label="clickTab === 1 ? defaultLabel : undefined"
        :allow-hold="clickTab === 1 && !waits"
        :allow-repeat="clickTab === 1 && !waits"
        :layers="clickTab === 1 && !waits ? 'hold' : 'toggle'"
        profiles
        @update:model-value="setSelectedClick"
        @default="setClick(null)"
      />
      <div class="h-10 text-[13px] leading-relaxed text-ink-3">
        <p v-if="draft.double">
          A single click waits
          <PopoverRoot v-model:open="waitOpen"
            ><PopoverTrigger class="border-b border-dotted border-ink-3 text-ink"
              >{{ speed }} ms</PopoverTrigger
            ><UiPopoverContent :open="waitOpen" class="flex w-72 flex-col gap-2 p-4"
              ><UiSlider
                v-model="speed"
                label="Click wait"
                :min="150"
                :max="600"
                :step="25"
                :default-value="300"
                :format="(ms) => `${ms} ms`"
                @commit="(ms) => attempt('setMultiClickMs', { ms }, 'Couldn\'t change how long clicks wait')"
              />
              <p class="text-xs text-ink-3">
                Applies to every button. Shorter feels snappier; longer is easier to hit.
              </p></UiPopoverContent
            ></PopoverRoot
          >
          for another click.
        </p>
        <p v-else-if="draft.rose">A rose button runs one click; double and triple clicks aren't available.</p>
        <p v-else>
          Use the double-click tab to add another action. Triple clicks become available after that.
        </p>
      </div>
    </section>

    <section class="flex flex-col gap-3" :aria-label="`${button.name} while held`">
      <div class="flex items-center justify-between">
        <h3 class="text-xs font-medium text-ink-3">While held</h3>
        <button
          v-if="!draft.rose"
          type="button"
          class="text-xs text-ink-3 hover:text-ink"
          @click="emit('openPresets', null)">
          Browse library ›
        </button>
      </div>
      <UiSegmented v-model="heldChoice" :options="HELD_OPTIONS" :label="`What ${button.name} does while held`" />
      <RoseEditor
        v-if="draft.rose"
        v-model="draft.rose"
        :below="inLayer ? (inherited?.mode === 'rose' ? (inherited.rose ?? null) : null) : undefined"
        :button-id="button.id"
        :button-name="button.name" />
      <UiSelect
        v-if="!draft.rose"
        v-model="presetChoice"
        :options="presetOptions"
        :label="`${button.name} gesture preset`" />
      <UiScrollArea
        v-if="!draft.rose"
        class="h-48 rounded-xl bg-fill/60 ring-1 ring-line ring-inset"
        content-class="p-3">
        <p v-if="!preset" class="text-[13px] leading-relaxed text-ink-3">
          Hold this button, then move up, down, left or right. Choose a shared preset to assign those
          movements.
        </p>
        <template v-else>
          <div class="flex items-start gap-3">
            <PresetCompass :preset="preset" :highlight="firedPath" class="shrink-0" />
            <ul class="flex min-w-0 flex-col gap-0.5 text-[13px]">
              <li v-for="line in presetLines" :key="line.key" class="flex min-w-0 gap-2">
                <span class="w-12 shrink-0 text-ink-3">{{ line.glyphs }}</span
                ><span class="truncate">{{ line.action }}</span>
              </li>
              <li v-if="moreLines" class="text-ink-3">and {{ moreLines }} more</li>
            </ul>
          </div>
          <div class="mt-3 flex items-center gap-2 border-t border-line pt-2.5">
            <p class="min-w-0 flex-1 text-xs text-ink-3">{{ presetUsed }}</p>
            <UiButton size="sm" @click="emit('openPresets', preset.id)">Edit preset ›</UiButton>
          </div>
        </template>
      </UiScrollArea>
    </section>
    <UiScrollArea
      class="h-12 text-xs leading-relaxed"
      :class="unfinished ? 'text-warn' : 'text-ink-3'"
      role="status"
    >
      {{ unfinished ? `Choose an action for “${unfinished}” to save this button.` : downgraded }}
    </UiScrollArea>
  </div>
</template>
<script setup lang="ts">
import { PopoverRoot, PopoverTrigger } from "reka-ui";
import type { ButtonAction, ButtonBinding, ButtonState, Direction, RoseMenu } from "~/composables/useAgent";

const { button, binding, inherited, apply } = defineProps<{
  button: ButtonState;
  binding: ButtonBinding | undefined;
  /**
   * In the second layer, the main layer's binding, which runs while this layer leaves the button unset; null for a
   * main binding that's the button's own default. Undefined in the main layer.
   */
  inherited?: ButtonBinding | null;
  /** Sends the binding, null for the default; resolves false if the agent refused it. */
  apply: (binding: ButtonBinding | null) => Promise<boolean>;
}>();
const emit = defineEmits<{
  /** Opens the presets page, on this preset if one is given. */
  openPresets: [id: string | null];
}>();

const GHOST =
  "flex h-10 w-full items-center rounded-lg border border-dashed border-fill-strong px-3 text-sm text-ink-3 hover:border-ink-3 hover:text-ink";

/** What the button does without MX Studio, as an action, for the buttons whose own action is known. */
const OWN_ACTIONS: Record<string, ButtonAction> = {
  back: { type: "click", mouse: "back" },
  forward: { type: "click", mouse: "forward" },
  middle: { type: "click", mouse: "middle" },
};
const inLayer = computed(() => inherited !== undefined);
/** What the click is before it's set: the main layer's click in the second layer, else what the button does on its own. */
const ownAction = computed(() =>
  inLayer.value && inherited ? (inherited.action ?? inherited.gesture?.click ?? NOTHING) : (OWN_ACTIONS[button.id] ?? NOTHING),
);
const defaultLabel = computed(() =>
  inLayer.value ? "Same as main layer" : (MOUSE_BUTTONS[button.id] ?? "What it does on its own"),
);

/** Null clicks are unset; a null click is the button's default. */
interface Draft {
  click: ButtonAction | null;
  double: ButtonAction | null;
  triple: ButtonAction | null;
  preset: string | null;
  /** The rose shown while held, instead of a gesture preset. */
  rose: RoseMenu | null;
}
const draft = ref<Draft>({ click: null, double: null, triple: null, preset: null, rose: null });
const CLICK_NAMES: Record<number, string> = { 1: "Click", 2: "Double click", 3: "Triple click" };
const clickTab = ref(1);
const selectedClick = computed(() =>
  clickTab.value === 1 ? draft.value.click : clickTab.value === 2 ? draft.value.double : draft.value.triple,
);
function chooseClickTab(count: number) {
  if (count === 3 && !draft.value.double) return;
  if (count === 2 && !draft.value.double) addDouble();
  if (count === 3 && !draft.value.triple) draft.value.triple = { type: "press", keys: [] };
  clickTab.value = count;
}
function setSelectedClick(action: ButtonAction) {
  if (clickTab.value === 1) setClick(action);
  else if (clickTab.value === 2) draft.value.double = action;
  else draft.value.triple = action;
}
function removeSelectedClick() {
  if (clickTab.value === 2) removeDouble();
  else if (clickTab.value === 3) draft.value.triple = null;
  clickTab.value = 1;
}
/** Explains when a double click or preset changed held keys into a shortcut, or stopped repetition. */
const downgraded = ref("");

const { state, attempt, lastGesture, lastClick } = useAgent();
const presets = useGesturePresets();
const view = useProfileView();
const preset = computed(() => presets.find(draft.value.preset));

/** Clicks have to wait or run on release, so a click can't keep a key down or repeat. */
const waits = computed(() => !!draft.value.double || !!preset.value || !!draft.value.rose);

function reset() {
  const b = binding;
  const legacy = b?.gesture;
  draft.value = {
    click: legacy ? legacy.click : (b?.action ?? null),
    double: (legacy ? legacy.doubleClick : b?.doubleClick) ?? null,
    triple: (legacy ? legacy.tripleClick : b?.tripleClick) ?? null,
    preset: b?.mode === "gesture" ? (b.preset ?? null) : null,
    rose: b?.mode === "rose" && b.rose ? (JSON.parse(JSON.stringify(b.rose)) as RoseMenu) : null,
  };
  if ((clickTab.value === 2 && !draft.value.double) || (clickTab.value === 3 && !draft.value.triple))
    clickTab.value = 1;
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
  const why = draft.value.double
    ? "a click now waits for a second one"
    : draft.value.rose
      ? "the quick press now runs when you let go"
      : "the click now runs when you let go";
  if (click.type === "hold") {
    draft.value.click = { ...click, type: "press", repeat: false };
    downgraded.value = `Since ${why}, the held keys became a keyboard shortcut that taps ${chordLabel(click.keys)} once.`;
  } else if (click.type === "layer" && click.layer === "momentary") {
    draft.value.click = { type: "layer", layer: "toggle" };
    downgraded.value = `Since ${why}, holding the second layer became a toggle.`;
  } else if (click.repeat) {
    draft.value.click = { ...click, repeat: false };
    downgraded.value = `Since ${why}, the click no longer repeats.`;
  }
});

const HELD_OPTIONS = [
  { value: "gestures", label: "Gestures" },
  { value: "rose", label: "Rose menu" },
];
const heldChoice = computed({
  get: () => (draft.value.rose ? "rose" : "gestures"),
  set: (value) => {
    if (value === "rose" && !draft.value.rose) startRose();
    if (value === "gestures") draft.value.rose = null;
  },
});

/**
 * Turns the button into a rose: four empty options, or in the second layer as many as the main layer's rose,
 * each passing its option through. A rose runs a single quick press, so extra clicks go.
 */
function startRose() {
  const main = inLayer.value && inherited?.mode === "rose" ? inherited.rose : null;
  draft.value.rose = main
    ? { items: main.items.map(() => ({ action: NOTHING, passThrough: true })) }
    : { items: Array.from({ length: 4 }, () => ({ action: NOTHING })) };
  draft.value.preset = null;
  if (draft.value.double) {
    removeDouble();
    clickTab.value = 1;
    downgraded.value = "A rose runs one quick press, so the double and triple clicks were removed.";
  }
}

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
  const others = presets.usedBy(preset.value.id, {
    profile: view.profile.value?.id ?? "",
    button: button.id,
    layer: view.second.value,
  });
  if (!others.length) return "Only this button uses it.";
  return `Shared with ${others.join(", ")}. Editing it changes ${others.length === 1 ? "both" : "all of them"}.`;
});

const next = computed<ButtonBinding | null>(() => {
  const { click, double, triple, preset: presetId, rose } = draft.value;
  if (rose) return { button: button.id, mode: "rose", action: click ?? ownAction.value, rose };
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
  const options = (draft.value.rose?.items ?? []).map((item, i): [string, ButtonAction | null] => [
    `Option ${i + 1}`,
    item.passThrough ? null : item.action,
  ]);
  return [...parts, ...options].find(([, action]) => action && !complete(action))?.[0];
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
watch(
  lastGesture,
  (gesture) => gesture?.button === button.id && gesture.path.length > 0 && light(0, gesture.path),
);
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
