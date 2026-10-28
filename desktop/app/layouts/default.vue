<template>
  <div class="flex h-full">
    <aside class="flex w-56 shrink-0 flex-col gap-4 border-r border-line px-2 py-4">
      <div class="px-3.5">
        <p class="truncate font-medium">{{ device?.name ?? "MX Controls" }}</p>
        <p class="type-caption mt-0.5">
          <template v-if="device">
            Battery {{ device.battery.percent }}%{{ device.battery.charging ? ", charging" : "" }}
          </template>
          <template v-else>{{ pipeConnected ? "Not connected" : "Starting…" }}</template>
        </p>
      </div>
      <nav aria-label="Sections" class="flex flex-col gap-1">
        <AppNavItem to="/buttons" :icon="IconMouse" label="Buttons" />
        <AppNavItem to="/pointer" :icon="IconGauge" label="Pointer and scrolling" />
        <AppNavItem to="/haptics" :icon="IconHaptic" label="Haptics" />
        <AppNavItem to="/device" :icon="IconInfo" label="Device" />
      </nav>
    </aside>

    <main class="min-w-0 flex-1 overflow-y-auto">
      <div class="mx-auto max-w-(--breakpoint-md) px-6 pt-6 pb-10">
        <div v-if="!pipeConnected" class="grid min-h-[60vh] place-items-center">
          <EmptyState
            title="Starting the MX Controls agent"
            description="The agent talks to the mouse in the background. It starts with this window if it isn't running." />
        </div>
        <div v-else-if="!device" class="grid min-h-[60vh] place-items-center">
          <EmptyState
            title="Connect your MX Master"
            :description="
              state?.error ??
              'Turn the mouse on and connect it to this computer over Bluetooth. It appears here on its own.'
            " />
        </div>
        <!-- Pages stay mounted so NuxtPage always renders; each page draws only once the mouse is there. -->
        <div v-show="device"><slot /></div>
      </div>
    </main>
  </div>
</template>

<script setup lang="ts">
import EmptyState from "@arcon.mobi/sine/components/UI/EmptyState.vue";
import IconMouse from "#sine-icons/ui/glyph/18/mouse";
import IconGauge from "#sine-icons/ui/glyph/18/gauge";
import IconHaptic from "#sine-icons/ui/glyph/18/haptic";
import IconInfo from "#sine-icons/ui/glyph/18/circle-info";

const { state, device, pipeConnected } = useAgent();
</script>
