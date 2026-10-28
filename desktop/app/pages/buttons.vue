<template>
  <div v-if="device" class="flex flex-col gap-6">
    <header>
      <h2 class="type-title">Buttons</h2>
      <p class="mt-1 text-ink-3">
        A mapped button holds its key down for as long as you hold the button, then releases it.
      </p>
    </header>

    <Callout v-if="sharedMapped.length" tone="warn" title="Logi Options+ also uses these buttons">
      Pressing {{ sharedMapped.join(", ") }} also runs whatever Options+ assigns. Unassign it in Options+, or quit
      Options+.
    </Callout>

    <SettingsSection title="Held keys" note="Press a mouse button to find it here. A mapped button stops doing its usual action.">
      <SettingsCard>
        <template v-for="(button, index) in device.buttons" :key="button.id">
          <Divider v-if="index > 0" />
          <SettingsRow :label="button.name" :description="describe(button)">
            <template #badge>
              <StatusPill v-if="held.has(button.id)" tone="live" dot size="xs">Pressed</StatusPill>
            </template>
            <KeyCapture
              :model-value="keyFor(button.id)"
              :button-name="button.name"
              @update:model-value="(code) => setKey(button.id, code)" />
            <Button v-if="keyFor(button.id)" variant="ghost" size="sm" @click="setKey(button.id, null)">
              Clear
            </Button>
          </SettingsRow>
        </template>
      </SettingsCard>
    </SettingsSection>
  </div>
</template>

<script setup lang="ts">
import Button from "@arcon.mobi/sine/components/UI/Button.vue";
import Callout from "@arcon.mobi/sine/components/UI/Callout.vue";
import Divider from "@arcon.mobi/sine/components/UI/Divider.vue";
import StatusPill from "@arcon.mobi/sine/components/UI/StatusPill.vue";
import type { ButtonMapping, ButtonState } from "~/composables/useAgent";

const { state, device, held, attempt } = useAgent();

const mappings = computed(() => state.value?.mappings ?? []);
const keyFor = (button: string) => mappings.value.find((m) => m.button === button)?.key ?? null;

const sharedMapped = computed(
  () => device.value?.buttons.filter((b) => b.divertedElsewhere && keyFor(b.id) && state.value?.optionsPlusRunning).map((b) => b.name) ?? [],
);

function describe(button: ButtonState) {
  const key = keyFor(button.id);
  if (key) return `Holds ${keyLabel(key)}`;
  if (button.divertedElsewhere) return "Used by another app, such as Logi Options+";
  return "Does its usual action";
}

async function setKey(button: string, key: string | null) {
  const next: ButtonMapping[] = mappings.value.filter((m) => m.button !== button);
  if (key) next.push({ button, key });
  await attempt("setMappings", { mappings: next }, "Couldn't save the button");
}
</script>
