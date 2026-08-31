<script setup lang="ts">
import { nextTick, ref, watch } from 'vue'

import type { LogLine } from '../state/types'

const props = defineProps<{
  logLines: LogLine[]
}>()

const listEl = ref<HTMLDivElement | null>(null)

watch(
  () => props.logLines.length,
  async () => {
    await nextTick()
    if (listEl.value) {
      listEl.value.scrollTop = listEl.value.scrollHeight
    }
  }
)
</script>

<template>
  <div
    ref="listEl"
    class="max-h-52 overflow-y-auto rounded-[var(--radius-sm)] border border-[var(--hairline)] bg-[var(--glass-2)] p-2.5 font-mono text-xs leading-relaxed">
    <div
      v-for="(line, index) in logLines"
      :key="index"
      class="break-all whitespace-pre-wrap"
      :class="line.isError ? 'text-[var(--st-failed)]' : 'text-[var(--text-dim)]'">
      {{ line.text }}
    </div>
    <div v-if="logLines.length === 0" class="text-[var(--text-faint)]">（空）</div>
  </div>
</template>
