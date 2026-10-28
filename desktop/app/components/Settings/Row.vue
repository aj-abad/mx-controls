<template>
  <div
    :class="
      cn(
        'flex flex-wrap items-center gap-x-4 gap-y-2',
        control ? 'px-3' : 'px-4 min-h-10',
        $attrs.class as string,
      )
    ">
    <component :is="labelFor ? 'label' : 'div'" :for="labelFor" class="min-w-0 flex-[1_1_12rem]">
      <span class="flex items-center gap-2">
        <span :class="cn('text-ink-3', muted && 'text-ink-4')">{{ label }}</span>
        <slot name="badge" />
      </span>
      <p v-if="description" :id="descriptionId" class="mt-0.5 text-sm text-ink-3">
        {{ description }}
      </p>
    </component>
    <!-- Drops under the label when the two don't fit on one line. -->
    <div class="flex max-w-full shrink-0 items-center gap-3">
      <slot :described-by="descriptionId" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { cn } from "@arcon.mobi/sine/utils/cn";
defineOptions({ inheritAttrs: false });

const { description } = defineProps<{
  label: string;
  description?: string;
  /** Row holds a form control that supplies its own horizontal padding. */
  control?: boolean;
  /** `id` of the control, when the label should be clickable. */
  labelFor?: string;
  muted?: boolean;
}>();

const rowId = useId();
const descriptionId = computed(() => (description ? `${rowId}-description` : undefined));
</script>
