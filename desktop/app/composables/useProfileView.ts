import type { ButtonBinding, ProfileSummary, ThumbWheelSettings } from "~/composables/useAgent";

export type LayerId = "main" | "second";

/** The profile being edited, which can differ from the one running: a profile that follows programs is only edited. */
const viewedId = ref<string | null>(null);
const layer = ref<LayerId>("main");

/** The profile and layer the Profiles page shows and edits, and requests aimed at them. */
export function useProfileView() {
  const { state } = useAgent();

  const profile = computed<ProfileSummary | null>(() => {
    const s = state.value;
    if (!s) return null;
    return (
      s.profiles.find((p) => p.id === viewedId.value) ??
      s.profiles.find((p) => p.id === s.selectedProfile) ??
      s.profiles[0] ??
      null
    );
  });
  const second = computed(() => layer.value === "second");

  /** The bindings of the layer shown; in the second layer, a button without one runs its main binding. */
  const bindings = computed<ButtonBinding[]>(() =>
    profile.value ? (second.value ? profile.value.layer.bindings : profile.value.bindings) : [],
  );
  const mainBindings = computed<ButtonBinding[]>(() => profile.value?.bindings ?? []);
  /** Null in the second layer while it keeps the main layer's thumb wheel. */
  const thumbWheel = computed<ThumbWheelSettings | null>(() =>
    profile.value ? (second.value ? profile.value.layer.thumbWheel : profile.value.thumbWheel) : null,
  );
  const mainThumbWheel = computed(() => profile.value?.thumbWheel ?? null);

  /** Where `setBindings` and `setThumbWheel` go, fixed when they're made so a later switch can't redirect them. */
  const target = () => ({ profile: profile.value?.id, layer: second.value });

  const view = (id: string) => (viewedId.value = id);

  return { profile, layer, second, bindings, mainBindings, thumbWheel, mainThumbWheel, target, view };
}
