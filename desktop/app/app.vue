<template>
  <!-- With Reduce Motion on, movement turns into fades; nothing that carries meaning is lost. -->
  <MotionConfig reduced-motion="user">
    <TooltipProvider :delay-duration="300">
      <NuxtLayout>
        <NuxtPage />
      </NuxtLayout>
      <UiToaster />
    </TooltipProvider>
  </MotionConfig>
</template>

<script setup lang="ts">
import { MotionConfig } from "motion-v";
import { TooltipProvider } from "reka-ui";

useWindowDrag();
</script>
