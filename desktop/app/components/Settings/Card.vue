<template>
  <Squircle
    :radius="RADIUS.row"
    :border-width="1"
    :border-color="colorVar.line"
    :class="cn('py-2', $attrs.class as string)"
    v-bind="rest">
    <slot />
  </Squircle>
</template>

<script setup lang="ts">
import { Squircle } from "@arcon.mobi/sine/utils/squircle";
import { RADIUS } from "@arcon.mobi/sine/utils/controlSquircle";
import { cn } from "@arcon.mobi/sine/utils/cn";
import { colorVar } from "@arcon.mobi/sine/tokens";

// `class` goes through cn, so a caller's padding replaces the default `py-2`.
defineOptions({ inheritAttrs: false });

const attrs = useAttrs();
const rest = computed(() => {
  const { class: _class, ...others } = attrs;
  return others;
});
</script>
