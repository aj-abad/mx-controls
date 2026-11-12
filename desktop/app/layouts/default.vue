<template>
  <div class="relative flex h-full flex-col">
    <div v-if="isMac" class="h-7 shrink-0" />
    <header class="flex h-12 shrink-0 items-center gap-3 border-b border-line pl-5">
      <div class="flex w-36 shrink-0 items-center gap-2.5">
        <span class="text-sm font-semibold">MX Studio</span>
      </div>
      <div v-if="device" class="flex min-w-0 items-center gap-3">
        <DeviceInfo :device="device" />
        <span class="flex items-center gap-1.5 text-xs text-ink-3">
          <BatteryIcon
            :percent="device.battery.percent"
            :charging="device.battery.charging"
            class="size-4" />
          <span class="tabular-nums">{{ device.battery.percent }}%</span>
          <PhLightning
            v-if="device.battery.charging"
            weight="fill"
            class="size-3 text-good"
            aria-label="Charging" />
        </span>
      </div>
      <span class="flex-1" />
      <WindowControls v-if="!isMac" />
    </header>

    <div class="flex min-h-0 flex-1">
      <aside class="flex w-42 shrink-0 flex-col border-r border-line bg-panel/40 px-3 pt-4 pb-4">
        <nav aria-label="Workspace" class="flex flex-col gap-1">
          <button
            type="button"
            :disabled="!ready"
            :class="navClass('profiles')"
            :aria-current="page === 'profiles' ? 'page' : undefined"
            @click="goTo('profiles')">
            <PhStack class="size-4.5" :weight="page === 'profiles' ? 'fill' : 'regular'" /> Profiles
          </button>
          <p class="mt-5 px-3 pb-1 text-xs font-semibold text-ink-3">Shared library</p>
          <button
            v-for="item in library"
            :key="item.id"
            type="button"
            :disabled="!ready"
            :class="navClass(item.id)"
            :aria-current="page === item.id ? 'page' : undefined"
            @click="goTo(item.id)">
            <component
              :is="item.icon"
              class="size-4.5"
              :weight="page === item.id ? 'fill' : 'regular'" />
            {{ item.label }}
            <span class="ml-auto text-[11px] font-normal text-ink-3 tabular-nums">{{
              item.count
            }}</span>
          </button>
        </nav>
        <div class="mt-auto flex flex-col gap-3 px-2 pt-5">
          <div class="h-8"><EasySwitch v-if="ready && device?.hosts" :hosts="device.hosts" /></div>
          <p class="flex h-4 items-center gap-1.5 px-1 text-[11px] text-ink-3">
            <span class="size-1.5 shrink-0 rounded-full" :class="ready ? 'bg-good' : 'bg-warn'" />{{
              ready ? "Mouse connected" : pipeConnected ? "Mouse disconnected" : "Reconnecting…"
            }}
          </p>
        </div>
      </aside>

      <div class="flex min-w-0 flex-1 flex-col">
        <header class="flex h-20 shrink-0 items-center gap-5 px-6 py-3">
          <div class="min-w-0 flex-1">
            <h1 class="text-xl font-semibold">{{ titles[page] }}</h1>
            <p class="mt-1 text-xs text-ink-3">{{ descriptions[page] }}</p>
          </div>
          <UiScrollArea
            v-if="page === 'profiles' && ready"
            orientation="horizontal"
            class="max-w-[55%]"
            content-class="py-1"
            :viewport-attrs="{ tabindex: -1 }"
            ><ProfileBar
          /></UiScrollArea>
          <span
            v-else-if="page !== 'profiles'"
            class="flex shrink-0 items-center gap-1.5 rounded-full bg-fill/60 px-2.5 py-1 text-[11px] text-ink-2"
            ><PhStack class="size-3.5" /> Shared across profiles</span
          >
        </header>

        <div v-if="page === 'profiles'" class="mx-6 mb-3 h-10 shrink-0">
          <div
            v-if="ready && shared"
            role="status"
            class="flex h-full items-center gap-2 rounded-xl bg-warn-fill px-3 text-xs text-warn">
            <PhWarning class="mt-px size-4 shrink-0" />
            <p>
              {{ state?.optionsPlusRunning ? "Logi Options+" : "Another app" }} conflicts with
              {{ shared }} {{ shared === 1 ? "control" : "controls" }} on this profile.
            </p>
          </div>
        </div>

        <AnimatePresence :initial="false">
          <!-- Leave footer recovery actions reachable while the error is showing. -->
          <motion.div
            v-if="failure"
            :key="failure.id"
            class="absolute top-14 right-6 z-50 w-96 max-w-[calc(100%-3rem)]"
            :initial="{ opacity: 0 }"
            :animate="{ opacity: 1 }"
            :exit="{ opacity: 0, transition: EXIT }"
            :transition="BASE">
            <div
              role="alert"
              class="flex items-center gap-2.5 rounded-xl bg-field py-1.5 pr-1.5 pl-4 text-sm shadow-lg ring-1 ring-danger/40">
              <PhWarningCircle class="size-4 shrink-0 text-danger" />
              <p class="line-clamp-3 min-w-0 flex-1 wrap-break-word py-1" :title="failure.message">
                {{ failure.message }}
              </p>
              <button
                type="button"
                aria-label="Dismiss"
                class="grid size-7 shrink-0 place-items-center rounded-full text-ink-3 hover:bg-fill hover:text-ink"
                @click="dismissFailure">
                <PhX class="size-3.5" />
              </button>
            </div>
          </motion.div>
        </AnimatePresence>

        <main class="min-h-0 flex-1 px-6 pb-5">
          <div v-if="!pipeConnected || !device" class="grid h-full place-items-center">
            <div class="flex max-w-sm flex-col items-center text-center">
              <MouseDrawing :weight="2.5" aria-hidden="true" class="mb-3 w-36 text-ink-3" />
              <h2 class="text-lg font-semibold">
                {{ pipeConnected ? "Connect your MX Master" : "Starting MX Studio" }}
              </h2>
              <p class="mt-1 text-sm text-ink-3">
                {{
                  pipeConnected
                    ? (state?.error ??
                      "Turn the mouse on and connect it to this computer over Bluetooth. It appears here on its own.")
                    : "Getting the background helper ready. It runs your buttons, even with this window closed."
                }}
              </p>
            </div>
          </div>
          <div v-show="pipeConnected && device" class="h-full"><slot /></div>
        </main>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import {
  PhLightning,
  PhMouseSimple,
  PhPath,
  PhStack,
  PhWarning,
  PhWarningCircle,
  PhX,
  PhKeyboard,
} from "@phosphor-icons/vue";
import { AnimatePresence, motion } from "motion-v";
import type { Page } from "~/composables/usePage";

const { state, device, pipeConnected, failure, dismissFailure } = useAgent();
const ready = computed(() => pipeConnected.value && !!device.value);
const { page, goTo } = usePage();
const shared = computed(() => (state.value ? sharedControlCount(state.value) : 0));
const library = computed(() => [
  {
    id: "gestures" as const,
    label: "Gestures",
    icon: PhPath,
    count: state.value?.gesturePresets.length ?? 0,
  },
  {
    id: "macros" as const,
    label: "Macros",
    icon: PhKeyboard,
    count: state.value?.macros.length ?? 0,
  },
]);
const titles: Record<Page, string> = {
  profiles: "Profiles",
  gestures: "Gestures",
  macros: "Macros",
};
const descriptions: Record<Page, string> = {
  profiles: "Choose a profile, then select a control to customize it.",
  gestures: "Turn a held button and a movement into an action.",
  macros: "Build reusable sequences of keys, text and clicks.",
};
const navClass = (id: Page) => [
  "flex h-9 w-full items-center gap-2.5 rounded-lg px-3 text-sm font-medium transition-colors disabled:opacity-40",
  page.value === id ? "bg-accent/12 text-ink" : "text-ink-2 hover:bg-fill/60 hover:text-ink",
];
const isMac = navigator.userAgent.includes("Mac");
</script>
