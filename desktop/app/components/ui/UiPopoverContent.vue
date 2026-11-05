<template>
  <PopoverPortal>
    <!-- Mounted only while open, so AnimatePresence sees it leave and can play the exit. -->
    <AnimatePresence>
      <PopoverContent v-if="open" force-mount as-child side="bottom" :align="align" :side-offset="8" :collision-padding="16">
        <!-- Grows out of its trigger, the way a macOS popover does. -->
        <motion.div
          v-bind="$attrs"
          class="z-40 origin-(--reka-popover-content-transform-origin) rounded-2xl bg-field shadow-lg ring-1 ring-line"
          :initial="{ opacity: 0, scale: 0.97 }"
          :animate="{ opacity: 1, scale: 1, transition: BASE }"
          :exit="{ opacity: 0, transition: EXIT }">
          <slot />
        </motion.div>
      </PopoverContent>
    </AnimatePresence>
  </PopoverPortal>
</template>

<script setup lang="ts">
import { AnimatePresence, motion } from "motion-v";
import { PopoverContent, PopoverPortal } from "reka-ui";

defineOptions({ inheritAttrs: false });
const { align = "start" } = defineProps<{ open: boolean; align?: "start" | "center" | "end" }>();
</script>
