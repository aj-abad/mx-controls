<template>
  <div class="flex flex-col gap-4">
    <form class="flex flex-col gap-1.5" @submit.prevent="saveName">
      <label for="profile-name" class="text-xs font-medium text-ink-3">Name</label>
      <input
        id="profile-name"
        ref="input"
        v-model="name"
        maxlength="40"
        class="h-9 rounded-lg bg-field px-3 text-sm ring-1 ring-line ring-inset outline-none! focus:ring-ink"
        @blur="saveName" />
    </form>

    <fieldset class="flex flex-col gap-1.5">
      <legend class="mb-1.5 text-xs font-medium text-ink-3">Icon</legend>
      <div class="grid grid-cols-7 gap-1">
        <button
          v-for="choice in CHOICES"
          :key="choice ?? ''"
          type="button"
          :aria-label="choice ? choice.replaceAll('-', ' ') : 'Initial'"
          :aria-pressed="profile.icon === choice"
          class="grid size-9 place-items-center rounded-lg transition-colors duration-150"
          :class="profile.icon === choice ? 'bg-ink text-on-ink' : 'text-ink-2 hover:bg-fill hover:text-ink'"
          @click="setIcon(choice)">
          <ProfileAvatar :profile="{ name: name || profile.name, icon: choice }" class="size-5" />
        </button>
      </div>
    </fieldset>
  </div>
</template>

<script setup lang="ts">
import type { ProfileSummary } from "~/composables/useAgent";

const { profile } = defineProps<{ profile: ProfileSummary }>();
const { attempt } = useAgent();

// The profile's initial comes first, for a profile that wants no icon.
const CHOICES = [null, ...Object.keys(PROFILE_ICONS)];

const name = ref(profile.name);
const input = useTemplateRef("input");
onMounted(() => input.value?.select());

// Saved as it closes too, since a popover can close without the field losing focus first.
async function saveName() {
  const next = name.value.trim();
  if (!next || next === profile.name) return;
  await attempt("renameProfile", { id: profile.id, name: next }, "Couldn't rename the profile");
}
onBeforeUnmount(saveName);

function setIcon(icon: string | null) {
  if (icon !== profile.icon) void attempt("setProfileIcon", { id: profile.id, icon }, "Couldn't change the icon");
}
</script>
