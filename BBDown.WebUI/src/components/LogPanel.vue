<script setup lang="ts">
import { computed, ref, watch } from 'vue'

import type { LogLine } from '../state/types'

const props = defineProps<{
  logLines: LogLine[]
}>()

/** 实际渲染的窗口大小：全量 5000 行的 DOM 太重，只渲染尾部一段。 */
const RENDER_WINDOW = 500

const listEl = ref<HTMLDivElement | null>(null)
/** 视口贴近底部时才跟随滚动；用户上翻阅读时不抢视图 */
const pinnedToBottom = ref(true)

/** 渲染窗口的起始下标与被折叠的早前行数。 */
const hiddenCount = computed(() => Math.max(0, props.logLines.length - RENDER_WINDOW))
const visibleLines = computed(() => props.logLines.slice(hiddenCount.value))

let pendingFrame = 0
function scheduleScroll(): void {
  if (pendingFrame !== 0) {
    return
  }

  pendingFrame = requestAnimationFrame(() => {
    pendingFrame = 0
    const el = listEl.value
    if (el && pinnedToBottom.value) {
      el.scrollTop = el.scrollHeight
    }
  })
}

watch(() => props.logLines.length, scheduleScroll)
</script>

<template>
  <div
    ref="listEl"
    class="max-h-52 overflow-y-auto rounded-[var(--radius-sm)] border border-[var(--hairline)] bg-[var(--glass-2)] p-2.5 font-mono text-xs leading-relaxed"
    @scroll="pinnedToBottom = listEl ? listEl.scrollHeight - listEl.scrollTop - listEl.clientHeight < 24 : true">
    <div v-if="hiddenCount > 0" class="mb-1 text-[var(--text-faint)]">
      （更早 {{ hiddenCount }} 行已折叠）
    </div>
    <div
      v-for="(line, index) in visibleLines"
      :key="hiddenCount + index"
      class="break-all whitespace-pre-wrap"
      :class="line.isError ? 'text-[var(--st-failed)]' : 'text-[var(--text-dim)]'">
      {{ line.text }}
    </div>
    <div v-if="logLines.length === 0" class="text-[var(--text-faint)]">（空）</div>
  </div>
</template>