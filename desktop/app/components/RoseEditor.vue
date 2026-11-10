<template>
  <div class="flex flex-col gap-4">
    <div class="flex items-center justify-between">
      <span class="text-xs text-ink-3">Move toward an option and let go. Back in the middle runs nothing.</span>
      <div class="flex shrink-0 items-center gap-1" role="group" aria-label="Number of options">
        <button
          type="button"
          aria-label="Fewer options"
          :disabled="count <= MIN"
          class="grid size-7 place-items-center rounded-lg text-ink-2 hover:bg-fill disabled:opacity-30"
          @click="setCount(count - 1)">
          <PhMinus class="size-3.5" />
        </button>
        <span class="w-5 text-center text-sm font-medium tabular-nums" aria-live="polite">{{ count }}</span>
        <button
          type="button"
          aria-label="More options"
          :disabled="count >= MAX"
          class="grid size-7 place-items-center rounded-lg text-ink-2 hover:bg-fill disabled:opacity-30"
          @click="setCount(count + 1)">
          <PhPlus class="size-3.5" />
        </button>
      </div>
    </div>

    <div class="relative mx-auto size-60 shrink-0">
      <svg viewBox="-120 -120 240 240" class="absolute inset-0 size-full" aria-hidden="true">
        <circle r="112" class="fill-fill" />
        <path
          v-for="(slot, i) in slots"
          :key="i"
          :d="sector(i)"
          class="cursor-pointer transition-colors duration-150"
          :class="
            live === i
              ? 'fill-accent/40'
              : editing === i
                ? 'fill-accent/15'
                : 'fill-transparent hover:fill-fill'
          "
          @click="editing = i" />
        <line
          v-for="i in count"
          :key="`d${i}`"
          :x1="point(INNER + 2, edge(i - 1)).x"
          :y1="point(INNER + 2, edge(i - 1)).y"
          :x2="point(OUTER, edge(i - 1)).x"
          :y2="point(OUTER, edge(i - 1)).y"
          class="pointer-events-none stroke-line" />
        <path
          v-if="editing < count"
          :d="rim(editing)"
          fill="none"
          class="pointer-events-none stroke-accent"
          stroke-width="3"
          stroke-linecap="round" />
        <circle r="112" fill="none" class="pointer-events-none stroke-fill-strong" />
        <circle :r="INNER" class="pointer-events-none fill-panel stroke-fill-strong" :class="live === null && liveOpen && 'stroke-accent'" />
      </svg>
      <PhX class="pointer-events-none absolute top-1/2 left-1/2 size-4 -translate-1/2 text-ink-3" />
      <button
        v-for="(slot, i) in slots"
        :key="i"
        type="button"
        :aria-pressed="editing === i"
        :aria-label="`Option ${i + 1}, ${POSITIONS[count]?.[i] ?? ''}: ${slot.text}`"
        class="absolute flex w-18 -translate-1/2 flex-col items-center gap-0.5 rounded-md px-0.5 text-center"
        :style="labelStyle(i)"
        @click="editing = i">
        <component :is="ROSE_ICONS[slot.item.icon ?? '']" v-if="slot.item.icon && ROSE_ICONS[slot.item.icon]" class="size-4" :class="slot.dim ? 'text-ink-3' : 'text-ink'" />
        <span
          class="line-clamp-2 text-[10px] leading-tight font-semibold tracking-wide uppercase"
          :class="slot.empty ? 'text-ink-3/70' : slot.dim ? 'text-ink-3' : 'text-ink'">
          {{ slot.text }}
        </span>
        <span v-if="slot.inherited" class="size-1 rounded-full bg-accent" aria-hidden="true" />
      </button>
    </div>

    <section class="flex flex-col gap-3" :aria-label="`Option ${editing + 1}`">
      <div class="flex items-center justify-between">
        <h4 class="text-xs font-medium text-ink-3">
          Option {{ editing + 1 }} <span class="text-ink-3/70">· {{ POSITIONS[count]?.[editing] }}</span>
        </h4>
        <UiCheckbox
          v-if="below !== undefined"
          :model-value="!!current.passThrough"
          label="Same as main layer"
          class="text-xs! text-ink-2"
          @update:model-value="setPassThrough" />
      </div>

      <p v-if="current.passThrough" class="rounded-lg bg-fill/60 px-3 py-2.5 text-[13px] leading-relaxed text-ink-3">
        {{
          inheritedItem
            ? `Runs the main layer's option here: ${describeItem(inheritedItem)}.`
            : "The main layer has no option here, so this one does nothing."
        }}
      </p>
      <template v-else>
        <ActionEditor
          :key="`${buttonId}:${editing}`"
          :model-value="current.action"
          :label="`${buttonName} rose option ${editing + 1}`"
          :allow-hold="false"
          :allow-repeat="false"
          layers="toggle"
          profiles
          @update:model-value="(action) => patch({ action })" />
        <div class="flex items-center gap-2">
          <PopoverRoot v-model:open="iconsOpen">
            <PopoverTrigger
              class="grid size-9 shrink-0 place-items-center rounded-lg bg-field ring-1 ring-line ring-inset hover:ring-fill-strong"
              :aria-label="current.icon ? `Icon: ${current.icon.replaceAll('-', ' ')}` : 'Choose an icon'">
              <component :is="ROSE_ICONS[current.icon]" v-if="current.icon && ROSE_ICONS[current.icon]" class="size-4.5" />
              <PhImageSquare v-else class="size-4.5 text-ink-3" />
            </PopoverTrigger>
            <UiPopoverContent :open="iconsOpen" class="w-80 p-2">
              <div class="grid grid-cols-8 gap-1" role="group" aria-label="Icons">
                <button
                  type="button"
                  aria-label="No icon"
                  :aria-pressed="!current.icon"
                  class="grid size-8 place-items-center rounded-md text-xs text-ink-3"
                  :class="!current.icon ? 'bg-ink text-on-ink' : 'hover:bg-fill'"
                  @click="setIcon(null)">
                  <PhProhibit class="size-4" />
                </button>
                <button
                  v-for="(icon, name) in ROSE_ICONS"
                  :key="name"
                  type="button"
                  :aria-label="name.replaceAll('-', ' ')"
                  :aria-pressed="current.icon === name"
                  class="grid size-8 place-items-center rounded-md"
                  :class="current.icon === name ? 'bg-ink text-on-ink' : 'text-ink-2 hover:bg-fill hover:text-ink'"
                  @click="setIcon(name)">
                  <component :is="icon" class="size-4" />
                </button>
              </div>
            </UiPopoverContent>
          </PopoverRoot>
          <input
            :value="current.label ?? ''"
            :maxlength="MAX_LABEL"
            :placeholder="current.action.type === 'none' ? 'Label' : describeAction(current.action)"
            :aria-label="`Option ${editing + 1} label`"
            class="h-9 min-w-0 flex-1 rounded-lg bg-field px-3 text-sm ring-1 ring-line ring-inset outline-none! placeholder:text-ink-3 focus:ring-ink"
            @change="(event) => patch({ label: (event.target as HTMLInputElement).value.trim() || null })" />
        </div>
      </template>
    </section>
  </div>
</template>

<script setup lang="ts">
import { PhImageSquare, PhMinus, PhPlus, PhProhibit, PhX } from "@phosphor-icons/vue";
import { PopoverRoot, PopoverTrigger } from "reka-ui";
import type { RoseItem, RoseMenu } from "~/composables/useAgent";

const { modelValue, below, buttonId, buttonName } = defineProps<{
  modelValue: RoseMenu;
  /** In the second layer, the main layer's rose for this button, or null where it has none. Undefined in the main layer. */
  below?: RoseMenu | null;
  buttonId: string;
  buttonName: string;
}>();
const emit = defineEmits<{ "update:modelValue": [rose: RoseMenu] }>();

const MIN = 2;
const MAX = 8;
const MAX_LABEL = 24;
const OUTER = 112;
const INNER = 34;

// Where each option sits, for people who can't see the wheel.
const POSITIONS: Record<number, string[]> = {
  2: ["top", "bottom"],
  3: ["top", "lower right", "lower left"],
  4: ["top", "right", "bottom", "left"],
  5: ["top", "upper right", "lower right", "lower left", "upper left"],
  6: ["top", "upper right", "lower right", "bottom", "lower left", "upper left"],
  7: ["top", "upper right", "right", "lower right", "lower left", "left", "upper left"],
  8: ["top", "upper right", "right", "lower right", "bottom", "lower left", "left", "upper left"],
};

const count = computed(() => modelValue.items.length);
const editing = ref(0);
watch(count, (n) => editing.value >= n && (editing.value = n - 1));
const current = computed(() => modelValue.items[editing.value] ?? { action: NOTHING });
const inheritedItem = computed(() => {
  const item = below?.items[editing.value];
  return item && !item.passThrough ? item : null;
});

const describeItem = (item: RoseItem) => item.label || (item.action.type === "none" ? "nothing" : describeAction(item.action));

/** What the wheel shows in each place: a pass-through option shows the main layer's, dimmed. */
const slots = computed(() =>
  modelValue.items.map((own, i) => {
    const inherited = !!own.passThrough;
    const item = inherited ? (below?.items[i] && !below.items[i]!.passThrough ? below.items[i]! : { action: NOTHING }) : own;
    const empty = item.action.type === "none";
    return { item, inherited, empty, dim: inherited, text: empty ? "—" : (item.label || describeAction(item.action)) };
  }),
);

// Options removed by lowering the count come back when it's raised again, until the button changes.
let removed: RoseItem[] = [];
watch(() => buttonId, () => (removed = []));
function setCount(n: number) {
  const items = [...modelValue.items];
  while (items.length > n) removed.unshift(items.pop()!);
  while (items.length < n) items.push(removed.shift() ?? fresh(items.length));
  emit("update:modelValue", { items });
}

/** A new option: empty, or in the second layer the main layer's option in that place. */
function fresh(index: number): RoseItem {
  return below?.items[index] ? { action: NOTHING, passThrough: true } : { action: NOTHING };
}

function patch(change: Partial<RoseItem>) {
  const items = modelValue.items.map((item, i) => (i === editing.value ? { ...item, ...change } : item));
  emit("update:modelValue", { items });
}

function setPassThrough(on: boolean) {
  patch(on ? { action: NOTHING, label: null, icon: null, iconPath: null, passThrough: true } : { passThrough: false });
}

const iconsOpen = ref(false);
function setIcon(name: string | null) {
  patch({ icon: name, iconPath: name ? roseIconPath(name) : null });
  iconsOpen.value = false;
}

// Angles in degrees clockwise from the top, as the overlay draws them.
const width = computed(() => 360 / count.value);
const edge = (i: number) => (i - 0.5) * width.value;
function point(r: number, degrees: number) {
  const a = (degrees * Math.PI) / 180;
  return { x: r * Math.sin(a), y: -r * Math.cos(a) };
}
function sector(i: number) {
  const [a0, a1] = [edge(i), edge(i + 1)];
  const large = width.value > 180 ? 1 : 0;
  const [o0, o1, i1, i0] = [point(OUTER, a0), point(OUTER, a1), point(INNER, a1), point(INNER, a0)];
  return `M${o0.x},${o0.y}A${OUTER},${OUTER} 0 ${large} 1 ${o1.x},${o1.y}L${i1.x},${i1.y}A${INNER},${INNER} 0 ${large} 0 ${i0.x},${i0.y}Z`;
}
function rim(i: number) {
  const r = OUTER - 2;
  const [p0, p1] = [point(r, edge(i) + 2), point(r, edge(i + 1) - 2)];
  return `M${p0.x},${p0.y}A${r},${r} 0 0 1 ${p1.x},${p1.y}`;
}
function labelStyle(i: number) {
  const { x, y } = point(INNER + (OUTER - INNER) * 0.55, i * width.value);
  return { left: `${50 + (x / 240) * 100}%`, top: `${50 + (y / 240) * 100}%` };
}

// Holding the button on the mouse lights up the option it points at, so the wheel can be tried while it's edited.
const { lastRose } = useAgent();
const live = ref<number | null>(null);
const liveOpen = ref(false);
let unlight: ReturnType<typeof setTimeout> | undefined;
watch(lastRose, (event) => {
  if (!event || event.button !== buttonId) return;
  clearTimeout(unlight);
  live.value = event.option;
  liveOpen.value = !event.ran;
  if (event.ran || event.option === null) unlight = setTimeout(() => ((live.value = null), (liveOpen.value = false)), event.ran ? 700 : 400);
});
onBeforeUnmount(() => clearTimeout(unlight));
</script>
