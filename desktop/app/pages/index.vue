<template>
  <FitStage v-if="device" :width="STAGE.width" :height="STAGE.height">
    <ControlMap
      :items="items"
      :selected="selectedId"
      :active="hoveredId ?? selectedId"
      :frame="frame"
      :pulse="pulse"
      @select="(id) => (selectedId = id)"
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
              :style="{ top: `${item.top}px`, height: `${LIST.row}px`, left: `${LIST.padding}px`, right: `${LIST.padding}px` }">
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
                :class="(hoveredId === item.control.id || hoveredId === group.items[i - 1]?.control.id) && 'opacity-0'" />
              <button
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
                  <IconWarning v-if="item.shared" class="size-3.5 shrink-0 text-warn" />
                  <span v-if="item.shared" class="sr-only">Also used by another app:</span>
                  <span class="truncate">{{ item.summary }}</span>
                </span>
                <IconChevronRight
                  class="size-4 shrink-0 text-ink-3 transition-transform"
                  :class="hoveredId === item.control.id && 'translate-x-0.5 text-ink'" />
              </button>
            </li>
          </ul>
        </template>
      </motion.div>

      <motion.section
        v-else
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
        <header class="px-6 pt-4 pb-5">
          <button
            type="button"
            class="-ml-1 mb-2 inline-flex items-center gap-1.5 rounded-full px-1 text-sm text-ink-3 hover:text-ink"
            @click="selectedId = null">
            <IconArrowLeft class="size-4" />
            All controls
          </button>
          <h2 class="text-lg font-semibold">{{ selected.label }}</h2>
          <p class="mt-0.5 text-sm text-ink-3">{{ DESCRIPTIONS[selected.control.id] }}</p>
          <p v-if="selected.shared" class="mt-3 flex gap-2 rounded-lg bg-warn-fill px-3 py-2 text-[13px] text-warn">
            <IconWarning class="mt-px size-4 shrink-0" />
            {{ state?.optionsPlusRunning ? "Logi Options+" : "Another app" }} also responds to it while it's left at
            its default here.
          </p>
        </header>

        <div class="flex min-h-0 flex-1 flex-col gap-6 overflow-y-auto px-6 pb-6">
          <WheelSettings v-if="selected.control.id === 'wheel'" :device="device" />
          <ThumbWheelSettings v-else-if="selected.control.id === 'thumbwheel'" :device="device" />
          <SensorSettings v-else-if="selected.control.id === 'sensor'" :device="device" />
          <template v-else-if="selected.button">
            <ButtonEditor :button="selected.button" :binding="bindingFor(selected.button.id)" :apply="apply" />
            <HapticSettings v-if="selected.control.id === 'haptic'" :device="device" />
          </template>
        </div>

        <footer class="flex h-14 shrink-0 items-center justify-between border-t border-line pr-3 pl-6">
          <span class="flex items-center gap-2 text-[13px] text-ink-3">
            <span class="size-1.5 rounded-full" :class="selected.customized ? 'bg-accent' : 'bg-fill-strong'" />
            {{ selected.customized ? "Changed from the default" : "Using the default" }}
          </span>
          <UiButton variant="ghost" size="sm" :disabled="!selected.customized" @click="resetSelected">
            Reset to default
          </UiButton>
        </footer>
      </motion.section>
    </AnimatePresence>

    <p class="absolute bottom-0 left-0 h-5 text-sm text-ink-3" aria-live="polite">
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
import { animate, AnimatePresence, motion, useReducedMotion } from "motion-v";
import type { ButtonBinding, ButtonState } from "~/composables/useAgent";
import type { ControlId } from "~/utils/controlMap";

const { state, device, lastGesture, lastPress, attempt } = useAgent();

const bindings = computed(() => state.value?.bindings ?? []);
const bindingFor = (button: string) => bindings.value.find((b) => b.button === button);

const selectedId = ref<ControlId | null>(null);
const hoveredId = ref<ControlId | null>(null);

// The controls this mouse has; a button it doesn't have has no row.
const present = computed(() => {
  const mouse = device.value;
  if (!mouse) return [];
  return CONTROLS.flatMap((control) => {
    const button = mouse.buttons.find((b) => b.id === control.id);
    if (control.id === "wheel")
      return [{ control, button, label: "Scroll wheel", summary: wheelSummary(), customized: wheelCustomized(), shared: false }];
    if (control.id === "thumbwheel") {
      if (!mouse.thumbWheel) return [];
      const customized = !isDefaultThumbWheel(state.value!.thumbWheel);
      return [
        {
          control,
          button,
          label: "Thumb wheel",
          summary: thumbWheelSummary(),
          customized,
          shared: mouse.thumbWheel.divertedElsewhere && !customized,
        },
      ];
    }
    if (control.id === "sensor") {
      const { current, default: standard } = mouse.dpi;
      return [{ control, button, label: "Sensor", summary: `${current} DPI`, customized: current !== standard, shared: false }];
    }
    if (!button) return [];
    const label = control.id === "haptic" ? "Haptic sense panel" : `${button.name} button`;
    const customized = !!bindingFor(button.id);
    return [{ control, button, label, summary: describe(button), customized, shared: button.divertedElsewhere && !customized }];
  });
});

const layout = computed(() => layoutList(present.value.map((item) => item.control)));
const items = computed(() => present.value.map((item) => ({ ...item, ...layout.value.rows.get(item.control.id)! })));
const groups = computed(() =>
  layout.value.headers.map((header) => ({ ...header, items: items.value.filter((item) => item.control.group === header.id) })),
);

const selected = computed(() => items.value.find((item) => item.control.id === selectedId.value) ?? null);

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

const DESCRIPTIONS: Record<ControlId, string> = {
  wheel: "In ratchet mode the wheel clicks, and switches to free spin when you scroll fast.",
  thumbwheel: "Scrolls sideways, or runs an action for each step you turn it.",
  middle: "Pressing down on the scroll wheel.",
  smartshift: "The button behind the wheel. On its own it switches the wheel between ratchet and free spin.",
  back: "On its own it goes back, in browsers and file windows.",
  forward: "On its own it goes forward, in browsers and file windows.",
  gesture: "The button in the thumb rest, made for gestures: hold it and move the mouse.",
  haptic: "The panel under your thumb. It clicks with a haptic tap rather than a switch.",
  sensor: "Stored on the mouse, so it follows the mouse to other computers.",
};

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
  const wheel = state.value!.thumbWheel;
  const inverted = wheel.inverted ? ", inverted" : "";
  if (wheel.mode === "actions")
    return `${describeAction(wheel.left ?? NOTHING)} / ${describeAction(wheel.right ?? NOTHING)}${inverted}`;
  return wheel.speed === 100 ? `Scroll sideways${inverted}` : `Scroll sideways ${wheel.speed}%${inverted}`;
}

function describe(button: ButtonState) {
  const binding = bindingFor(button.id);
  if (binding?.mode === "gesture") return "Gestures";
  if (binding) return describeAction(binding.action ?? NOTHING);
  // Back, forward and middle are named after what they do on their own, so their defaults can say it.
  return MOUSE_BUTTONS[button.id] ?? "Default";
}

// Escape closes the panel, unless it's closing a menu or popover first; those are still in the page when it arrives.
function onKey(event: KeyboardEvent) {
  if (event.key !== "Escape" || !selectedId.value || event.defaultPrevented) return;
  if (!document.querySelector("[data-dismissable-layer]")) selectedId.value = null;
}
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
const DIRECTION_LABELS: Record<string, string> = { click: "Click", up: "↑ Up", down: "↓ Down", left: "← Left", right: "→ Right" };
const flashed = ref<{ id: number; name: string; label: string } | null>(null);
let flashes = 0;
let clearFlash: ReturnType<typeof setTimeout> | undefined;
watch(lastGesture, (gesture) => {
  const button = device.value?.buttons.find((b) => b.id === gesture?.button);
  if (!gesture || !button) return;
  flashed.value = { id: ++flashes, name: button.name, label: DIRECTION_LABELS[gesture.direction] ?? gesture.direction };
  clearTimeout(clearFlash);
  clearFlash = setTimeout(() => (flashed.value = null), 1500);
});
onBeforeUnmount(() => clearTimeout(clearFlash));

/** Puts the open control back to its default; the toast can put back what it was. */
function resetSelected() {
  const item = selected.value!;
  const mouse = device.value!;
  if (item.button) return apply(null);
  if (item.control.id === "wheel") {
    const shift = mouse.smartShift;
    return resetWith(
      item.label,
      "setSmartShift",
      { ratchet: true, autoDisengage: shift.defaultAutoDisengage, torque: shift.defaultTorque },
      { ratchet: shift.ratchet, autoDisengage: shift.autoDisengage, torque: shift.torque },
    );
  }
  if (item.control.id === "thumbwheel")
    return resetWith(
      item.label,
      "setThumbWheel",
      { thumbWheel: { mode: "scroll", speed: 100, inverted: false } },
      { thumbWheel: { ...state.value!.thumbWheel } },
    );
  return resetWith(item.label, "setDpi", { dpi: mouse.dpi.default }, { dpi: mouse.dpi.current });
}

async function resetWith(label: string, method: string, params: Record<string, unknown>, before: Record<string, unknown>) {
  if (!(await attempt(method, params, `Couldn't reset the ${label.toLowerCase()}`))) return;
  showToast(`${label} is back to its default`, "neutral", {
    key: method,
    action: { label: "Undo", run: () => void attempt(method, before, `Couldn't put the ${label.toLowerCase()} back`) },
  });
}

/** Sets one button's binding, or its default for null, keeping the others. */
function setBinding(button: string, binding: ButtonBinding | null) {
  const next = bindings.value.filter((b) => b.button !== button);
  if (binding) next.push(binding);
  return attempt("setBindings", { bindings: next }, "Couldn't change the button");
}

// Changes apply as they're made; the toast says what the button does now and can put back what it did.
async function apply(binding: ButtonBinding | null) {
  const { label, button } = selected.value!;
  const before = bindingFor(button!.id) ?? null;
  if (!(await setBinding(button!.id, binding))) return false;
  showToast(`${label}: ${describe(button!)}${binding ? "" : " (default)"}`, "neutral", {
    key: button!.id,
    action: { label: "Undo", run: () => void setBinding(button!.id, before) },
  });
  return true;
}
</script>
