<template>
  <section>
    <h3 v-if="title" class="type-label mb-2 pl-4">{{ title }}</h3>
    <slot />
    <p v-if="note" class="mt-2 pl-4 text-sm text-ink-3">{{ note }}</p>
  </section>
</template>

<script setup lang="ts">
defineProps<{
  title?: string;
  /** Helper text under the card, indented to the row labels. */
  note?: string;
}>();
</script>
