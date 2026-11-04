<template>
  <div role="toolbar" aria-label="Profiles" class="flex min-w-0 items-center gap-1">
    <PopoverRoot
      v-for="profile in profiles"
      :key="profile.id"
      :open="editing === profile.id"
      @update:open="(open) => !open && editing === profile.id && (editing = null)">
      <!-- Anchored by hand: inside the context menu and tooltip, a nested anchor would anchor those instead. -->
      <PopoverAnchor as="template" :reference="chips[profile.id]" />
      <UiContextMenu :items="menuFor(profile)">
        <UiTooltip :text="tooltipFor(profile)">
          <button
            :ref="(el) => (chips[profile.id] = (el as HTMLElement | null) ?? undefined)"
            type="button"
            :aria-label="labelFor(profile)"
            :aria-pressed="profile.id === shownId"
            class="flex h-8 max-w-44 shrink-0 items-center gap-1.5 rounded-lg pr-3 pl-2.5 transition-colors duration-150"
            :class="
              profile.id === shownId
                ? 'bg-accent/12 text-ink ring-1 ring-accent/25 ring-inset'
                : 'text-ink-2 hover:bg-fill hover:text-ink data-[state=open]:bg-fill'
            "
            @click="choose(profile)"
            @dblclick="editing = profile.id"
            @keydown.f2.prevent="editing = profile.id">
            <ProfileAvatar :profile="profile" class="size-4.5" />
            <span class="truncate text-sm font-medium">{{ profile.name }}</span>
            <PhAppWindow v-if="profile.programs.length" class="size-3.5 shrink-0 text-ink-3" />
            <span
              v-if="profile.id === state?.selectedProfile && profile.id !== shownId"
              class="size-1.5 shrink-0 rounded-full bg-accent"
              aria-hidden="true" />
          </button>
        </UiTooltip>
      </UiContextMenu>
      <UiPopoverContent :open="editing === profile.id" align="start" class="w-76 p-4">
        <ProfileEditor :profile="profile" />
      </UiPopoverContent>
    </PopoverRoot>

    <UiTooltip text="Edit profile">
      <button
        v-if="shown"
        type="button"
        aria-label="Edit profile"
        class="grid size-8 shrink-0 place-items-center rounded-full text-ink-3 hover:bg-fill hover:text-ink"
        @click="editing = shown.id">
        <PhPencilSimple class="size-3.5" />
      </button>
    </UiTooltip>

    <UiContextMenu :items="addMenu">
      <UiTooltip text="New profile">
        <button
          type="button"
          aria-label="New profile"
          class="grid size-8 shrink-0 place-items-center rounded-full text-ink-3 ring-1 ring-line ring-inset transition-colors duration-150 ring-dashed hover:bg-fill hover:text-ink"
          @click="create()">
          <PhPlus class="size-4" />
        </button>
      </UiTooltip>
    </UiContextMenu>
  </div>
</template>

<script setup lang="ts">
import { PhAppWindow, PhCheckCircle, PhCopy, PhPencilSimple, PhPlus, PhTrash } from "@phosphor-icons/vue";
import { PopoverAnchor, PopoverRoot } from "reka-ui";
import type { ProfileSummary } from "~/composables/useAgent";
import type { MenuEntry } from "~/utils/menu";

const { state, attempt } = useAgent();
const view = useProfileView();

const profiles = computed(() => state.value?.profiles ?? []);
/** The profile the page shows and edits. */
const shownId = computed(() => view.profile.value?.id);
const shown = computed(() => view.profile.value);
const switchable = (profile: ProfileSummary) => profile.programs.length === 0;

const chips = reactive<Record<string, HTMLElement | undefined>>({});

/** The profile whose name, icon and programs are open for editing. */
const editing = ref<string | null>(null);

const programList = (profile: ProfileSummary) => profile.programs.join(", ");

function tooltipFor(profile: ProfileSummary) {
  if (!switchable(profile)) return `${profile.name} · runs in ${programList(profile)}`;
  if (profile.id === state.value?.selectedProfile && profile.id !== shownId.value) return `${profile.name} · in use everywhere else`;
  return `${profile.name} · double-click to rename`;
}

function labelFor(profile: ProfileSummary) {
  if (!switchable(profile)) return `${profile.name}, runs in ${programList(profile)}`;
  return profile.id === state.value?.selectedProfile ? `${profile.name}, in use` : profile.name;
}

// A profile that follows programs is only shown for editing; any other one is also put to use.
async function choose(profile: ProfileSummary) {
  view.view(profile.id);
  if (!switchable(profile) || profile.id === state.value?.selectedProfile) return;
  await attempt("switchProfile", { id: profile.id }, "Couldn't switch profiles");
}

// A new profile is in use straight away and opens for naming, the way a new folder does.
async function create(source?: ProfileSummary) {
  const taken = new Set(profiles.value.map((p) => p.name));
  const base = source ? `${source.name} copy` : "New profile";
  let name = base;
  for (let n = 2; taken.has(name); n++) name = `${base} ${n}`;
  const params = source ? { name, copyFrom: source.id } : { name, icon: unusedProfileIcon(profiles.value) };
  if (!(await attempt("createProfile", params, "Couldn't add a profile"))) return;
  const created = state.value?.selectedProfile;
  if (created) view.view(created);
  editing.value = created ?? null;
}

async function remove(profile: ProfileSummary) {
  await attempt("deleteProfile", { id: profile.id }, "Couldn't delete the profile");
}

/** One profile always stays without programs, to run everywhere else. */
const lastSwitchable = (profile: ProfileSummary) =>
  switchable(profile) && profiles.value.filter(switchable).length <= 1;

function menuFor(profile: ProfileSummary): MenuEntry[] {
  return [
    switchable(profile)
      ? {
          label: "Use this profile",
          icon: PhCheckCircle,
          disabled: profile.id === state.value?.selectedProfile,
          select: () => choose(profile),
        }
      : { label: "Runs with its programs", icon: PhAppWindow, disabled: true, select: () => {} },
    { label: "Name, icon and programs…", icon: PhPencilSimple, select: () => (editing.value = profile.id) },
    { label: "Duplicate", icon: PhCopy, select: () => create(profile) },
    "separator",
    {
      label: "Delete",
      icon: PhTrash,
      danger: true,
      disabled: profiles.value.length <= 1 || lastSwitchable(profile),
      select: () => remove(profile),
    },
  ];
}

const addMenu = computed<MenuEntry[]>(() => [
  { label: "New profile", icon: PhPlus, select: () => create() },
  ...(shown.value ? [{ label: `Duplicate ${shown.value.name}`, icon: PhCopy, select: () => create(shown.value!) }] : []),
]);
</script>
