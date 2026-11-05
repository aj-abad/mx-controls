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

    <fieldset v-if="state?.followsPrograms" class="flex flex-col gap-2">
      <legend class="mb-1 text-xs font-medium text-ink-3">Programs</legend>
      <p class="text-xs leading-relaxed text-ink-3">
        {{
          programs.length
            ? "Runs while one of these is in front. It can't be chosen by hand or by a button."
            : "Choose programs to run this profile whenever they're in front."
        }}
      </p>
      <ul v-if="programs.length" class="flex flex-wrap gap-1.5" aria-label="Programs that run this profile">
        <li
          v-for="program in programs"
          :key="program"
          class="flex h-7 items-center gap-1 rounded-full bg-fill pr-1 pl-2.5 text-xs font-medium">
          {{ program }}
          <button
            type="button"
            :aria-label="`Remove ${program}`"
            class="grid size-5 place-items-center rounded-full text-ink-3 hover:bg-fill-strong hover:text-ink"
            @click="setPrograms(programs.filter((p) => p !== program))">
            <PhX class="size-3" />
          </button>
        </li>
      </ul>
      <p v-if="lastChosen" class="text-xs leading-relaxed text-warn">
        Keep one profile without programs; it runs for every other program. Add another profile to give this one
        programs.
      </p>
      <template v-else>
        <form class="flex gap-1.5" @submit.prevent="addTyped">
          <input
            v-model="typed"
            aria-label="Program file name"
            :placeholder="isMac ? 'adobe photoshop 2026' : 'photoshop.exe'"
            class="h-8 min-w-0 flex-1 rounded-lg bg-field px-3 text-xs ring-1 ring-line ring-inset outline-none! placeholder:text-ink-3 focus:ring-ink" />
          <UiButton size="sm" type="submit" :disabled="!typed.trim()">Add</UiButton>
        </form>
        <button
          type="button"
          class="flex h-8 items-center gap-1.5 self-start rounded-lg px-1 text-xs font-medium text-ink-2 hover:text-ink"
          :aria-expanded="browsing"
          @click="toggleBrowse">
          <PhCaretRight class="size-3.5 transition-transform" :class="browsing && 'rotate-90'" />
          Pick from open programs
        </button>
        <UiScrollArea v-if="browsing" class="h-40 rounded-lg ring-1 ring-line ring-inset" content-class="p-1">
          <p v-if="!running" class="px-2 py-3 text-xs text-ink-3">Looking for programs…</p>
          <p v-else-if="!running.length" class="px-2 py-3 text-xs text-ink-3">No other programs have a window open.</p>
          <button
            v-for="program in running ?? []"
            :key="program.id"
            type="button"
            :disabled="!!takenBy(program.id) || programs.includes(program.id)"
            class="flex h-8 w-full items-center gap-2 rounded-md px-2 text-left text-xs hover:bg-fill disabled:opacity-50 disabled:hover:bg-transparent"
            @click="setPrograms([...programs, program.id])">
            <span class="min-w-0 flex-1 truncate font-medium">{{ program.name }}</span>
            <span class="shrink-0 text-ink-3">{{
              programs.includes(program.id) ? "Added" : takenBy(program.id) ? `In ${takenBy(program.id)}` : program.id
            }}</span>
          </button>
        </UiScrollArea>
      </template>
    </fieldset>
  </div>
</template>

<script setup lang="ts">
import { PhCaretRight, PhX } from "@phosphor-icons/vue";
import type { ProfileSummary, ProgramInfo } from "~/composables/useAgent";

const { profile } = defineProps<{ profile: ProfileSummary }>();
const { state, attempt, call } = useAgent();

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

const programs = computed(() => profile.programs);
/** The one profile left without programs can't take any, since it runs for every program without a profile. */
const lastChosen = computed(
  () => !programs.value.length && (state.value?.profiles.filter((p) => !p.programs.length).length ?? 0) <= 1,
);
const takenBy = (id: string) =>
  state.value?.profiles.find((p) => p.id !== profile.id && p.programs.includes(id))?.name;

function setPrograms(next: string[]) {
  void attempt("setProfilePrograms", { id: profile.id, programs: next }, "Couldn't change the programs");
}

const isMac = navigator.userAgent.includes("Mac");
const typed = ref("");
function addTyped() {
  // People type "Photoshop" as often as "photoshop.exe"; the executable's file name is what's matched.
  let id = typed.value.trim().toLowerCase().split(/[\\/]/).pop() ?? "";
  if (!id) return;
  if (!isMac && !id.includes(".")) id += ".exe";
  typed.value = "";
  if (!programs.value.includes(id)) setPrograms([...programs.value, id]);
}

const browsing = ref(false);
const running = ref<ProgramInfo[] | null>(null);
async function toggleBrowse() {
  browsing.value = !browsing.value;
  if (!browsing.value) return;
  running.value = null;
  try {
    running.value = await call<ProgramInfo[]>("listPrograms");
  } catch (error) {
    running.value = [];
    reportFailure(`Couldn't list the open programs: ${String(error)}`);
  }
}
</script>
