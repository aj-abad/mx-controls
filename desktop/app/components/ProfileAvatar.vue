<template>
  <component :is="icon" v-if="icon" aria-hidden="true" />
  <span v-else aria-hidden="true" class="grid place-items-center text-[13px] leading-none font-semibold">{{ initial }}</span>
</template>

<script setup lang="ts">
import type { ProfileSummary } from "~/composables/useAgent";

const { profile } = defineProps<{ profile: Pick<ProfileSummary, "name" | "icon"> }>();

const icon = computed(() => (profile.icon ? PROFILE_ICONS[profile.icon] : undefined));
const initial = computed(() => [...profile.name.trim()][0]?.toUpperCase() ?? "?");
</script>
