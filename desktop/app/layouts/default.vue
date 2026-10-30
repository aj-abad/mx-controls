<template>
  <div class="flex h-full flex-col">
    <!-- The window has no system title bar. On macOS the traffic lights keep a row of their own above the header;
         elsewhere the header is the title bar, with the app's own minimize and close at its end. -->
    <div v-if="isMac" class="h-7 shrink-0" />
    <header class="flex h-12 shrink-0 items-center gap-3 pl-6" :class="isMac ? 'pr-8' : 'pr-3'">
      <h1 class="text-xl font-semibold tracking-tight">
        <DeviceInfo v-if="device" :device="device" />
        <span v-else class="pl-2">MX Controls</span>
      </h1>
      <span v-if="device" class="flex items-center gap-1.5 text-sm text-ink-2">
        <BatteryIcon :percent="device.battery.percent" :charging="device.battery.charging" class="size-5" />
        <span class="tabular-nums">{{ device.battery.percent }}%</span>
        <PhLightning v-if="device.battery.charging" weight="fill" class="-ml-1 size-3.5 text-good" aria-hidden="true" />
        <span v-if="device.battery.charging" class="sr-only">, charging</span>
      </span>
      <span class="flex-1" />
      <WindowControls v-if="!isMac" />
    </header>

    <!-- Which computer the mouse is on, then which profile it's running; both change often, so both stay in view. -->
    <div v-if="pipeConnected && device" class="flex h-10 shrink-0 items-center gap-3 px-8">
      <template v-if="device.hosts">
        <EasySwitch :hosts="device.hosts" />
        <span class="h-5 w-px shrink-0 bg-line" />
      </template>
      <ProfileBar />
    </div>

    <!-- Takes its space at once and only fades: growing its height would re-zoom the whole stage every frame. -->
    <AnimatePresence :initial="false">
      <motion.div
        v-if="pipeConnected && device && shared"
        class="shrink-0"
        :initial="{ opacity: 0 }"
        :animate="{ opacity: 1 }"
        :exit="{ opacity: 0, transition: EXIT }"
        :transition="BASE">
        <div
          role="status"
          class="mx-8 mt-2 flex items-center gap-2.5 rounded-xl bg-warn-fill px-4 py-2.5 text-sm text-warn">
          <PhWarning class="size-4 shrink-0" />
          <p>
            <span class="font-medium">
              {{ state?.optionsPlusRunning ? "Logi Options+" : "Another app" }} is also responding to
              {{ shared === 1 ? "1 of your controls" : `${shared} of your controls` }}.
            </span>
            Quit it, or set {{ shared === 1 ? "that control" : "those controls" }} here, so each press does one thing.
          </p>
        </div>
      </motion.div>
    </AnimatePresence>

    <main class="min-h-0 flex-1 px-8 pt-3 pb-8">
      <div v-if="!pipeConnected || !device" class="grid h-full place-items-center">
        <div class="flex max-w-sm flex-col items-center text-center">
          <img src="/mx-master-4.png" alt="" class="mb-2 w-64 opacity-60" />
          <h2 class="text-lg font-semibold">
            {{ pipeConnected ? "Connect your MX Master" : "Starting MX Controls" }}
          </h2>
          <p class="mt-1 text-sm text-ink-3">
            {{
              pipeConnected
                ? (state?.error ?? "Turn the mouse on and connect it to this computer over Bluetooth. It appears here on its own.")
                : "Getting the background helper ready. It runs your buttons, even with this window closed."
            }}
          </p>
        </div>
      </div>
      <!-- The page stays mounted so NuxtPage always renders; it draws only once the mouse is there. -->
      <div v-show="pipeConnected && device" class="h-full"><slot /></div>
    </main>
  </div>
</template>

<script setup lang="ts">
import { PhLightning, PhWarning } from "@phosphor-icons/vue";
import { AnimatePresence, motion } from "motion-v";

const { state, device, pipeConnected } = useAgent();

const shared = computed(() => (state.value ? sharedControlCount(state.value) : 0));
const isMac = navigator.userAgent.includes("Mac");
</script>
