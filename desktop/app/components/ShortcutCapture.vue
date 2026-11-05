<template>
  <button
    ref="field"
    type="button"
    :aria-label="capturing ? 'Press keys, or Esc to cancel' : `${label}: ${chordLabel(modelValue)}`"
    :title="capturing ? (pending.length ? chordLabel(pending) : 'Press the keys…') : chordLabel(modelValue)"
    class="flex h-10 w-full min-w-0 items-center overflow-hidden rounded-lg bg-field px-3 text-left text-sm ring-1 ring-inset transition-colors"
    :class="capturing ? 'ring-2 ring-accent' : 'ring-line hover:ring-fill-strong'"
    @click="capturing ? stop() : begin()"
    @blur="onBlur">
    <span v-if="capturing" class="min-w-0 truncate text-ink-2">
      {{ pending.length ? `${chordLabel(pending)} + …` : "Press the keys…" }}
    </span>
    <span v-else-if="modelValue?.length" class="flex min-w-0 gap-1 overflow-hidden">
      <kbd
        v-for="key in modelValue"
        :key="key"
        class="shrink-0 rounded-md bg-fill px-1.5 py-0.5 font-sans text-xs font-medium">{{ keyLabel(key) }}</kbd>
    </span>
    <span v-else class="text-ink-3">Click, then press the keys</span>
  </button>
</template>

<script setup lang="ts">
const { label } = defineProps<{ modelValue: string[] | null | undefined; label: string }>();
const emit = defineEmits<{ "update:modelValue": [keys: string[]] }>();
const { captureKeys } = useAgent();

const MODIFIERS = new Set(["ShiftLeft", "ShiftRight", "ControlLeft", "ControlRight", "AltLeft", "AltRight", "MetaLeft", "MetaRight"]);

const capturing = ref(false);
/** Modifiers held so far, in the order they went down. */
const pending = ref<string[]>([]);
/** Hands the keyboard back to Windows while the agent has it; the page's own key events are ignored then. */
let release: (() => void) | null = null;
let capture: AbortController | null = null;
let session = 0;

// A chord ends at its first non-modifier key; modifiers alone end when the first of them is released.
function press(code: string | null, down: boolean) {
  if (!code || code === "Unidentified") return;
  if (!down) {
    if (MODIFIERS.has(code) && pending.value.length) finish(pending.value);
    return;
  }
  if (code === "Escape" && !pending.value.length) {
    stop();
    return;
  }
  if (MODIFIERS.has(code)) {
    if (!pending.value.includes(code)) pending.value = [...pending.value, code];
    return;
  }
  finish([...pending.value, code]);
}

function onKeyDown(event: KeyboardEvent) {
  event.preventDefault();
  event.stopPropagation();
  if (!release && !event.repeat) press(event.code, true);
}

function onKeyUp(event: KeyboardEvent) {
  event.preventDefault();
  event.stopPropagation();
  if (!release) press(event.code, false);
}

// Without the agent, releasing the Windows key alone opens Start, which takes focus before the key-up reaches
// the page, so losing focus with modifiers held ends the chord instead of dropping it. With the agent, losing
// focus must hand the keyboard back either way.
function onBlur() {
  if (pending.value.length) finish(pending.value);
  else stop();
}

function finish(keys: string[]) {
  emit("update:modelValue", keys);
  stop();
}

async function begin() {
  capturing.value = true;
  pending.value = [];
  window.addEventListener("keydown", onKeyDown, { capture: true });
  window.addEventListener("keyup", onKeyUp, { capture: true });
  const mine = ++session;
  capture = new AbortController();
  const handBack = await captureKeys({ key: ({ code, down }) => press(code, down), ended: stop }, capture.signal);
  // Recording may have ended while the agent was starting.
  if (mine === session) release = handBack;
  else handBack?.();
}

function stop() {
  session++;
  capturing.value = false;
  pending.value = [];
  window.removeEventListener("keydown", onKeyDown, { capture: true });
  window.removeEventListener("keyup", onKeyUp, { capture: true });
  capture?.abort();
  capture = null;
  release?.();
  release = null;
}

/** Starts recording without a click, for when the shortcut was just chosen. */
function start() {
  field.value?.focus();
  if (!capturing.value) begin();
}
const field = useTemplateRef("field");
defineExpose({ start });

onBeforeUnmount(stop);
</script>
