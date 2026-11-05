<template>
  <div class="flex items-center justify-between gap-6">
    <div class="min-w-0">
      <component :is="labelFor ? 'label' : 'div'" :for="labelFor" class="block font-medium">{{ label }}</component>
      <UiScrollArea v-if="description" class="mt-0.5 h-10 text-sm text-ink-3">{{ description }}</UiScrollArea>
    </div>
    <div class="flex shrink-0 items-center gap-3">
      <slot />
    </div>
  </div>
</template>

<script setup lang="ts">
defineProps<{ label: string; description?: string; labelFor?: string }>();
</script>
