<script setup lang="ts">
import { computed } from 'vue'

import type { ServeConfig } from '../api/client'
import type { EventStreamState } from '../state/useTasks'

const props = defineProps<{
  config: ServeConfig
  connected: boolean
  eventStream: EventStreamState
  error: string | null
}>()

const emit = defineEmits<{
  settings: []
}>()

/** 事件流状态展示：active 绿色（已连接并推送），reconnecting/connecting 黄色（连接中/重连）。 */
const eventStreamLabel = computed<{ text: string; dot: string; tip: string }>(() => {
  switch (props.eventStream) {
    case 'active': {
      return {
        text: '事件流已开启',
        dot: 'var(--st-success)',
        tip: '任务列表 / 日志 / 选项交互均由事件流推送'
      }
    }
    case 'reconnecting': {
      return { text: '事件流重连中', dot: 'var(--st-waiting)', tip: 'WebSocket 断开，正在重连' }
    }
    default: {
      return { text: '事件流连接中', dot: 'var(--st-waiting)', tip: '正在建立 WebSocket 连接' }
    }
  }
})

/** 空 baseUrl 归一为本机 serve 默认地址（直连），非空 = 直连指定 serve 地址。 */
const displayUrl = computed(() => props.config.baseUrl.trim() || '默认直连（127.0.0.1:23333）')
</script>

<template>
  <div class="flex items-center gap-2.5 text-sm">
    <span class="stat-text">
      <i
        class="stat-dot"
        :style="{ background: connected ? 'var(--st-success)' : 'var(--st-failed)' }" />
      {{ connected ? '已连接' : '未连接' }}
    </span>
    <span class="stat-text" :title="eventStreamLabel.tip">
      <i class="stat-dot" :style="{ background: eventStreamLabel.dot }" />
      {{ eventStreamLabel.text }}
    </span>
    <span
      v-if="error"
      class="max-w-[220px] truncate text-xs text-[var(--st-failed)]"
      :title="error"
      >{{ error }}</span
    >
    <span
      class="hidden truncate text-xs text-[var(--text-faint)] md:inline"
      :title="config.baseUrl"
      >{{ displayUrl }}</span
    >
    <button class="btn-ghost" type="button" @click="emit('settings')">设置</button>
  </div>
</template>
