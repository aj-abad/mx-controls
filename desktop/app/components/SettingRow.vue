<template>
  <div class="flex items-center justify-between gap-6">
    <component :is="labelFor ? 'label' : 'div'" :for="labelFor" class="min-w-0">
      <span class="font-medium">{{ label }}</span>
      <p v-if="description" class="mt-0.5 text-sm text-ink-3">{{ description }}</p>
    </component>
    <div class="flex shrink-0 items-center gap-3">
      <slot />
    </div>
  </div>
</template>

<script setup lang="ts">
defineProps<{ label: string; description?: string; labelFor?: string }>();
</script>
