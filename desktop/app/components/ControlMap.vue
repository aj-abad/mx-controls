<template>
  <MouseDrawing
    role="img"
    aria-label="MX Master 4"
    class="absolute text-ink-3"
    :style="{
      left: `${frame.image.x}px`,
      top: `${frame.image.y}px`,
      width: `${imageSize(frame).width}px`,
      height: `${imageSize(frame).height}px`,
    }" />

  <!-- The dots are drawn here with the lines rather than as elements of their own: the stage is zoomed by a
       fractional amount, and elements at fractional spots each snap to the pixel grid on their own, more so
       while a transition lifts them onto a layer, so they shimmered against the lines and the drawing. -->
  <svg
    class="pointer-events-none absolute inset-0"
    :width="STAGE.width"
    :height="STAGE.height"
    :viewBox="`0 0 ${STAGE.width} ${STAGE.height}`"
    fill="none"
    aria-hidden="true">
    <!-- Shown the moment the pointer arrives: it follows every hover, so animating it would only make it lag. -->
    <path
      v-for="line in lines"
      v-show="line.id === active"
      :key="line.id"
      :d="line.path"
      stroke-linecap="round"
      stroke-linejoin="round"
      class="stroke-ink [stroke-width:2]" />

    <g
      v-for="dot in dots"
      :key="dot.id"
      class="light-dots transition-opacity duration-150 ease-out [&>*]:origin-center [&>*]:[transform-box:fill-box]"
      :class="selected && dot.id !== selected && dot.id !== active && 'opacity-50'">
      <!-- Pressing the control on the mouse sends a ring out from its dot, so you can find it by pressing it. -->
      <motion.circle
        v-if="pulse?.id === dot.id"
        :key="pulse.n"
        :cx="dot.x"
        :cy="dot.y"
        r="12.5"
        stroke-width="2"
        class="stroke-accent"
        :initial="{ scale: 1, opacity: 0.9 }"
        :animate="{ scale: 2.4, opacity: 0 }"
        :transition="PULSE" />
      <!-- A soft halo on hover says the dot can be clicked. -->
      <circle
        :cx="dot.x"
        :cy="dot.y"
        r="20"
        class="fill-ink/10 transition-[opacity,scale] duration-150 ease-out"
        :class="dot.id === active ? 'scale-100 opacity-100' : 'scale-50 opacity-0'" />
      <circle
        :cx="dot.x"
        :cy="dot.y"
        r="12.5"
        stroke-width="1"
        class="drop-shadow-[0_1px_3px_rgb(0_0_0/0.22)] transition-[fill,stroke,scale]"
        :class="[dot.id === active ? 'fill-ink stroke-ink' : 'fill-white stroke-black/15', dot.id === pressed && 'scale-90']" />
      <circle
        :cx="dot.x"
        :cy="dot.y"
        r="4"
        class="transition-[fill,scale]"
        :class="[
          dot.id === active ? 'fill-white' : dot.customized ? 'fill-accent' : 'fill-fill-strong',
          dot.id === pressed && 'scale-90',
        ]" />
    </g>
  </svg>

  <!-- What you click; the dot it sits on is drawn above. Kept out of the tab order, as the list reaches every control. -->
  <UiContextMenu v-for="(item, i) in items" :key="item.control.id" :items="menu?.(item.control.id) ?? []">
    <button
      type="button"
      tabindex="-1"
      :aria-label="item.label"
      :aria-pressed="selected === item.control.id"
      class="absolute size-10 -translate-x-1/2 -translate-y-1/2 cursor-pointer rounded-full"
      :style="{ left: `${dots[i]!.x}px`, top: `${dots[i]!.y}px` }"
      @click="emit('select', item.control.id)"
      @pointerdown="pressed = item.control.id"
      @pointerup="pressed = null"
      @pointerenter="emit('hover', item.control.id)"
      @pointerleave="(pressed = null), emit('hover', null)" />
  </UiContextMenu>
</template>

<script setup lang="ts">
import { motion } from "motion-v";
import type { Control, ControlId, Frame } from "~/utils/controlMap";
import type { MenuEntry } from "~/utils/menu";

const { items, frame } = defineProps<{
  /** The controls this mouse has, each with the top of its row in the list. */
  items: { control: Control; top: number; label: string; customized: boolean }[];
  selected: ControlId | null;
  /** The control under the pointer, or the one open. */
  active: ControlId | null;
  frame: Frame;
  /** The control last pressed on the mouse; `n` counts presses, so each one plays again. */
  pulse: { id: ControlId; n: number } | null;
  /** What right-clicking a control's dot offers. */
  menu?: (id: ControlId) => MenuEntry[];
}>();
const emit = defineEmits<{ select: [id: ControlId]; hover: [id: ControlId | null] }>();

const pressed = ref<ControlId | null>(null);

const lines = computed(() =>
  items.map(({ control, top }) => ({ id: control.id, path: routePath(control, top, frame) })),
);
const dots = computed(() =>
  items.map(({ control, customized }) => ({ id: control.id, customized, ...dotAt(control, frame) })),
);
</script>
