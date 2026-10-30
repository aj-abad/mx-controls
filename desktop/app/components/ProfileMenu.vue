<template>
  <PopoverRoot v-model:open="open">
    <PopoverTrigger
      :aria-label="`Profile: ${active?.name ?? ''}`"
      class="flex h-8 items-center gap-2 rounded-lg bg-panel pr-2.5 pl-3 text-sm ring-1 ring-line ring-inset transition-colors hover:bg-fill data-[state=open]:bg-fill">
      <span class="text-ink-3">Profile</span>
      <span class="font-medium">{{ active?.name }}</span>
      <IconChevronDown class="size-3.5 text-ink-3" />
    </PopoverTrigger>
    <UiPopoverContent :open="open" align="end" class="flex w-72 flex-col p-2">
      <p class="px-3 pt-2 pb-1.5 text-xs text-ink-3">Each profile keeps its own buttons, gestures and thumb wheel.</p>
      <ul aria-label="Profiles" class="flex flex-col">
        <li v-for="profile in profiles" :key="profile.id" class="group flex h-10 items-center gap-1 rounded-lg pr-1 hover:bg-fill">
          <InlineRename
            v-if="editing === profile.id"
            class="pl-1.5"
            :value="profile.name"
            :label="`Rename ${profile.name}`"
            :maxlength="40"
            @save="(name) => rename(profile.id, name)"
            @cancel="editing = null" />
          <template v-else>
            <button
              type="button"
              class="flex h-full min-w-0 flex-1 items-center gap-2 pl-3 text-left text-sm"
              :aria-current="profile.id === state?.activeProfile"
              @click="choose(profile.id)">
              <span class="min-w-0 flex-1 truncate" :class="profile.id === state?.activeProfile && 'font-medium'">{{ profile.name }}</span>
              <IconCheck v-if="profile.id === state?.activeProfile" class="size-4 shrink-0" />
            </button>
            <button
              type="button"
              class="h-7 rounded-full px-2.5 text-xs text-ink-3 opacity-0 group-hover:opacity-100 hover:bg-fill-strong hover:text-ink focus-visible:opacity-100"
              @click="editing = profile.id">
              Rename
            </button>
            <button
              v-if="profiles.length > 1"
              type="button"
              class="h-7 rounded-full px-2.5 text-xs text-ink-3 opacity-0 group-hover:opacity-100 hover:bg-fill-strong hover:text-warn focus-visible:opacity-100"
              @click="remove(profile)">
              Delete
            </button>
          </template>
        </li>
      </ul>
      <div class="mt-1 flex gap-1 border-t border-line pt-2">
        <UiButton variant="ghost" size="sm" @click="create(false)">New profile</UiButton>
        <UiButton variant="ghost" size="sm" :aria-label="`Duplicate ${active?.name}`" @click="create(true)">Duplicate</UiButton>
      </div>
    </UiPopoverContent>
  </PopoverRoot>
</template>

<script setup lang="ts">
import { PopoverRoot, PopoverTrigger } from "reka-ui";
import type { ProfileSummary } from "~/composables/useAgent";

const { state, attempt } = useAgent();

const open = ref(false);
const editing = ref<string | null>(null);
watch(open, (isOpen) => !isOpen && (editing.value = null));

const profiles = computed(() => state.value?.profiles ?? []);
const active = computed(() => profiles.value.find((p) => p.id === state.value?.activeProfile));

async function choose(id: string) {
  if (id === state.value?.activeProfile) return;
  if (await attempt("switchProfile", { id }, "Couldn't switch profiles"))
    showToast(`Using ${profiles.value.find((p) => p.id === id)?.name}`, "neutral", { key: "profile" });
}

// A new profile is active straight away and named in place, the way a new folder is.
async function create(copy: boolean) {
  const taken = new Set(profiles.value.map((p) => p.name));
  const base = copy ? `${active.value?.name} copy` : "New profile";
  let name = base;
  for (let n = 2; taken.has(name); n++) name = `${base} ${n}`;
  const params = copy ? { name, copyFrom: state.value?.activeProfile } : { name };
  if (await attempt("createProfile", params, "Couldn't add a profile")) editing.value = state.value?.activeProfile ?? null;
}

async function rename(id: string, name: string) {
  editing.value = null;
  await attempt("renameProfile", { id, name }, "Couldn't rename the profile");
}

async function remove(profile: ProfileSummary) {
  if (!(await attempt("deleteProfile", { id: profile.id }, "Couldn't delete the profile"))) return;
  showToast(`Deleted ${profile.name}`, "neutral", { key: "profile" });
}
</script>
