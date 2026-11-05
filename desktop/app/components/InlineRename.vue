<template>
  <form class="flex min-w-0 flex-1 items-center gap-1.5" @submit.prevent="save">
    <input
      ref="input"
      v-model="name"
      :aria-label="label"
      :maxlength="maxlength"
      class="h-8 min-w-0 flex-1 rounded-lg bg-field px-2.5 text-sm ring-1 ring-ink ring-inset outline-none!"
      @keydown.escape.stop.prevent="emit('cancel')"
      @blur="save" />
  </form>
</template>

<script setup lang="ts">
const { value } = defineProps<{ value: string; label: string; maxlength?: number }>();
/** `save` comes with the trimmed name, and only when it changed; anything else cancels. */
const emit = defineEmits<{ save: [name: string]; cancel: [] }>();

const name = ref(value);
const input = useTemplateRef("input");
onMounted(() => input.value?.select());

let done = false;
function save() {
  if (done) return;
  done = true;
  const next = name.value.trim();
  if (next && next !== value) emit("save", next);
  else emit("cancel");
}
</script>
