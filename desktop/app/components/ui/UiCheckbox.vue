<template>
  <label class="inline-flex cursor-default items-center gap-2.5 text-sm">
    <CheckboxRoot
      v-model="checked"
      class="flex size-[18px] items-center justify-center rounded-[5px] bg-field ring-1 ring-fill-strong ring-inset data-[state=checked]:bg-ink data-[state=checked]:ring-ink">
      <CheckboxIndicator>
        <PhCheck class="size-3.5 text-on-ink" />
      </CheckboxIndicator>
    </CheckboxRoot>
    {{ label }}
  </label>
</template>

<script setup lang="ts">
import { PhCheck } from "@phosphor-icons/vue";
import { CheckboxIndicator, CheckboxRoot } from "reka-ui";

defineProps<{ label: string }>();
const checked = defineModel<boolean>({ required: true });
</script>
