<script setup lang="ts">
import { computed, ref } from 'vue'

import type { ServeConfig } from '../api/client'
import type { EventStreamState } from '../state/useTasks'

const props = defineProps<{
  config: ServeConfig
  connected: boolean
  eventStream: EventStreamState
  error: string | null
}>()

const emit = defineEmits<{
  save: [config: ServeConfig]
}>()

const editing = ref(false)
const baseUrl = ref(props.config.baseUrl)
const token = ref(props.config.token)

/** 事件流状态展示：active 绿色（已连接并推送），reconnecting/connecting 黄色，其余黄色（连接中/重连）。 */
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

const save = (): void => {
  emit('save', { baseUrl: baseUrl.value.trim(), token: token.value.trim() })
  editing.value = false
}

/** 空 baseUrl 归一为本机 serve 默认地址（直连），非空 = 直连指定 serve 地址。 */
const displayUrl = computed(() => props.config.baseUrl.trim() || '默认直连（127.0.0.1:23333）')
</script>

<template>
  <div class="relative flex items-center gap-2.5 text-sm">
    <span class="stat" :title="connected ? 'REST 连接正常' : 'REST 连接失败'">
      <i
        class="stat-dot"
        :style="{ background: connected ? 'var(--st-success)' : 'var(--st-failed)' }" />
      {{ connected ? '已连接' : '未连接' }}
    </span>
    <span class="stat" :title="eventStreamLabel.tip">
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
    <button class="btn-ghost" type="button" @click="editing = !editing">设置</button>

    <Teleport to="body">
      <div
        v-if="editing"
        class="fixed inset-0 z-50 flex items-center justify-center bg-black/55 p-4 backdrop-blur-sm"
        @click.self="editing = false">
        <div class="card card-pad w-96 max-w-full">
          <div class="mb-3 text-sm font-semibold">serve 连接设置</div>
          <label class="label mb-1.5" for="base-url">服务器地址</label>
          <input
            id="base-url"
            v-model="baseUrl"
            class="field mb-3"
            placeholder="留空：直连本机 127.0.0.1:23333（默认免令牌）；或填 http://<ip>:23333"
            @keydown.enter="save" />
          <label class="label mb-1.5" for="serve-token">鉴权令牌（可选）</label>
          <input
            id="serve-token"
            v-model="token"
            class="field mb-4"
            placeholder="默认免令牌，传入 --serve-token 后需 X-BBDown-Token"
            @keydown.enter="save" />
          <div class="flex justify-end gap-2">
            <button class="btn-ghost" type="button" @click="editing = false">取消</button>
            <button class="btn-primary" type="button" @click="save">保存</button>
          </div>
          <p class="mt-3 text-xs leading-relaxed text-[var(--text-dim)]">
            需先以
            <code class="text-[var(--accent)]">BBDown serve</code> 启动服务端。地址留空即直连本机
            <code class="text-[var(--accent)]">127.0.0.1:23333</code>（默认免令牌，且回环 Origin
            默认可跨域）；若服务端以
            <code class="text-[var(--accent)]">--serve-token</code> 启用了鉴权，须在此填入对应的
            <code class="text-[var(--accent)]">X-BBDown-Token</code> 令牌。跨机器访问时还须以
            <code class="text-[var(--accent)]">--cors-origin</code>
            允许本页来源。事件流默认开启（推送日志与选项交互）。
          </p>
        </div>
      </div>
    </Teleport>
  </div>
</template>
