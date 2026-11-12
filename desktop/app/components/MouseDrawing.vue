<template>
  <!-- A top-down sketch of the mouse. `spot`s in `controlMap.ts` are in its 296 × 454 units; the shapes are drawn
       3 units in from the corner so strokes at the edge aren't clipped. -->
  <svg viewBox="0 0 296 454" fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round">
    <g transform="translate(3 3)">
      <!-- Body, open along the left edge where the thumb wheel and thumb buttons sit -->
      <path
        :stroke-width="2 * weight"
        d="M76.88 186.44V60.14c0-.71.01-1.42.04-2.12C78.21 19.86 114.91-7.15 152.06 1.68l84.81 20.17c30.84 7.33 52.6 34.88 52.6 66.58v252.34c0 58.7-47.59 106.29-106.29 106.29s-106.29-47.59-106.29-106.29V332" />
      <!-- Thumb rest -->
      <path
        :stroke-width="2 * weight"
        d="M76.88 176.55l-19.83 8.16c-14.15 5.83-25.45 16.97-31.48 31.03l-20.85 48.65c-9.6 22.4-4.3 48.41 13.31 65.27l87.89 84.13" />
      <g :stroke-width="1.45 * weight">
        <!-- Seam behind the main buttons, and the split between them -->
        <path d="M76.88 162.85H289.47M180.77 8.51V63.49M180.77 135.09V162.85" />
        <!-- Left edge between the thumb controls -->
        <path d="M76.88 238.27V252M76.88 272V282M76.88 302V312" />
        <!-- Scroll wheel and mode button -->
        <rect x="166.14" y="63.49" width="29.26" height="71.6" rx="9" />
        <rect x="173.27" y="188.7" width="15" height="20" rx="5" />
        <!-- Thumb wheel, then the gesture, forward and back buttons -->
        <rect x="64.34" y="186.44" width="25.09" height="51.82" rx="6" />
        <rect x="70.38" y="252" width="13" height="20" rx="4.5" />
        <rect x="70.38" y="282" width="13" height="20" rx="4.5" />
        <rect x="70.38" y="312" width="13" height="20" rx="4.5" />
        <!-- Haptic pad -->
        <circle cx="40.5" cy="255" r="19" />
      </g>
      <g :stroke-width="0.9 * weight" stroke-opacity="0.55">
        <!-- Ribs on both wheels -->
        <path d="M171.5 75h18.5M171.5 83h18.5M171.5 91h18.5M171.5 99h18.5M171.5 107h18.5M171.5 115h18.5M171.5 123h18.5" />
        <path d="M69.5 192v40.5M73.2 192v40.5M76.88 192v40.5M80.6 192v40.5M84.3 192v40.5" />
        <!-- The sensor is underneath; a ring marks where -->
        <circle cx="183.18" cy="330" r="14" />
      </g>
    </g>
  </svg>
</template>

<script setup lang="ts">
const { weight = 1 } = defineProps<{
  /** Scales every line's width, for when the drawing is shown much smaller than on the control map. */
  weight?: number;
}>();
</script>
