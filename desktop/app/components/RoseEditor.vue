<template>
  <div class="flex flex-col gap-4">
    <div class="flex items-center justify-between gap-3">
      <nav aria-label="Folder" class="flex min-w-0 items-center gap-1 text-xs">
        <template v-for="(crumb, i) in crumbs" :key="i">
          <PhCaretRight v-if="i" class="size-3 shrink-0 text-ink-3" />
          <button
            type="button"
            class="min-w-0 truncate rounded-md px-1.5 py-0.5"
            :class="i === crumbs.length - 1 ? 'bg-fill font-medium text-ink' : 'text-ink-3 hover:bg-fill hover:text-ink'"
            :aria-current="i === crumbs.length - 1 ? 'location' : undefined"
            @click="goTo(i)">
            {{ crumb }}
          </button>
        </template>
      </nav>
      <div class="flex shrink-0 items-center gap-1" role="group" aria-label="Number of options">
        <button
          type="button"
          aria-label="Fewer options"
          :disabled="count <= ROSE_MIN"
          class="grid size-7 place-items-center rounded-lg text-ink-2 hover:bg-fill disabled:opacity-30"
          @click="setCount(count - 1)">
          <PhMinus class="size-3.5" />
        </button>
        <span class="w-5 text-center text-sm font-medium tabular-nums" aria-live="polite">{{ count }}</span>
        <button
          type="button"
          aria-label="More options"
          :disabled="count >= ROSE_MAX"
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
          :class="live === i ? 'fill-accent/40' : editing === i ? 'fill-accent/15' : 'fill-transparent hover:fill-fill'"
          @click="editing = i"
          @dblclick="slot.folder && open(i)" />
        <line
          v-for="i in count"
          :key="`d${i}`"
          :x1="point(INNER + 2, edge(i - 1)).x"
          :y1="point(INNER + 2, edge(i - 1)).y"
          :x2="point(OUTER, edge(i - 1)).x"
          :y2="point(OUTER, edge(i - 1)).y"
          class="pointer-events-none stroke-line" />
        <path
          v-for="(slot, i) in slots"
          v-show="slot.folder"
          :key="`f${i}`"
          :d="chevron(i)"
          fill="none"
          class="pointer-events-none stroke-ink-3"
          stroke-width="1.6"
          stroke-linecap="round"
          stroke-linejoin="round" />
        <path
          v-if="editing < count"
          :d="rim(editing)"
          fill="none"
          class="pointer-events-none stroke-accent"
          stroke-width="3"
          stroke-linecap="round" />
        <circle r="112" fill="none" class="pointer-events-none stroke-fill-strong" />
        <circle
          :r="INNER"
          class="fill-panel stroke-fill-strong"
          :class="[live === null && liveOpen && 'stroke-accent', path.length && 'cursor-pointer hover:fill-fill']"
          @click="path.length && goTo(path.length - 1)" />
      </svg>
      <component
        :is="path.length ? PhArrowLeft : PhX"
        class="pointer-events-none absolute top-1/2 left-1/2 size-4 -translate-1/2 text-ink-3" />
      <button
        v-for="(slot, i) in slots"
        :key="i"
        type="button"
        :aria-pressed="editing === i"
        :aria-label="`Option ${i + 1}, ${POSITIONS[count]?.[i] ?? ''}: ${slot.text}${slot.folder ? ', folder' : ''}`"
        class="absolute flex w-18 -translate-1/2 flex-col items-center gap-0.5 rounded-md px-0.5 text-center"
        :style="labelStyle(i)"
        @click="editing = i"
        @dblclick="slot.folder && open(i)">
        <component
          :is="ROSE_ICONS[slot.item.icon ?? '']"
          v-if="slot.item.icon && ROSE_ICONS[slot.item.icon]"
          class="size-4"
          :class="slot.dim ? 'text-ink-3' : 'text-ink'" />
        <span
          class="line-clamp-2 text-[10px] leading-tight font-semibold tracking-wide uppercase"
          :class="slot.empty ? 'text-ink-3/70' : slot.dim ? 'text-ink-3' : 'text-ink'">
          {{ slot.text }}
        </span>
        <span v-if="slot.inherited" class="size-1 rounded-full bg-accent" aria-hidden="true" />
      </button>
    </div>
    <p class="-mt-1 text-center text-[11px] leading-relaxed text-ink-3">
      {{
        path.length
          ? "Rest in the middle to go back a folder. Letting go there runs nothing."
          : "Move toward an option and let go. Push on to the rim of a folder to open it."
      }}
    </p>

    <section class="flex flex-col gap-3" :aria-label="`Option ${editing + 1}`">
      <div class="flex items-center justify-between">
        <h4 class="text-xs font-medium text-ink-3">
          Option {{ editing + 1 }} <span class="text-ink-3/70">· {{ POSITIONS[count]?.[editing] }}</span>
        </h4>
        <UiCheckbox
          :model-value="!!current.passThrough"
          label="Same as main layer"
          class="text-xs! text-ink-2"
          @update:model-value="setPassThrough" />
      </div>

      <p v-if="current.passThrough" class="rounded-lg bg-fill/60 px-3 py-2.5 text-[13px] leading-relaxed text-ink-3">
        {{ passThroughText }}
      </p>
      <template v-else>
        <UiSegmented
          :model-value="current.items ? 'folder' : 'action'"
          :options="KINDS"
          :label="`Option ${editing + 1} kind`"
          @update:model-value="(kind) => setKind(kind as 'action' | 'folder')" />
        <div v-if="current.items" class="flex h-10 items-center gap-2 rounded-lg bg-fill/60 px-3 text-[13px]">
          <PhFolder class="size-4 shrink-0 text-ink-3" />
          <span class="min-w-0 flex-1 truncate text-ink-2">{{ folderSummary(current.items) }}</span>
          <UiButton size="sm" @click="open(editing)">Open folder ›</UiButton>
        </div>
        <ActionEditor
          v-else
          :key="`${pathKey}:${editing}`"
          :model-value="current.action"
          :label="`Rose option ${editing + 1}`"
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
            :placeholder="current.items ? 'Folder name' : current.action.type === 'none' ? 'Label' : describeAction(current.action)"
            :aria-label="`Option ${editing + 1} label`"
            class="h-9 min-w-0 flex-1 rounded-lg bg-field px-3 text-sm ring-1 ring-line ring-inset outline-none! placeholder:text-ink-3 focus:ring-ink"
            @change="(event) => patch({ label: (event.target as HTMLInputElement).value.trim() || null })" />
        </div>
      </template>
    </section>
  </div>
</template>

<script setup lang="ts">
import {
  PhArrowLeft,
  PhCaretRight,
  PhFolder,
  PhImageSquare,
  PhMinus,
  PhPlus,
  PhProhibit,
  PhX,
} from "@phosphor-icons/vue";
import { PopoverRoot, PopoverTrigger } from "reka-ui";
import type { RoseItem } from "~/composables/useAgent";

const { modelValue, name, below, liveButtons = [] } = defineProps<{
  /** The menu's own options; folders hold theirs. */
  modelValue: RoseItem[];
  /** The menu's name, which starts the folder trail. */
  name: string;
  /**
   * The main layer's rose options where this menu shows in a second layer, to show what passes through; null
   * where the main layer has none. Undefined where the menu is edited on its own.
   */
  below?: RoseItem[] | null;
  /** Buttons showing this menu in the profile running, whose use on the mouse lights up the options. */
  liveButtons?: string[];
}>();
const emit = defineEmits<{ "update:modelValue": [items: RoseItem[]] }>();

const MAX_LABEL = 24;
const OUTER = 112;
const INNER = 34;
const KINDS = [
  { value: "action", label: "Action" },
  { value: "folder", label: "Folder" },
];

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

/** The folders open, by index from the menu's own ring. */
const path = ref<number[]>([]);
const pathKey = computed(() => path.value.join("."));
const editing = ref(0);

// An undo or an edit elsewhere can take away the folder that's open; the trail goes back as far as it still reaches.
const level = computed(() => {
  for (let depth = path.value.length; depth >= 0; depth--) {
    const found = roseLevel(modelValue, path.value.slice(0, depth));
    if (found) return { items: found, depth };
  }
  return { items: modelValue, depth: 0 };
});
watch(
  () => level.value.depth,
  (depth) => depth < path.value.length && (path.value = path.value.slice(0, depth)),
);
const items = computed(() => level.value.items);
const count = computed(() => items.value.length);
watch(count, (n) => editing.value >= n && (editing.value = n - 1));
const current = computed(() => items.value[editing.value] ?? { action: NOTHING });

const crumbs = computed(() => {
  const names = [name];
  let at = modelValue;
  for (const index of path.value) {
    const item = at[index];
    names.push(item?.label || `Option ${index + 1}`);
    at = item?.items ?? [];
  }
  return names;
});

/** Goes back to the folder `depth` deep, with the folder it came out of picked. */
function goTo(depth: number) {
  if (depth >= path.value.length) return;
  editing.value = path.value[depth] ?? 0;
  path.value = path.value.slice(0, depth);
}
function open(index: number) {
  if (!items.value[index]?.items) return;
  path.value = [...path.value, index];
  editing.value = 0;
}

/** The main layer's option in a place of the ring showing, as far as its folders match this one's. */
const belowLevel = computed(() => (below ? roseLevel(below, path.value) : null));
const belowItem = (i: number) => {
  const item = belowLevel.value?.[i];
  return item && !item.passThrough ? item : null;
};
const passThroughText = computed(() => {
  if (below === undefined)
    return "Where a button shows this menu in its second layer, this option runs the main layer's option in the same place.";
  const item = belowItem(editing.value);
  return item ? `Runs the main layer's option here: ${describeRoseItem(item)}.` : "The main layer has no option here, so this one does nothing.";
});

const folderSummary = (options: RoseItem[]) => {
  const named = options.filter((o) => o.items || o.action.type !== "none" || o.passThrough).length;
  return `${options.length} options, ${named} set. Double-click it in the wheel to open it too.`;
};

/** What the wheel shows in each place: a pass-through option shows the main layer's, dimmed. */
const slots = computed(() =>
  items.value.map((own, i) => {
    const inherited = !!own.passThrough;
    const item = inherited ? (belowItem(i) ?? { action: NOTHING }) : own;
    const folder = !!item.items;
    const empty = !folder && item.action.type === "none";
    const text = inherited && below === undefined ? "Main layer" : empty ? "—" : describeRoseItem(item);
    return { item, inherited, empty, folder: folder && !inherited, dim: inherited, text };
  }),
);

function emitLevel(change: (level: RoseItem[]) => RoseItem[]) {
  emit("update:modelValue", withRoseLevel(modelValue, path.value, change));
}

// Options removed by lowering the count come back when it's raised again, until another folder or menu opens.
let removed: RoseItem[] = [];
watch([pathKey, () => name], () => (removed = []));
function setCount(n: number) {
  emitLevel((level) => {
    const next = [...level];
    while (next.length > n) removed.unshift(next.pop()!);
    while (next.length < n) next.push(removed.shift() ?? { action: NOTHING });
    return next;
  });
}

function patch(change: Partial<RoseItem>) {
  emitLevel((level) => level.map((item, i) => (i === editing.value ? { ...item, ...change } : item)));
}

function setPassThrough(on: boolean) {
  patch(on ? { action: NOTHING, label: null, icon: null, iconPath: null, items: null, passThrough: true } : { passThrough: false });
}

// A folder turned back into an action keeps its options until another option is picked, so turning it back is free.
let folded: { at: string; items: RoseItem[] } | null = null;
function setKind(kind: "action" | "folder") {
  const at = `${pathKey.value}:${editing.value}`;
  if (kind === "folder") {
    patch({ action: NOTHING, items: folded?.at === at ? folded.items : emptyOptions(4) });
    folded = null;
  } else if (current.value.items) {
    folded = { at, items: current.value.items };
    patch({ items: null });
  }
}

const iconsOpen = ref(false);
function setIcon(icon: string | null) {
  patch({ icon, iconPath: icon ? roseIconPath(icon) : null });
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
/** A chevron by the rim, pointing out, as the overlay marks a folder. */
function chevron(i: number) {
  const angle = i * width.value;
  const tip = point(OUTER - 8, angle);
  const [left, right] = [point(OUTER - 13, angle - 4), point(OUTER - 13, angle + 4)];
  return `M${left.x},${left.y}L${tip.x},${tip.y}L${right.x},${right.y}`;
}
function labelStyle(i: number) {
  const { x, y } = point(INNER + (OUTER - INNER) * 0.55, i * width.value);
  return { left: `${50 + (x / 240) * 100}%`, top: `${50 + (y / 240) * 100}%` };
}

// Holding a button that shows this menu lights up the option it points at, in the folder shown.
const { lastRose } = useAgent();
const live = ref<number | null>(null);
const liveOpen = ref(false);
let unlight: ReturnType<typeof setTimeout> | undefined;
watch(lastRose, (event) => {
  if (!event || !event.button || !liveButtons.includes(event.button)) return;
  clearTimeout(unlight);
  const here = event.path.join(".") === pathKey.value;
  live.value = here ? event.option : null;
  liveOpen.value = here && !event.ran;
  if (event.ran || event.option === null || !here)
    unlight = setTimeout(() => ((live.value = null), (liveOpen.value = false)), event.ran ? 700 : 400);
});
onBeforeUnmount(() => clearTimeout(unlight));
</script>
