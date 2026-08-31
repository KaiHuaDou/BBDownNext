<script setup lang="ts">
import { ref } from 'vue'

import { loadCredential, saveCredential, type Credential } from '../api/login'

defineProps<{
  credential: Credential
}>()

const emit = defineEmits<{
  close: []
  saved: [credential: Credential]
}>()

const cookie = ref(loadCredential().cookie)
const accessToken = ref(loadCredential().accessToken)

const save = (): void => {
  const credential = { cookie: cookie.value.trim(), accessToken: accessToken.value.trim() }
  saveCredential(credential)
  emit('saved', credential)
}
</script>

<template>
  <div
    class="fixed inset-0 z-40 flex items-center justify-center bg-black/55 p-4 backdrop-blur-sm"
    @click.self="emit('close')">
    <div class="card w-[26rem] max-w-full p-5">
      <div class="mb-4 text-center text-base font-semibold">登录</div>

      <!-- 二维码区：登录端点预留（serve 未实现），显示说明 -->
      <div
        class="mb-4 flex h-44 items-center justify-center rounded-[var(--radius-sm)] border border-dashed border-[var(--hairline)] bg-[rgba(0,0,0,0.28)] p-4 text-center">
        <p class="text-xs leading-relaxed text-[var(--text-dim)]">
          扫码登录端点尚未实现：serve 当前未提供登录接口，请在 CLI 或桌面 GUI
          完成登录。下方可配置凭据（Cookie / access_token），提交任务时随请求附带。
        </p>
      </div>

      <label class="label mb-1.5">Cookie（WEB，SESSDATA）</label>
      <textarea v-model="cookie" class="field mb-3 h-20 resize-none font-mono" spellcheck="false" />

      <label class="label mb-1.5">Access Token（TV / APP）</label>
      <textarea
        v-model="accessToken"
        class="field mb-4 h-14 resize-none font-mono"
        spellcheck="false" />

      <div class="flex justify-center gap-3">
        <button class="btn-ghost" type="button" @click="emit('close')">关闭</button>
        <button class="btn-primary" type="button" @click="save">保存凭据</button>
      </div>
    </div>
  </div>
</template>
