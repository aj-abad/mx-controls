<template>
  <Pill
    :as="NuxtLink"
    :to="to"
    :focus-ring="true"
    :radius="RADIUS.pill"
    :surface-class="active ? 'bg-fill-2' : 'group-hover/nav:bg-fill-2'"
    :class="
      cn(
        'group/nav relative flex w-full select-none items-center gap-2 px-3.5 py-2 text-ink-3',
        active && 'text-ink',
      )
    "
    :aria-current="active ? 'page' : undefined">
    <component :is="icon" :size="18" aria-hidden="true" />
    <span class="min-w-0 truncate font-medium">{{ label }}</span>
  </Pill>
</template>

<script setup lang="ts">
import type { Component } from "vue";
import Pill from "@arcon.mobi/sine/components/UI/Pill.vue";
import { RADIUS } from "@arcon.mobi/sine/utils/controlSquircle";
import { cn } from "@arcon.mobi/sine/utils/cn";
import { NuxtLink } from "#components";

const { to } = defineProps<{ to: string; icon: Component; label: string }>();

const route = useRoute();
const active = computed(() => route.path === to);
</script>
