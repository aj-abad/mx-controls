<template>
  <TooltipRoot :disabled="disabled">
    <TooltipTrigger as-child>
      <slot />
    </TooltipTrigger>
    <TooltipPortal>
      <TooltipContent
        side="bottom"
        :side-offset="6"
        class="z-50 rounded-md bg-ink px-2 py-1 text-xs font-medium text-on-ink data-[state=closed]:animate-menu-out data-[state=delayed-open]:animate-menu-in">
        {{ text }}
      </TooltipContent>
    </TooltipPortal>
  </TooltipRoot>
</template>

<script setup lang="ts">
import { TooltipContent, TooltipPortal, TooltipRoot, TooltipTrigger } from "reka-ui";

defineProps<{ text: string; disabled?: boolean }>();
</script>
