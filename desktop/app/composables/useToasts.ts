export interface ToastMessage {
  id: number;
  title: string;
  tone: "neutral" | "error";
  /** A button on the toast, such as Undo; pressing it closes the toast. */
  action?: { label: string; run: () => void };
  /** A toast with the same key takes the place of this one, so a run of edits leaves one toast. */
  key?: string;
}

const toasts = ref<ToastMessage[]>([]);
let nextId = 0;

/** Raises a toast; `UiToaster` in the app shell draws them. */
export function showToast(
  title: string,
  tone: ToastMessage["tone"] = "neutral",
  options: Pick<ToastMessage, "action" | "key"> = {},
) {
  const others = options.key ? toasts.value.filter((t) => t.key !== options.key) : toasts.value;
  toasts.value = [...others, { id: ++nextId, title, tone, ...options }];
}

export function useToasts() {
  const dismiss = (id: number) => (toasts.value = toasts.value.filter((t) => t.id !== id));
  return { toasts, dismiss };
}
