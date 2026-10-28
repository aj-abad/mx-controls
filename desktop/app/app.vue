<template>
  <NuxtLayout>
    <NuxtPage />
  </NuxtLayout>
  <DialogBackdrop />
  <SharedToasters />
  <GlassFilters />
</template>

<script setup lang="ts">
import DialogBackdrop from "@arcon.mobi/sine/components/UI/DialogBackdrop.vue";
import GlassFilters from "@arcon.mobi/sine/components/UI/GlassFilters.vue";
import SharedToasters from "@arcon.mobi/sine/components/Shared/Toasters.vue";
import { useOutsideClickSelection } from "@arcon.mobi/sine/composables/useOutsideClickSelection";

useOutsideClickSelection();
</script>
