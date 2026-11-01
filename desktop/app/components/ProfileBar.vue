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
        <UiTooltip :text="`${profile.name} · double-click to rename`">
          <button
            :ref="(el) => (chips[profile.id] = (el as HTMLElement | null) ?? undefined)"
            type="button"
            :aria-label="profile.name"
            :aria-pressed="profile.id === activeId"
            class="flex h-8 shrink-0 items-center gap-1.5 rounded-lg transition-colors duration-150"
            :class="
              profile.id === activeId
                ? 'max-w-40 bg-accent/12 pr-3.5 pl-2.5 text-ink ring-1 ring-accent/25 ring-inset'
                : 'max-w-40 pr-3.5 pl-2.5 text-ink-2 hover:bg-fill hover:text-ink data-[state=open]:bg-fill'
            "
            @click="choose(profile)"
            @dblclick="editing = profile.id"
            @keydown.f2.prevent="editing = profile.id">
            <ProfileAvatar :profile="profile" class="size-4.5" />
            <span class="truncate text-sm font-medium">{{ profile.name }}</span>
          </button>
        </UiTooltip>
      </UiContextMenu>
      <UiPopoverContent :open="editing === profile.id" align="start" class="w-76 p-4">
        <ProfileEditor :profile="profile" />
      </UiPopoverContent>
    </PopoverRoot>

    <UiTooltip text="Edit active profile">
      <button
        v-if="active"
        type="button"
        aria-label="Edit active profile"
        class="grid size-8 shrink-0 place-items-center rounded-full text-ink-3 hover:bg-fill hover:text-ink"
        @click="editing = active.id">
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
import { PhCheckCircle, PhCopy, PhPencilSimple, PhPlus, PhTrash } from "@phosphor-icons/vue";
import { PopoverAnchor, PopoverRoot } from "reka-ui";
import type { ProfileSummary } from "~/composables/useAgent";
import type { MenuEntry } from "~/utils/menu";

const { state, attempt } = useAgent();

const profiles = computed(() => state.value?.profiles ?? []);
const activeId = computed(() => state.value?.activeProfile);
const active = computed(() => profiles.value.find((p) => p.id === activeId.value));

const chips = reactive<Record<string, HTMLElement | undefined>>({});

/** The profile whose name and icon are open for editing. */
const editing = ref<string | null>(null);

async function choose(profile: ProfileSummary) {
  if (profile.id === activeId.value) return;
  await attempt("switchProfile", { id: profile.id }, "Couldn't switch profiles");
}

// A new profile is active straight away and opens for naming, the way a new folder does.
async function create(source?: ProfileSummary) {
  const taken = new Set(profiles.value.map((p) => p.name));
  const base = source ? `${source.name} copy` : "New profile";
  let name = base;
  for (let n = 2; taken.has(name); n++) name = `${base} ${n}`;
  const params = source ? { name, copyFrom: source.id } : { name, icon: unusedProfileIcon(profiles.value) };
  if (await attempt("createProfile", params, "Couldn't add a profile")) editing.value = activeId.value ?? null;
}

async function remove(profile: ProfileSummary) {
  await attempt("deleteProfile", { id: profile.id }, "Couldn't delete the profile");
}

function menuFor(profile: ProfileSummary): MenuEntry[] {
  return [
    { label: "Use this profile", icon: PhCheckCircle, disabled: profile.id === activeId.value, select: () => choose(profile) },
    { label: "Name and icon…", icon: PhPencilSimple, select: () => (editing.value = profile.id) },
    { label: "Duplicate", icon: PhCopy, select: () => create(profile) },
    "separator",
    { label: "Delete", icon: PhTrash, danger: true, disabled: profiles.value.length <= 1, select: () => remove(profile) },
  ];
}

const addMenu = computed<MenuEntry[]>(() => [
  { label: "New profile", icon: PhPlus, select: () => create() },
  ...(active.value ? [{ label: `Duplicate ${active.value.name}`, icon: PhCopy, select: () => create(active.value) }] : []),
]);
</script>
