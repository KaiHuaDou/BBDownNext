<script setup lang="ts">
import { nextTick, ref, watch } from 'vue'

import type { LogLine } from '../state/useTasks'

const props = defineProps<{
  logLines: LogLine[]
}>()

const emit = defineEmits<{
  export: []
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
  <div class="flex flex-col gap-1.5">
    <div
      ref="listEl"
      class="max-h-64 overflow-y-auto rounded border border-[#3c3c3c] bg-[#1a1a1c] p-2 font-mono text-xs leading-relaxed">
      <div
        v-for="(line, index) in logLines"
        :key="index"
        class="break-all whitespace-pre-wrap"
        :class="line.isError ? 'text-[#e53935]' : 'text-[#ddd]'">
        {{ line.text }}
      </div>
      <div v-if="logLines.length === 0" class="text-[#6e6e6e]">（空）</div>
    </div>
    <button class="btn-ghost w-fit" type="button" @click="emit('export')">导出日志</button>
  </div>
</template>
