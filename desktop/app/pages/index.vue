<template>
  <GesturePresets v-if="page === 'gestures'" />
  <MacrosPage v-else-if="page === 'macros'" />
  <FitStage v-else-if="device" :width="STAGE.width" :height="STAGE.height">
    <!-- The layer being edited. The photo starts lower than this, so it sits in the space above it. -->
    <div class="absolute top-0 left-0 z-10 flex w-76 flex-col gap-2">
      <UiSegmented
        :model-value="layer"
        :options="LAYER_OPTIONS"
        label="Layer to edit"
        @update:model-value="(value) => (layer = value as LayerId)" />
      <p class="flex min-h-8 items-start gap-1.5 px-1 text-xs leading-relaxed text-ink-3">
        <span
          v-if="layerOn"
          class="mt-1.5 size-1.5 shrink-0 rounded-full bg-good"
          aria-hidden="true" />
        <span>{{ layerHint }}</span>
      </p>
    </div>

    <ControlMap
      :items="items"
      :selected="selectedId"
      :active="hoveredId ?? selectedId"
      :frame="frame"
      :pulse="pulse"
      :menu="(id) => menuFor(items.find((item) => item.control.id === id)!)"
      @select="open"
      @hover="(id) => (hoveredId = id)" />

    <!-- Opening a control pushes the list off to the left and brings its settings in from the right, while the
         photo steps back to make room; going back reverses it. Both overlap while they cross, so neither waits. -->
    <AnimatePresence :initial="false">
      <motion.div
        v-if="!selected"
        key="list"
        class="absolute top-0 rounded-2xl bg-panel"
        :style="{ left: `${LIST.x}px`, width: `${LIST.width}px`, height: `${layout.height}px` }"
        :initial="{ opacity: 0, x: -8 }"
        :animate="{ opacity: 1, x: 0 }"
        :exit="{ opacity: 0, x: -8, transition: EXIT }"
        :transition="BASE">
        <template v-for="group in groups" :key="group.id">
          <h3
            :id="`group-${group.id}`"
            class="absolute inset-x-0 flex items-end px-4.5 pb-2 text-xs font-medium text-ink-3"
            :style="{ top: `${group.top}px`, height: `${LIST.header}px` }">
            {{ group.label }}
          </h3>
          <ul :aria-labelledby="`group-${group.id}`">
            <li
              v-for="(item, i) in group.items"
              :key="item.control.id"
              class="absolute"
              :style="{
                top: `${item.top}px`,
                height: `${LIST.row}px`,
                left: `${LIST.padding}px`,
                right: `${LIST.padding}px`,
              }">
              <motion.span
                v-if="pulse?.id === item.control.id"
                :key="pulse.n"
                aria-hidden="true"
                class="pointer-events-none absolute inset-0 rounded-xl bg-accent/15"
                :initial="{ opacity: 1 }"
                :animate="{ opacity: 0 }"
                :transition="PULSE" />
              <span
                v-if="!item.first"
                class="absolute inset-x-3 top-0 h-px bg-line"
                :class="
                  (hoveredId === item.control.id || hoveredId === group.items[i - 1]?.control.id) &&
                  'opacity-0'
                " />
              <UiContextMenu :items="menuFor(item)">
                <!-- Pointer speed is one slider, so it's set right in its row rather than in a panel of its own. -->
                <div
                  v-if="item.control.id === 'sensor'"
                  id="sensor-row"
                  class="flex size-full items-center pr-2.5 pl-3"
                  @pointerenter="hoveredId = item.control.id"
                  @pointerleave="hoveredId = null">
                  <SensorSettings class="w-full" :device="device" />
                </div>
                <button
                  v-else
                  type="button"
                  class="group relative flex size-full cursor-pointer items-center gap-2 rounded-xl pr-2.5 pl-3 text-left active:bg-fill-strong/60"
                  :class="hoveredId === item.control.id && 'bg-fill'"
                  @click="selectedId = item.control.id"
                  @pointerenter="hoveredId = item.control.id"
                  @pointerleave="hoveredId = null">
                  <span class="shrink-0 text-sm font-medium">{{ item.label }}</span>
                  <span v-if="item.customized" class="size-1.5 shrink-0 rounded-full bg-accent" />
                  <span v-if="item.customized" class="sr-only">, changed</span>
                  <span
                    class="ml-auto flex min-w-0 items-center gap-1.5 text-[13px]"
                    :class="item.customized ? 'text-ink-2' : 'text-ink-3'">
                    <PhWarning v-if="item.shared" class="size-3.5 shrink-0 text-warn" />
                    <span v-if="item.shared" class="sr-only">Also used by another app:</span>
                    <span class="truncate">{{ item.summary }}</span>
                  </span>
                  <PhCaretRight
                    class="size-4 shrink-0 text-ink-3 transition-transform"
                    :class="hoveredId === item.control.id && 'translate-x-0.5 text-ink'" />
                </button>
              </UiContextMenu>
            </li>
          </ul>
        </template>
      </motion.div>

      <motion.section
        v-else-if="selected"
        :key="selected.control.id"
        class="absolute top-0 bottom-0 flex flex-col rounded-2xl bg-panel"
        :style="{ left: `${INSPECTOR.x}px`, width: `${INSPECTOR.width}px` }"
        :aria-label="selected.label"
        :initial="{ opacity: 0, x: 8 }"
        :animate="{ opacity: 1, x: 0 }"
        :exit="{ opacity: 0, x: 8, transition: EXIT }"
        :transition="BASE">
        <!-- Every control's settings share this frame: what it is, a warning if it's shared, its settings,
             and a way back to the default. -->
        <UiContextMenu :items="menuFor(selected)">
          <header class="px-6 pt-4 pb-5">
            <button
              type="button"
              class="-ml-1 mb-2 inline-flex items-center gap-1.5 rounded-full px-1 text-sm text-ink-3 hover:text-ink"
              @click="selectedId = null">
              <PhArrowLeft class="size-4" />
              All controls
            </button>
            <h2 class="text-lg font-semibold">{{ selected.label }}</h2>
            <p class="mt-0.5 text-sm text-ink-3">{{ DESCRIPTIONS[selected.control.id] }}</p>
            <p
              v-if="selected.shared"
              class="mt-3 flex gap-2 rounded-lg bg-warn-fill px-3 py-2 text-[13px] text-warn">
              <PhWarning class="mt-px size-4 shrink-0" />
              {{ state?.optionsPlusRunning ? "Logi Options+" : "Another app" }} also responds to
              this control. Set it here so a press does one thing.
            </p>
          </header>
        </UiContextMenu>

        <UiScrollArea class="min-h-0 flex-1" content-class="flex flex-col gap-6 px-6 pb-6">
          <WheelSettings v-if="selected.control.id === 'wheel'" :device="device" />
          <ThumbWheelSettings v-else-if="selected.control.id === 'thumbwheel'" :device="device" />
          <template v-else-if="selected.button">
            <ButtonEditor
              :button="selected.button"
              :binding="bindingFor(selected.button.id)"
              :inherited="second ? (mainBindingFor(selected.button.id) ?? null) : undefined"
              :apply="apply"
              @open-presets="(id) => goTo('gestures', id)" />
            <HapticSettings v-if="selected.control.id === 'haptic'" :device="device" />
          </template>
        </UiScrollArea>

        <footer
          class="flex h-14 shrink-0 items-center justify-between border-t border-line pr-3 pl-6">
          <span class="flex items-center gap-2 text-[13px] text-ink-3">
            <span
              class="size-1.5 rounded-full"
              :class="selected.customized ? 'bg-accent' : 'bg-fill-strong'" />
            {{ selected.customized ? "Customized" : second && !selected.global ? "Same as main layer" : "Default" }}
          </span>
          <UiButton
            variant="ghost"
            size="sm"
            :disabled="!selected.customized"
            @click="reset(selected)">
            {{ second && !selected.global ? "Use main layer" : "Reset to default" }}
          </UiButton>
        </footer>
      </motion.section>
    </AnimatePresence>

    <p class="absolute bottom-0 left-0 h-5 text-sm text-ink-3" aria-live="polite">
      <span v-if="!flashed" class="text-xs">Pointer speed, wheel feel and haptics apply to every profile.</span>
      <AnimatePresence>
        <motion.span
          v-if="flashed"
          :key="flashed.id"
          class="absolute left-0 whitespace-nowrap"
          :initial="{ opacity: 0, y: 4 }"
          :animate="{ opacity: 1, y: 0 }"
          :exit="{ opacity: 0, transition: EXIT }"
          :transition="FAST">
          <span class="font-medium text-ink">{{ flashed.name }}</span> · {{ flashed.label }}
        </motion.span>
      </AnimatePresence>
    </p>
  </FitStage>
</template>

<script setup lang="ts">
import {
  PhArrowCounterClockwise,
  PhArrowLeft,
  PhCaretRight,
  PhSlidersHorizontal,
  PhWarning,
} from "@phosphor-icons/vue";
import { animate, AnimatePresence, motion, useReducedMotion } from "motion-v";
import type { ButtonBinding, ButtonState } from "~/composables/useAgent";
import type { LayerId } from "~/composables/useProfileView";
import type { ControlId } from "~/utils/controlMap";
import type { MenuEntry } from "~/utils/menu";

const { state, device, lastGesture, lastClick, lastPress, attempt } = useAgent();

const view = useProfileView();
const { layer, second, bindings, mainBindings } = view;
const bindingFor = (button: string) => bindings.value.find((b) => b.button === button);
const mainBindingFor = (button: string) => mainBindings.value.find((b) => b.button === button);

const LAYER_OPTIONS = [
  { value: "main", label: "Main layer" },
  { value: "second", label: "Second layer" },
];
/** Whether the second layer is on right now, in the profile shown. */
const layerOn = computed(() => !!state.value?.layerActive && state.value.activeProfile === view.profile.value?.id);
const reachesLayer = computed(() =>
  [...mainBindings.value, ...(view.profile.value?.layer.bindings ?? [])].some((b) =>
    [b.action, b.doubleClick, b.tripleClick].some((a) => a?.type === "layer"),
  ),
);
const layerHint = computed(() => {
  if (layerOn.value) return "The second layer is on now.";
  if (!reachesLayer.value) return "Set a button to Hold or Toggle second layer to reach it.";
  return second.value
    ? "Runs while a button holds or toggles it. Controls left unset do what they do in the main layer."
    : "A button holds or toggles the second layer.";
});

// The page follows the chosen profile when a button or another window changes it.
watch(
  () => state.value?.selectedProfile,
  (now, before) => now && before && now !== before && view.view(now),
);

const selectedId = ref<ControlId | null>(null);
const hoveredId = ref<ControlId | null>(null);
const { page, goTo } = usePage();

const LABELS: Partial<Record<ControlId, string>> = BUTTON_LABELS;
const presets = useGesturePresets();

// The controls this mouse has; a button it doesn't have has no row.
const present = computed(() => {
  const mouse = device.value;
  if (!mouse) return [];
  return CONTROLS.flatMap((control) => {
    const button = mouse.buttons.find((b) => b.id === control.id);
    if (control.id === "wheel")
      return [
        {
          control,
          button,
          label: "Scroll wheel",
          summary: wheelSummary(),
          customized: wheelCustomized(),
          shared: false,
          global: true,
        },
      ];
    if (control.id === "thumbwheel") {
      if (!mouse.thumbWheel) return [];
      const own = view.thumbWheel.value;
      const customized = second.value ? !!own : !isDefaultThumbWheel(own ?? undefined);
      return [
        {
          control,
          button,
          label: "Thumb wheel",
          summary: second.value && !own ? "Same as main layer" : thumbWheelSummary(),
          customized,
          shared: mouse.thumbWheel.divertedElsewhere && !customized && !second.value,
          global: false,
        },
      ];
    }
    if (control.id === "sensor") {
      const { current, default: standard } = mouse.dpi;
      return [
        {
          control,
          button,
          label: "Pointer speed",
          summary: `${current} DPI`,
          customized: current !== standard,
          shared: false,
          global: true,
        },
      ];
    }
    if (!button) return [];
    const label = LABELS[control.id] ?? `${button.name} button`;
    const customized = !!bindingFor(button.id);
    return [
      {
        control,
        button,
        label,
        summary: describe(button),
        customized,
        shared: button.divertedElsewhere && !customized && !(second.value && mainBindingFor(button.id)),
        global: false,
      },
    ];
  });
});

const layout = computed(() => layoutList(present.value.map((item) => item.control)));
const items = computed(() =>
  present.value.map((item) => ({ ...item, ...layout.value.rows.get(item.control.id)! })),
);
const groups = computed(() =>
  layout.value.headers.map((header) => ({
    ...header,
    items: items.value.filter((item) => item.control.group === header.id),
  })),
);

const selected = computed(
  () => items.value.find((item) => item.control.id === selectedId.value) ?? null,
);

// How far the stage has moved from the overview to a control's settings, from 0 to 1. The photo, its dots
// and the line are all placed from the frame this gives, so they move together on the same spring.
const focus = ref(0);
const frame = computed(() => mixFrames(OVERVIEW, FOCUS, focus.value));
const reduceMotion = useReducedMotion();
let focusing: ReturnType<typeof animate> | undefined;
watch(
  () => !!selected.value,
  (open) => {
    focusing?.stop();
    focusing = animate(focus.value, open ? 1 : 0, {
      ...(reduceMotion.value ? { duration: 0 } : BASE),
      onUpdate: (value) => (focus.value = value),
    });
  },
);
onBeforeUnmount(() => focusing?.stop());

// Each says what the control is; what it does by default is said by its settings.
const DESCRIPTIONS: Partial<Record<ControlId, string>> = {
  wheel: "How the main wheel feels as you scroll.",
  thumbwheel: "The wheel under your thumb. It scrolls sideways, or runs an action each notch.",
  middle: "Pressing the scroll wheel straight down.",
  smartshift:
    "The button behind the wheel. By default it switches the wheel between clicking and free spin.",
  back: "The rear thumb button.",
  forward: "The front thumb button.",
  gesture: "The button in the thumb rest.",
  haptic: "The panel under your thumb. It clicks with a haptic tap rather than a switch.",
};

/** The sensor has no panel; its dot goes to the slider in its row instead. */
function open(id: ControlId) {
  if (id !== "sensor") return void (selectedId.value = id);
  selectedId.value = null;
  // With a control open, the list only comes back on the next render.
  void nextTick(() => document.querySelector<HTMLElement>("#sensor-row [role=slider]")?.focus());
}

function wheelSummary() {
  const shift = device.value!.smartShift;
  return shift.ratchet ? `SmartShift at ${shift.autoDisengage}` : "Free spin";
}

function wheelCustomized() {
  const shift = device.value!.smartShift;
  return (
    !shift.ratchet ||
    shift.autoDisengage !== shift.defaultAutoDisengage ||
    (shift.tunableTorque && shift.torque !== shift.defaultTorque)
  );
}

function thumbWheelSummary() {
  const wheel = view.thumbWheel.value ?? view.mainThumbWheel.value!;
  const inverted = wheel.inverted ? ", inverted" : "";
  if (wheel.mode === "actions")
    return `${describeAction(wheel.left ?? NOTHING)} / ${describeAction(wheel.right ?? NOTHING)}${inverted}`;
  return wheel.speed === 100
    ? `Scroll sideways${inverted}`
    : `Scroll sideways ${wheel.speed}%${inverted}`;
}

// "Copy · 2× Task View · Window tiling": the click, what extra clicks run where there's room, then the moves.
function describe(button: ButtonState) {
  const binding = bindingFor(button.id);
  if (binding) {
    const double = hasAction(binding.doubleClick) ? binding.doubleClick : null;
    const triple = hasAction(binding.tripleClick) ? binding.tripleClick : null;
    const gesture = binding.mode === "gesture" ? (presets.find(binding.preset)?.name ?? "Gestures") : null;
    const clicks = triple ? ["2×", "3×"] : double ? [gesture ? "2×" : `2× ${describeAction(double)}`] : [];
    const click = binding.action ?? binding.gesture?.click ?? NOTHING;
    return [describeAction(click), ...clicks, gesture].filter(Boolean).join(" · ");
  }
  if (second.value) return "Same as main layer";
  // Back, forward and middle are named after what they do on their own, so their defaults can say it.
  return MOUSE_BUTTONS[button.id] ?? "Default";
}

const isMac = navigator.userAgent.includes("Mac");

// Escape closes the panel, unless it's closing a menu or popover first; those are still in the page when it arrives.
// Ctrl+Z puts back the last change, except in a field, where it undoes typing.
function onKey(event: KeyboardEvent) {
  if (event.defaultPrevented) return;
  if (page.value === "macros") return;
  if (event.key === "Escape" && selectedId.value && page.value === "profiles") {
    if (!document.querySelector("[data-dismissable-layer]")) selectedId.value = null;
    return;
  }
  const command = isMac ? event.metaKey : event.ctrlKey;
  if (!command || event.altKey || !["KeyZ", "KeyY"].includes(event.code)) return;
  if ((event.target as HTMLElement | null)?.closest("input, textarea, [contenteditable]")) return;
  event.preventDefault();
  void (event.shiftKey || event.code === "KeyY" ? redoLast() : undoLast());
}

// Ctrl+Z puts back changes to the profile shown, on this mouse; changes to another one would be out of sight.
watch([() => view.profile.value?.id, () => device.value?.productId], forgetUndo);
onMounted(() => window.addEventListener("keydown", onKey));
onBeforeUnmount(() => window.removeEventListener("keydown", onKey));

// The mouse's own back button closes the panel, and forward opens the last one closed again.
let closed: ControlId | null = null;
watch(selectedId, (now, before) => {
  if (!now && before) closed = before;
});
const MOUSE_BACK = 3;
const MOUSE_FORWARD = 4;
function onMouseButton(event: MouseEvent) {
  if (event.button !== MOUSE_BACK && event.button !== MOUSE_FORWARD) return;
  // Otherwise the webview takes them as history and leaves the page.
  event.preventDefault();
  if (event.type !== "mouseup") return;
  if (event.button === MOUSE_BACK) selectedId.value = null;
  else if (!selectedId.value && closed) selectedId.value = closed;
}
onMounted(() => {
  window.addEventListener("mousedown", onMouseButton);
  window.addEventListener("mouseup", onMouseButton);
});
onBeforeUnmount(() => {
  window.removeEventListener("mousedown", onMouseButton);
  window.removeEventListener("mouseup", onMouseButton);
});

// Pressing a control on the mouse lights up its dot and row, for the controls the agent can hear.
const pulse = ref<{ id: ControlId; n: number } | null>(null);
watch(lastPress, (press) => {
  const control = CONTROLS.find((c) => c.id === press?.button);
  if (control) pulse.value = { id: control.id, n: (pulse.value?.n ?? 0) + 1 };
});

// Names the gesture that just fired, for a moment.
const DIRECTION_LABELS: Record<string, string> = {
  up: "↑ Up",
  down: "↓ Down",
  left: "← Left",
  right: "→ Right",
};
const CLICK_LABELS: Record<number, string> = { 1: "Click", 2: "Double click", 3: "Triple click" };
const flashed = ref<{ id: number; name: string; label: string } | null>(null);
let flashes = 0;
let clearFlash: ReturnType<typeof setTimeout> | undefined;
function flash(buttonId: string | null, label: string) {
  const button = device.value?.buttons.find((b) => b.id === buttonId);
  if (!button) return;
  flashed.value = { id: ++flashes, name: button.name, label };
  clearTimeout(clearFlash);
  clearFlash = setTimeout(() => (flashed.value = null), 1500);
}
watch(lastGesture, (gesture) => {
  if (!gesture || gesture.phase !== "fire" || !gesture.direction) return;
  flash(gesture.button, gesture.path.length > 1 ? pathText(gesture.path) : DIRECTION_LABELS[gesture.direction]!);
});
// A plain button's single click is just a press, which would flash on every use.
watch(lastClick, (click) => {
  if (!click || (click.count === 1 && bindingFor(click.button ?? "")?.mode !== "gesture")) return;
  flash(click.button, CLICK_LABELS[click.count] ?? `${click.count} clicks`);
});
onBeforeUnmount(() => clearTimeout(clearFlash));

type Item = (typeof items.value)[number];

/** Puts a control back to its default; Ctrl+Z puts back what it was. */
function reset(item: Item) {
  const mouse = device.value!;
  if (item.button) return apply(null, item);
  if (item.control.id === "wheel") {
    const shift = mouse.smartShift;
    return resetWith(
      item.label,
      "setSmartShift",
      { ratchet: true, autoDisengage: shift.defaultAutoDisengage, torque: shift.defaultTorque },
      { ratchet: shift.ratchet, autoDisengage: shift.autoDisengage, torque: shift.torque },
    );
  }
  if (item.control.id === "thumbwheel") {
    const target = view.target();
    const now = view.thumbWheel.value;
    return resetWith(
      item.label,
      "setThumbWheel",
      { thumbWheel: target.layer ? null : { mode: "scroll", speed: 100, inverted: false }, ...target },
      { thumbWheel: now && { ...now }, ...target },
    );
  }
  return resetWith(item.label, "setDpi", { dpi: mouse.dpi.default }, { dpi: mouse.dpi.current });
}

async function resetWith(
  label: string,
  method: string,
  params: Record<string, unknown>,
  before: Record<string, unknown>,
) {
  if (!(await attempt(method, params, `Couldn't reset the ${label.toLowerCase()}`))) return;
  rememberUndo(() => attempt(method, before, `Couldn't put the ${label.toLowerCase()} back`), () => attempt(method, params, `Couldn't reset the ${label.toLowerCase()}`), "Reset control");
}

function menuFor(item: Item): MenuEntry[] {
  const open = item.control.id === selectedId.value;
  return [
    ...(item.control.id === "sensor"
      ? []
      : [
          open
            ? { label: "All controls", icon: PhArrowLeft, select: () => (selectedId.value = null) }
            : {
                label: "Open settings",
                icon: PhSlidersHorizontal,
                select: () => (selectedId.value = item.control.id),
              },
          "separator" as const,
        ]),
    {
      label: "Reset to default",
      icon: PhArrowCounterClockwise,
      disabled: !item.customized,
      select: () => reset(item),
    },
  ];
}

type Target = ReturnType<typeof view.target>;

/** The bindings of one layer of one profile, as the page last heard them. */
function layerBindings({ profile, layer }: Target) {
  const p = state.value?.profiles.find((x) => x.id === profile);
  return (layer ? p?.layer.bindings : p?.bindings) ?? [];
}

/** Sets one button's binding in a layer, or unsets it for null, keeping the others. */
function setBinding(target: Target, button: string, binding: ButtonBinding | null) {
  const next = layerBindings(target).filter((b) => b.button !== button);
  if (binding) next.push(binding);
  return attempt("setBindings", { bindings: next, ...target }, "Couldn't change the button");
}

// Changes apply as they're made; Ctrl+Z puts back what the button did, in the layer it was changed in.
async function apply(binding: ButtonBinding | null, item: Item = selected.value!) {
  const { button } = item;
  const target = view.target();
  const before = bindingFor(button!.id) ?? null;
  if (!(await setBinding(target, button!.id, binding))) return false;
  rememberUndo(() => setBinding(target, button!.id, before), () => setBinding(target, button!.id, binding), "Edit button");
  return true;
}
</script>
