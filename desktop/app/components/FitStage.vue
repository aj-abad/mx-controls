<template>
  <div ref="frame" class="grid size-full place-items-center overflow-hidden">
    <!-- `zoom` rather than a transform, so text stays crisp and layout sees the scaled size. -->
    <div class="relative" :style="{ width: `${width}px`, height: `${height}px`, zoom: scale }">
      <slot />
    </div>
  </div>
</template>

<script setup lang="ts">
/** Draws its content at a fixed design size, scaled to fit the space it's given. */
const { width, height, maxScale = 1.15 } = defineProps<{ width: number; height: number; maxScale?: number }>();

const frame = useTemplateRef("frame");
const scale = ref(1);

let observer: ResizeObserver | undefined;
onMounted(() => {
  observer = new ResizeObserver(([entry]) => {
    const { width: available, height: tall } = entry!.contentRect;
    if (available && tall) scale.value = Math.min(available / width, tall / height, maxScale);
  });
  observer.observe(frame.value!);
});
onBeforeUnmount(() => observer?.disconnect());
</script>
