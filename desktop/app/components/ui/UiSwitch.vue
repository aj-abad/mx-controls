<template>
  <SwitchRoot
    v-model="checked"
    :id="id"
    :aria-label="ariaLabel"
    class="relative inline-flex h-6 w-10 shrink-0 items-center rounded-full bg-fill-strong transition-colors data-[state=checked]:bg-ink">
    <SwitchThumb as-child>
      <motion.span
        class="block size-5 rounded-full shadow-sm"
        :class="checked ? 'bg-on-ink' : 'bg-white'"
        :initial="false"
        :animate="{ x: checked ? 18 : 2 }"
        :transition="FAST" />
    </SwitchThumb>
  </SwitchRoot>
</template>

<script setup lang="ts">
import { motion } from "motion-v";
import { SwitchRoot, SwitchThumb } from "reka-ui";

defineProps<{ id?: string; ariaLabel?: string }>();
const checked = defineModel<boolean>({ required: true });
</script>
