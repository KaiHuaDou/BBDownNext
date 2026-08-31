<script setup lang="ts">
import { ref } from 'vue'

import type { ServeConfig } from '../api/client'

const props = defineProps<{
  config: ServeConfig
}>()

const emit = defineEmits<{
  save: [config: ServeConfig]
  close: []
}>()

const baseUrl = ref(props.config.baseUrl)
const token = ref(props.config.token)

const save = (): void => {
  emit('save', { baseUrl: baseUrl.value.trim(), token: token.value.trim() })
}
</script>

<template>
  <div
    class="fixed inset-0 z-40 flex items-center justify-center bg-black/55 p-4 backdrop-blur-sm"
    @click.self="emit('close')">
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
        <button class="btn-ghost" type="button" @click="emit('close')">取消</button>
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
</template>
