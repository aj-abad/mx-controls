<template>
  <div class="action-editor flex flex-col gap-2.5">
    <ActionPicker
      :model-value="choice"
      :label="label"
      :allow-hold="allowHold"
      :default-label="defaultLabel"
      :layers="layers"
      :profiles="profiles"
      :current-name="modelValue?.name ?? undefined"
      @update:model-value="choose"
      @record="capture?.start()"
    />
    <div class="flex h-10 shrink-0 items-center gap-3">
      <AppPicker
        v-if="modelValue?.type === 'open' && openKind(modelValue.open) === 'app'"
        class="min-w-0 flex-1"
        :id="(modelValue.open ?? '').slice(APP_PREFIX.length)"
        :name="modelValue.name"
        :label="label"
        :auto-open="justChose"
        @pick="(app) => update({ open: APP_PREFIX + app.id, name: app.name })" />
      <input
        v-else-if="modelValue?.type === 'open'"
        ref="target"
        :value="openKind(modelValue.open) === 'url' && !openComplete(modelValue.open) ? '' : (modelValue.open ?? '')"
        :placeholder="openKind(modelValue.open) === 'url' ? 'https://example.com' : IS_MAC ? '~/Documents/Notes.txt' : 'C:\Users\Me\Documents'"
        :aria-label="`${label}: ${openKind(modelValue.open) === 'url' ? 'web address' : 'file or folder path'}`"
        spellcheck="false"
        class="h-10 min-w-0 flex-1 rounded-lg bg-field px-3 text-sm ring-1 ring-line ring-inset outline-none! placeholder:text-ink-3 focus:ring-ink"
        @change="(event) => setTarget((event.target as HTMLInputElement).value)" />
      <ShortcutCapture
        v-if="modelValue && (choice === 'shortcut' || choice === 'hold')"
        ref="capture"
        class="min-w-0 flex-1"
        :model-value="modelValue.keys"
        :label="label"
        @update:model-value="(keys) => update({ keys })"
      />
      <button
        v-if="modelValue?.type === 'macro' && modelValue.macro"
        type="button"
        class="mr-auto text-xs text-ink-3 hover:text-ink"
        @click="goTo('macros', modelValue.macro)"
      >
        Edit shared macro ›
      </button>
      <button v-if="macroKeys" type="button" class="text-xs font-medium text-accent hover:underline" @click="useAsShortcut">
        Use as shortcut
      </button>
    </div>
    <div class="flex h-8 shrink-0 items-center text-xs text-ink-3">
      <UiCheckbox
        v-if="canRepeat"
        :model-value="!!modelValue?.repeat"
        :label="repeatLabel"
        class="text-xs! text-ink-2"
        @update:model-value="(repeat) => update({ repeat })"
      />
      <span v-else class="line-clamp-2">{{ actionHint }}</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { ButtonAction } from "~/composables/useAgent";

const {
  modelValue,
  label,
  allowHold = true,
  allowRepeat = true,
  repeatLabel = "Repeat while held",
  defaultLabel,
  layers = "none",
  profiles = false,
} = defineProps<{
  /** Null for the button's default, where `defaultLabel` is given. */
  modelValue: ButtonAction | null;
  label: string;
  /** A key only makes sense where the action lasts as long as the press. */
  allowHold?: boolean;
  /** Repeating needs a press to repeat through. */
  allowRepeat?: boolean;
  /** A gesture direction repeats with movement rather than with time. */
  repeatLabel?: string;
  /** Offers the button's default, under this name; picking it emits `default`. */
  defaultLabel?: string;
  /** Which second-layer actions fit here; holding the layer needs a press that lasts. */
  layers?: "hold" | "toggle" | "none";
  /** Offers switching profiles. */
  profiles?: boolean;
}>();
const emit = defineEmits<{ "update:modelValue": [action: ButtonAction]; default: [] }>();

const capture = useTemplateRef("capture");

/**
 * Set once a custom shortcut is chosen, so recording keys that happen to match a preset, such as Ctrl + C,
 * doesn't swap the key field for "Copy" mid-edit.
 */
const custom = ref(false);
const choice = computed(() => {
  if (!modelValue) return "default";
  if (modelValue.type === "macro") return `macro:${modelValue.macro}`;
  if (modelValue.type === "open") return `open:${openKind(modelValue.open)}`;
  if (modelValue.type === "appleShortcut") return `appleShortcut:${modelValue.shortcut}`;
  if (modelValue.type === "layer") return `layer:${modelValue.layer}`;
  if (modelValue.type === "profile") return `profile:${modelValue.profile}`;
  const preset = presetFor(modelValue);
  if (preset && !custom.value) return preset.id;
  // A fresh action has no keys and no choice yet; the picker asks for one.
  if (
    (modelValue.type === "press" || modelValue.type === "hold") &&
    !modelValue.keys?.length &&
    !custom.value
  )
    return "";
  if (modelValue.type === "hold") return "hold";
  if (modelValue.type === "press") return "shortcut";
  return "none";
});

/** Set as "open an app" is chosen, so its list opens straight away; editing an existing one doesn't. */
const justChose = ref(false);
const target = useTemplateRef("target");

function choose(id: string, name?: string) {
  custom.value = id === "shortcut" || id === "hold";
  justChose.value = id.startsWith("open:");
  if (id === "default") return emit("default");
  if (id.startsWith("open:")) {
    const kind = id.slice(5);
    if (modelValue?.type === "open" && openKind(modelValue.open) === kind) return;
    emit("update:modelValue", { type: "open", open: kind === "app" ? APP_PREFIX : kind === "url" ? "https://" : "" });
    if (kind !== "app") void nextTick(() => target.value?.focus());
    return;
  }
  if (id.startsWith("appleShortcut:"))
    return emit("update:modelValue", { type: "appleShortcut", shortcut: id.slice(14), name: name ?? null });
  if (id.startsWith("macro:"))
    return emit("update:modelValue", keepRepeat({
      type: "macro",
      macro: id.slice(6),
    }));
  if (id.startsWith("layer:"))
    return emit("update:modelValue", { type: "layer", layer: id.slice(6) as "momentary" | "toggle" });
  if (id.startsWith("profile:")) return emit("update:modelValue", { type: "profile", profile: id.slice(8) });
  const keys =
    modelValue && (modelValue.type === "press" || modelValue.type === "hold") && !presetFor(modelValue)
      ? modelValue.keys
      : [];
  if (id === "shortcut") emit("update:modelValue", keepRepeat({ type: "press", keys }));
  else if (id === "hold") emit("update:modelValue", { type: "hold", keys });
  else emit("update:modelValue", keepRepeat(presetById(id)?.action ?? NOTHING));
}

const repeatable = (action: ButtonAction) => ["press", "system", "macro"].includes(action.type);
const canRepeat = computed(() => !!modelValue && allowRepeat && repeatable(modelValue));
const keepRepeat = (action: ButtonAction): ButtonAction =>
  allowRepeat && modelValue?.repeat && repeatable(action) ? { ...action, repeat: true } : action;
const actionHint = computed(() => {
  if (modelValue?.type === "hold") return "Keys stay down until you release the button.";
  if (modelValue?.type === "press") return "One key combination, saved only on this action.";
  if (modelValue?.type === "macro") return "Shared macro. Editing it updates every assignment.";
  if (modelValue?.type === "layer")
    return modelValue.layer === "momentary"
      ? "The second layer runs while you hold this button."
      : "Each time, turns this profile's second layer on or off.";
  if (modelValue?.type === "profile")
    return "Programs with a profile of their own keep it; every other program gets this one.";
  if (modelValue?.type === "open")
    return {
      app: "Opens the app, or brings it to the front if it's running.",
      url: "Opens in your default browser, or the app that handles the link.",
      path: "Opens with its default app; a folder opens in " + (IS_MAC ? "Finder." : "File Explorer."),
    }[openKind(modelValue.open)];
  if (modelValue?.type === "appleShortcut") return "Runs the Shortcut from the Shortcuts app, in the background.";
  return "";
});

const update = (patch: Partial<ButtonAction>) =>
  modelValue && emit("update:modelValue", { ...modelValue, ...patch });

const IS_MAC = navigator.userAgent.includes("Mac");

/** A typed web address gets https:// if it has no scheme, since "example.com" alone would read as a file. */
function setTarget(value: string) {
  let next = value.trim().replace(/^"(.*)"$/, "$1");
  if (modelValue && openKind(modelValue.open) === "url" && next && openKind(next) === "path") next = `https://${next}`;
  if (modelValue && openKind(modelValue.open) === "url" && !next) next = "https://";
  update({ open: next });
}

const { goTo } = usePage();
const { state } = useAgent();
const macroKeys = computed(() => {
  if (modelValue?.type !== "macro") return null;
  const steps = state.value?.macros.find((macro) => macro.id === modelValue.macro)?.steps;
  return steps?.length === 1 && steps[0]?.type === "keys" && steps[0].keys?.length ? steps[0].keys : null;
});

/** Copies a one-combination macro into this action without changing the shared macro. */
function useAsShortcut() {
  if (!macroKeys.value) return;
  custom.value = true;
  emit("update:modelValue", keepRepeat({ type: "press", keys: [...macroKeys.value] }));
}
</script>
