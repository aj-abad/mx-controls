<template>
  <div class="flex h-full min-w-0 gap-3">
    <section aria-label="Rose menus" class="flex w-44 shrink-0 flex-col rounded-2xl bg-panel p-2">
      <div class="flex items-center justify-between px-2 py-2">
        <h2 class="text-xs font-semibold text-ink-2">Menus</h2>
        <span class="text-[11px] text-ink-3">{{ cards.length }}</span>
      </div>
      <UiScrollArea class="min-h-0 flex-1">
        <button
          v-for="card in cards"
          :key="card.menu.id"
          type="button"
          :aria-current="card.menu.id === menu?.id ? 'true' : undefined"
          class="mb-1 flex w-full items-start gap-2 rounded-xl px-2 py-2.5 text-left"
          :class="card.menu.id === menu?.id ? 'bg-field ring-1 ring-line ring-inset' : 'hover:bg-fill/60'"
          @click="openId = card.menu.id">
          <svg width="28" height="28" viewBox="-14 -14 28 28" class="mt-0.5 shrink-0" aria-hidden="true">
            <circle r="13" class="fill-canvas stroke-line" />
            <line
              v-for="i in card.menu.items.length"
              :key="i"
              :x2="12 * Math.sin(((i - 0.5) * 2 * Math.PI) / card.menu.items.length)"
              :y2="-12 * Math.cos(((i - 0.5) * 2 * Math.PI) / card.menu.items.length)"
              class="stroke-line" />
            <circle r="4" class="fill-canvas stroke-accent" stroke-width="1.5" />
          </svg>
          <span class="flex min-w-0 flex-1 flex-col gap-0.5">
            <span class="truncate text-[13px] font-medium">{{ card.menu.name }}</span>
            <span class="text-[11px] text-ink-3">{{ card.tag }}{{ card.used ? ` · ${card.used}` : "" }}</span>
          </span>
        </button>
        <p v-if="!cards.length" class="px-2 py-3 text-xs leading-relaxed text-ink-3">
          No rose menus yet. A rose shows its options around the pointer while a button is held.
        </p>
      </UiScrollArea>
      <button
        type="button"
        class="mt-2 flex h-9 shrink-0 items-center justify-center gap-1.5 rounded-xl border border-dashed border-fill-strong text-xs font-medium text-ink-2 hover:bg-fill"
        @click="createNew">
        <PhPlus class="size-3.5" /> New rose menu
      </button>
    </section>

    <section
      v-if="menu && draft"
      :aria-label="menu.name"
      class="flex min-w-0 flex-1 flex-col overflow-hidden rounded-2xl border border-line bg-panel">
      <header class="flex shrink-0 items-center gap-2 border-b border-line px-5 py-3">
        <div class="min-w-0 flex-1">
          <input
            :value="menu.name"
            aria-label="Rose menu name"
            maxlength="60"
            class="-ml-1.5 w-full rounded-md bg-transparent px-1.5 text-base font-semibold outline-none hover:ring-1 hover:ring-line focus:ring-1 focus:ring-accent"
            @change="rename" />
          <p class="mt-0.5 truncate text-[11px] text-ink-3">
            {{ countsText }} · {{ uses.length }} {{ uses.length === 1 ? "use" : "uses" }} ·
            {{ unfinished ? "Unfinished changes" : "Changes save automatically" }}
          </p>
        </div>
        <UiButton variant="ghost" size="sm" class="px-2.5! text-xs!" :disabled="!!unfinished" @click="duplicate">
          Duplicate
        </UiButton>
        <UiButton variant="ghost" size="sm" class="px-2.5! text-xs!" @click="askDelete">Delete</UiButton>
      </header>

      <div class="rose-workspace min-h-0 flex-1 overflow-hidden">
        <UiScrollArea content-class="px-5 py-4">
          <RoseEditor v-model="draft.items" :name="draft.name" :live-buttons="liveButtons" />
        </UiScrollArea>
        <UiScrollArea class="border-l border-line" content-class="flex flex-col gap-3 px-5 py-4">
          <h3 class="text-xs font-medium text-ink-2">How it works</h3>
          <ul class="flex list-disc flex-col gap-1.5 pl-4 text-xs leading-relaxed text-ink-3">
            <li>Hold the button: the rose opens around the pointer, which stays still while a ghost cursor moves.</li>
            <li>Move toward an option and let go to run it. Letting go in the middle runs nothing.</li>
            <li>Push on to the rim of a folder to open it. Folders can hold folders, as deep as you like.</li>
            <li>In a folder, rest in the middle for a moment to go back.</li>
          </ul>
          <details class="border-t border-line pt-3" open>
            <summary class="cursor-pointer text-xs font-medium text-ink-2">
              {{ uses.length ? `Shown by ${uses.length} ${uses.length === 1 ? "button" : "buttons"}` : "How to use this menu" }}
            </summary>
            <ul v-if="uses.length" class="mt-2 flex flex-col gap-1 text-xs text-ink-3">
              <li v-for="use in uses" :key="use">{{ use }}</li>
            </ul>
            <p class="mt-2 text-xs leading-relaxed text-ink-3">
              {{
                uses.length
                  ? "Editing this shared menu updates every button listed above."
                  : "In Profiles, select a button and choose this menu under Rose menu while held."
              }}
            </p>
            <button type="button" class="mt-2 text-xs font-medium text-ink hover:underline" @click="goTo('profiles')">
              Go to Profiles →
            </button>
          </details>
        </UiScrollArea>
      </div>
      <footer
        class="flex h-12 shrink-0 items-center border-t border-line px-5 text-xs"
        :class="unfinished ? 'text-warn' : 'text-ink-3'"
        role="status">
        <p class="line-clamp-2">
          {{
            unfinished
              ? `Choose an action for ${unfinished} to save. Buttons keep the last saved version until then.`
              : "Changes update every button that shows this menu."
          }}
        </p>
      </footer>
      <DialogRoot v-model:open="confirming"
        ><DialogPortal
          ><DialogOverlay class="fixed inset-0 z-50 bg-black/25" /><DialogContent
            class="fixed top-1/2 left-1/2 z-50 w-90 -translate-x-1/2 -translate-y-1/2 rounded-2xl bg-field p-5 shadow-xl ring-1 ring-line"
            ><DialogTitle class="text-base font-semibold">Delete “{{ menu.name }}”?</DialogTitle
            ><DialogDescription class="mt-2 text-sm leading-relaxed text-ink-3"
              >{{ uses.join(" and ") }}
              {{ uses.length === 1 ? "keeps its quick press and loses its rose" : "keep their quick press and lose their rose" }}.</DialogDescription
            >
            <div class="mt-5 flex justify-end gap-2">
              <UiButton variant="secondary" size="sm" @click="confirming = false">Cancel</UiButton
              ><UiButton size="sm" @click="deleteMenu">Delete menu</UiButton>
            </div></DialogContent
          ></DialogPortal
        ></DialogRoot
      >
    </section>
    <section
      v-else
      class="flex min-w-0 flex-1 flex-col items-center justify-center gap-3 rounded-2xl border border-dashed border-line bg-panel/60 p-8 text-center">
      <p class="max-w-80 text-sm leading-relaxed text-ink-3">
        A rose menu puts up to eight actions around the pointer while you hold a button, and folders inside it hold more.
      </p>
      <UiButton size="sm" @click="createNew">New rose menu</UiButton>
    </section>
  </div>
</template>

<script setup lang="ts">
import { PhPlus } from "@phosphor-icons/vue";
import { DialogContent, DialogDescription, DialogOverlay, DialogPortal, DialogRoot, DialogTitle } from "reka-ui";
import type { RoseItem, RoseMenu } from "~/composables/useAgent";

const { openRose: openId, goTo } = usePage();
const { state } = useAgent();
const roses = useRoseMenus();
const menu = computed(() => roses.find(openId.value) ?? roses.menus.value[0]);
const uses = computed(() => (menu.value ? roses.usedBy(menu.value.id) : []));
const cards = computed(() =>
  roses.menus.value.map((m) => {
    const count = roses.usedBy(m.id).length;
    return { menu: m, tag: `${m.items.length} options`, used: count ? `${count} ${count === 1 ? "use" : "uses"}` : "" };
  }),
);

// The buttons showing this menu in the profile running, whose rose lights up the wheel as it's used.
const liveButtons = computed(() =>
  (state.value?.roseUses ?? [])
    .filter((use) => use.menu === menu.value?.id && use.profile === state.value?.activeProfile)
    .map((use) => use.button),
);

const draft = ref<RoseMenu | null>(null);
const confirming = ref(false);
const copy = (m: RoseMenu): RoseMenu => JSON.parse(JSON.stringify(m));
function resetDraft() {
  draft.value = menu.value ? copy(menu.value) : null;
}
watch(() => canonical(menu.value ?? null), resetDraft, { immediate: true });
watch(
  () => menu.value?.id,
  () => (confirming.value = false),
);

const unfinished = computed(() => (draft.value ? unfinishedRoseItem(draft.value.items) : null));
watch(
  () => canonical(draft.value),
  async (now) => {
    const d = draft.value;
    if (!d || unfinished.value || now === canonical(menu.value ?? null)) return;
    if (!(await roses.change(d))) resetDraft();
  },
);

const countsText = computed(() => {
  const folders = (items: RoseItem[]): number => items.reduce((n, i) => n + (i.items ? 1 + folders(i.items) : 0), 0);
  const options = (items: RoseItem[]): number => items.reduce((n, i) => n + (i.items ? options(i.items) : 1), 0);
  const d = draft.value!;
  const f = folders(d.items);
  return `${options(d.items)} options${f ? ` · ${f} ${f === 1 ? "folder" : "folders"}` : ""}`;
});

function rename(event: Event) {
  const input = event.target as HTMLInputElement;
  const name = input.value.trim();
  if (name) draft.value = { ...draft.value!, name };
  else input.value = draft.value!.name;
}
async function createNew() {
  const id = await roses.create(roses.blank());
  if (id) openId.value = id;
}
async function duplicate() {
  const { id: _, ...rest } = draft.value!;
  const id = await roses.create({ ...rest, name: `${rest.name} copy` });
  if (id) openId.value = id;
}
function askDelete() {
  if (uses.value.length) confirming.value = true;
  else void deleteMenu();
}
async function deleteMenu() {
  if (await roses.discard(menu.value!)) openId.value = null;
}
</script>

<style scoped>
.rose-workspace {
  display: grid;
  grid-template-columns: minmax(0, 1.25fr) minmax(0, 1fr);
  grid-template-rows: minmax(0, 1fr);
}
</style>
