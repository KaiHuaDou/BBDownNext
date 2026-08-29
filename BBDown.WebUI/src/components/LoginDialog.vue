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
    class="fixed inset-0 z-40 flex items-center justify-center bg-black/60"
    @click.self="emit('close')">
    <div class="flex w-96 flex-col gap-3 rounded border border-[#3c3c3c] bg-[#252526] p-5">
      <div class="text-center text-sm font-semibold text-[#eee]">登录</div>

      <!-- 二维码区：登录端点预留（serve 未实现），显示说明 -->
      <div
        class="flex h-60 items-center justify-center rounded border border-dashed border-[#3c3c3c] bg-[#1a1a1c] p-4 text-center">
        <p class="text-xs leading-relaxed text-[#9e9e9e]">
          扫码登录端点尚未实现：serve 当前未提供登录接口，请在 CLI 或桌面 GUI
          完成登录。下方可配置凭据（Cookie / access_token），提交任务时随请求附带。
        </p>
      </div>

      <label class="flex flex-col gap-1 text-xs text-[#9e9e9e]">
        Cookie（WEB，SESSDATA）
        <textarea
          v-model="cookie"
          class="field-input h-16 resize-none font-mono"
          spellcheck="false" />
      </label>
      <label class="flex flex-col gap-1 text-xs text-[#9e9e9e]">
        Access Token（TV / APP）
        <textarea
          v-model="accessToken"
          class="field-input h-10 resize-none font-mono"
          spellcheck="false" />
      </label>

      <div class="mt-1 flex justify-center gap-3">
        <button class="btn-ghost" type="button" @click="emit('close')">关闭</button>
        <button class="btn-action" type="button" @click="save">保存凭据</button>
      </div>
    </div>
  </div>
</template>
