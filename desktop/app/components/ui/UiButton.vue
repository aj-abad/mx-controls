<template>
  <button
    :type="type"
    :disabled="disabled"
    :class="[
      'inline-flex items-center justify-center gap-2 rounded-lg font-medium transition-[color,background-color,scale] duration-150 ease-out enabled:active:scale-[0.97] disabled:cursor-default disabled:opacity-40',
      size === 'sm' ? 'h-8 px-3.5 text-sm' : 'h-10 px-5 text-sm',
      VARIANTS[variant],
    ]">
    <slot />
  </button>
</template>

<script setup lang="ts">
const {
  variant = "primary",
  size = "md",
  type = "button",
  disabled = false,
} = defineProps<{
  variant?: "primary" | "secondary" | "ghost";
  size?: "sm" | "md";
  type?: "button" | "submit";
  disabled?: boolean;
}>();

const VARIANTS = {
  primary: "bg-action text-white enabled:hover:brightness-90",
  secondary: "bg-field text-ink ring-1 ring-line ring-inset enabled:hover:bg-fill",
  ghost: "text-ink-2 enabled:hover:bg-fill",
};
</script>
