<template>
  <img v-if="programImage" :src="programImage" alt="" aria-hidden="true" class="object-contain" draggable="false" />
  <component :is="icon" v-else-if="icon" aria-hidden="true" />
  <span v-else aria-hidden="true" class="grid place-items-center text-[13px] leading-none font-semibold">{{ initial }}</span>
</template>

<script setup lang="ts">
import type { ProfileSummary } from "~/composables/useAgent";

/** A profile that follows programs wears its first program's icon; any other, its chosen icon or its initial. */
const { profile } = defineProps<{ profile: Pick<ProfileSummary, "name" | "icon"> & { programs?: string[] } }>();

const programImage = computed(() => programIcon(profile.programs?.[0]));
const icon = computed(() => (profile.icon ? PROFILE_ICONS[profile.icon] : undefined));
const initial = computed(() => [...profile.name.trim()][0]?.toUpperCase() ?? "?");
</script>
