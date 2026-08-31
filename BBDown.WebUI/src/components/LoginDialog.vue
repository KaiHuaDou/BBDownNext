<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'

import { fetchLoginQr, pollLoginStatus, type QrLoginState, type ServeConfig } from '../api/client'
import { LOGIN_CHANNELS, saveCredential, type Credential, type LoginChannel } from '../api/login'

const props = defineProps<{
  config: ServeConfig
  credential: Credential
}>()

const emit = defineEmits<{
  close: []
  saved: [credential: Credential, channel?: LoginChannel]
}>()

const channel = ref<LoginChannel>('web')
const cookie = ref(props.credential.cookie)
const accessToken = ref(props.credential.accessToken)
const qrcodeKey = ref('')
const qrImage = ref('')
const qrStatus = ref<QrLoginState | 'generating' | null>(null)
const qrError = ref('')
const generating = ref(false)
let pollTimer: ReturnType<typeof setInterval> | null = null

const statusLabel = computed(() => {
  switch (qrStatus.value) {
    case 'generating': {
      return '正在生成二维码…'
    }
    case 'waitingScan': {
      return '等待扫码'
    }
    case 'waitingConfirm': {
      return '已扫码，请在手机上确认'
    }
    case 'expired': {
      return '二维码已过期'
    }
    case 'failed': {
      return qrError.value || '登录失败'
    }
    case 'success': {
      return '登录成功'
    }
    default: {
      return ''
    }
  }
})

const stopPolling = (): void => {
  if (pollTimer !== null) {
    clearInterval(pollTimer)
    pollTimer = null
  }
}

const startQr = async (): Promise<void> => {
  stopPolling()
  generating.value = true
  qrImage.value = ''
  qrcodeKey.value = ''
  qrStatus.value = 'generating'
  qrError.value = ''
  try {
    const result = await fetchLoginQr(props.config, channel.value)
    qrcodeKey.value = result.qrcodeKey
    qrImage.value = `data:image/png;base64,${result.qrPngBase64}`
    qrStatus.value = 'waitingScan'
    pollTimer = setInterval(() => void poll(), 1500)
  } catch (e) {
    qrStatus.value = 'failed'
    qrError.value = e instanceof Error ? e.message : String(e)
  } finally {
    generating.value = false
  }
}

const poll = async (): Promise<void> => {
  if (!qrcodeKey.value) {
    return
  }

  try {
    const result = await pollLoginStatus(props.config, qrcodeKey.value)
    switch (result.state) {
      case 'waitingScan': {
        return
      }
      case 'waitingConfirm': {
        qrStatus.value = 'waitingConfirm'
        return
      }
      case 'expired': {
        stopPolling()
        qrStatus.value = 'expired'
        return
      }
      case 'failed': {
        stopPolling()
        qrStatus.value = 'failed'
        qrError.value = result.error ?? '登录失败'
        return
      }
      case 'success': {
        // 仅明确的 success 才保存凭据并上报（App 侧据此联动 API 通道与关闭弹窗）；未知状态一律不打断
        stopPolling()
        qrStatus.value = 'success'
        const credential: Credential = {
          cookie: result.cookie ?? '',
          accessToken: result.accessToken ?? ''
        }
        saveCredential(credential)
        emit('saved', credential, channel.value)
        return
      }
      default: {
        // 未知 / 旧版服务的 state（如数字枚举）不当作成功，保持当前状态等下一周期
        return
      }
    }
  } catch {
    // 轮询瞬时失败（断线 / 服务端重启）不打断流程，下个周期重试
  }
}

const save = (): void => {
  const credential = { cookie: cookie.value.trim(), accessToken: accessToken.value.trim() }
  saveCredential(credential)
  emit('saved', credential)
}

const clear = (): void => {
  stopPolling()
  const credential = { cookie: '', accessToken: '' }
  saveCredential(credential)
  emit('saved', credential)
}

// 扫码途中切换通道：放弃当前二维码，为新通道重新生成
watch(channel, () => {
  if (qrcodeKey.value) {
    void startQr()
  }
})

onMounted(() => void startQr())
onBeforeUnmount(stopPolling)
</script>

<template>
  <div
    class="fixed inset-0 z-40 flex items-center justify-center bg-black/55 p-4 backdrop-blur-sm"
    @click.self="emit('close')">
    <div class="card max-h-[90vh] w-[26rem] max-w-full overflow-y-auto p-5">
      <div class="mb-4 text-center text-base font-semibold">登录</div>

      <label class="label mb-1.5">登录通道</label>
      <div class="mb-3 flex gap-1.5">
        <button
          v-for="item in LOGIN_CHANNELS"
          :key="item.value"
          class="btn-ghost flex-1"
          :class="{ '!border-[var(--accent)] !text-[var(--accent)]': channel === item.value }"
          type="button"
          @click="channel = item.value">
          {{ item.label }}
        </button>
      </div>

      <div
        class="mb-4 flex flex-col items-center gap-2 rounded-[var(--radius-sm)] border border-dashed border-[var(--hairline)] bg-[rgba(0,0,0,0.28)] p-4">
        <img
          v-if="qrImage"
          :src="qrImage"
          class="h-40 w-40 border border-[var(--hairline)] bg-white"
          alt="登录二维码" />
        <div v-else class="flex h-40 w-40 items-center justify-center text-center text-xs">
          <span class="text-[var(--text-dim)]">
            {{ generating ? '正在生成二维码…' : '二维码加载失败' }}
          </span>
        </div>
        <p class="h-4 text-xs text-[var(--text-dim)]">{{ statusLabel }}</p>
        <button
          v-if="qrStatus === 'expired' || qrStatus === 'failed'"
          class="btn-primary px-3 py-1 text-xs"
          type="button"
          @click="startQr">
          重新生成
        </button>
      </div>

      <label class="label mb-1.5">Cookie（WEB，SESSDATA）</label>
      <textarea v-model="cookie" class="field mb-3 h-20 resize-none font-mono" spellcheck="false" />

      <label class="label mb-1.5">Access Token（TV / APP）</label>
      <textarea
        v-model="accessToken"
        class="field mb-4 h-14 resize-none font-mono"
        spellcheck="false" />

      <div class="flex justify-center gap-3">
        <button class="btn-ghost" type="button" @click="clear">清除凭据</button>
        <button class="btn-ghost" type="button" @click="emit('close')">关闭</button>
        <button class="btn-primary" type="button" @click="save">保存凭据</button>
      </div>
      <p class="mt-3 text-xs leading-relaxed text-[var(--text-dim)]">
        扫码登录成功后凭据自动保存并同步到本机 BBDown.data；WEB 得到 Cookie，TV / APP 得到
        access_token，面板的 API 通道会自动切换为对应通道，也可在下方手工填写 / 修改凭据。
      </p>
    </div>
  </div>
</template>
