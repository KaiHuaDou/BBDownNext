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

/** 事件流状态展示：active 绿色，disabled 灰色（serve 未开 --interactive），其余黄色（连接中/重连）。 */
const eventStreamLabel = computed<{ text: string; cls: string; tip: string }>(() => {
  switch (props.eventStream) {
    case 'active': {
      return { text: '事件流已开启', cls: 'bg-[#4caf50]', tip: '日志与选项交互可用' }
    }
    case 'disabled': {
      return {
        text: '事件流未启用',
        cls: 'bg-[#9e9e9e]',
        tip: 'serve 未以 --interactive 启动：日志与选项交互不可用，任务状态由轮询提供'
      }
    }
    case 'reconnecting': {
      return { text: '事件流重连中', cls: 'bg-[#c9a227]', tip: 'WebSocket 断开，正在重连' }
    }
    default: {
      return { text: '事件流连接中', cls: 'bg-[#c9a227]', tip: '正在建立 WebSocket 连接' }
    }
  }
})

const save = (): void => {
  emit('save', { baseUrl: baseUrl.value.trim(), token: token.value.trim() })
  editing.value = false
}
</script>

<template>
  <div class="flex items-center gap-2 text-sm">
    <span
      class="inline-block h-2.5 w-2.5 rounded-full"
      :class="connected ? 'bg-[#4caf50]' : 'bg-[#e53935]'"
      :title="connected ? 'REST 连接正常' : 'REST 连接失败'" />
    <span class="text-[#9e9e9e]">{{ connected ? '已连接' : '未连接' }}</span>
    <span
      class="inline-block h-2.5 w-2.5 rounded-full"
      :class="eventStreamLabel.cls"
      :title="eventStreamLabel.tip" />
    <span class="text-xs text-[#9e9e9e]" :title="eventStreamLabel.tip">{{
      eventStreamLabel.text
    }}</span>
    <span v-if="error" class="truncate text-xs text-[#e53935]" :title="error">{{ error }}</span>
    <span class="ml-auto truncate text-xs text-[#9e9e9e]">{{ config.baseUrl }}</span>
    <button class="btn-ghost" type="button" @click="editing = !editing">设置</button>

    <div
      v-if="editing"
      class="absolute right-3 top-12 z-30 w-96 rounded border border-[#3c3c3c] bg-[#252526] p-4 shadow-lg">
      <div class="mb-2 text-sm font-semibold text-[#eee]">serve 连接设置</div>
      <label class="mb-1 block text-xs text-[#9e9e9e]" for="base-url">服务器地址</label>
      <input
        id="base-url"
        v-model="baseUrl"
        class="field-input mb-3"
        placeholder="http://127.0.0.1:23333"
        @keydown.enter="save" />
      <label class="mb-1 block text-xs text-[#9e9e9e]" for="serve-token">鉴权令牌（可选）</label>
      <input
        id="serve-token"
        v-model="token"
        class="field-input mb-3"
        placeholder="回环地址免令牌，非回环需 X-BBDown-Token"
        @keydown.enter="save" />
      <div class="flex justify-end gap-2">
        <button class="btn-ghost" type="button" @click="editing = false">取消</button>
        <button class="btn-action" type="button" @click="save">保存</button>
      </div>
      <p class="mt-2 text-xs leading-relaxed text-[#9e9e9e]">
        需先以 <code class="text-[#c9a227]">BBDown serve</code> 启动服务端；默认回环地址免令牌。
        若要推送下载日志与选项交互，请以 <code class="text-[#c9a227]">--interactive</code> 启动。
      </p>
    </div>
  </div>
</template>
