<template>
  <ToastProvider :duration="5000" swipe-direction="down">
    <ToastRoot
      v-for="toast in toasts"
      :key="toast.id"
      class="flex items-center gap-3 rounded-xl px-4 py-3 text-sm shadow-lg data-[state=closed]:animate-toast-out data-[state=open]:animate-toast-in"
      :class="toast.tone === 'error' ? 'bg-ink text-white' : 'bg-field text-ink ring-1 ring-line'"
      @update:open="(open) => !open && settle(toast.id)">
      <span v-if="toast.tone === 'error'" class="size-2 shrink-0 rounded-full bg-[#ff5a4f]" aria-hidden="true" />
      <ToastTitle class="min-w-0 flex-1">{{ toast.title }}</ToastTitle>
      <ToastAction
        v-if="toast.action"
        :alt-text="toast.action.label"
        class="-my-1 -mr-2 h-8 shrink-0 cursor-pointer rounded-full px-3 font-medium transition-colors hover:bg-fill"
        @click="toast.action.run()">
        {{ toast.action.label }}
      </ToastAction>
    </ToastRoot>
    <ToastViewport class="fixed bottom-6 left-1/2 z-50 flex w-[min(28rem,calc(100vw-2rem))] -translate-x-1/2 flex-col gap-2" />
  </ToastProvider>
</template>

<script setup lang="ts">
import { ToastAction, ToastProvider, ToastRoot, ToastTitle, ToastViewport } from "reka-ui";

const { toasts, dismiss } = useToasts();

// Reka keeps a closed toast in the page until its exit animation ends; dropping it from the list first would cut that short.
const settle = (id: number) => setTimeout(() => dismiss(id), 200);
</script>
