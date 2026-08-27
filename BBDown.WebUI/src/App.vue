<script setup lang="ts">
import { computed, reactive, ref } from 'vue'

import { loadCredential, type Credential } from './api/login'
import AskDialog from './components/AskDialog.vue'
import ConnectionBar from './components/ConnectionBar.vue'
import LoginDialog from './components/LoginDialog.vue'
import LogPanel from './components/LogPanel.vue'
import OptionsPanel from './components/OptionsPanel.vue'
import TaskList from './components/TaskList.vue'
import { checkedFromContent, contentFromChecked, CONTENT_ORDER } from './lib/content'
import { DEFAULT_OPTIONS, type TaskOptions } from './lib/options'
import { describeInput, useTasks, type PendingAsk } from './state/useTasks'

const {
  config,
  connected,
  connectionError,
  eventStream,
  tasks,
  logLines,
  pendingAsks,
  submit,
  stop,
  remove,
  retry,
  clearAll,
  clearFailed,
  answerAsk,
  setConfig,
  exportLog,
  appendLog
} = useTasks()

const target = ref('')
let options = reactive<TaskOptions>({ ...DEFAULT_OPTIONS })
const loginVisible = ref(false)
const credential = ref<Credential>(loadCredential())

const targetHint = computed(() => describeInput(target.value))
const contentChecked = computed<Set<string>>({
  get: () => checkedFromContent(options.content),
  set: (next) => {
    options.content = contentFromChecked(next)
  }
})

const toggleContent = (ch: string, checked: boolean): void => {
  const next = new Set(contentChecked.value)
  if (checked) {
    next.add(ch)
  } else {
    next.delete(ch)
  }

  contentChecked.value = next
}

const statusCounts = computed(() => ({
  waiting: tasks.value.filter((t) => t.status === 'Waiting').length,
  running: tasks.value.filter((t) => t.status === 'Running').length,
  success: tasks.value.filter((t) => t.status === 'Success').length,
  failed: tasks.value.filter((t) => t.status === 'Failed').length,
  cancelled: tasks.value.filter((t) => t.status === 'Cancelled').length
}))

const loginStatusText = computed(() => {
  const hasCookie = credential.value.cookie.length > 0
  const hasToken = credential.value.accessToken.length > 0
  if (hasCookie && hasToken) {
    return '已配置 Cookie 与 access_token'
  }

  if (hasCookie) {
    return '已配置 WEB Cookie'
  }

  if (hasToken) {
    return '已配置 access_token'
  }

  return '未配置凭据'
})

const run = async (): Promise<void> => {
  const url = target.value.trim()
  if (url.length === 0) {
    appendLog('未填写下载目标')
    return
  }

  if (describeInput(url) === null) {
    appendLog('下载目标无法识别，未加入队列')
    return
  }

  const result = await submit(options, url)
  if (result) {
    appendLog(result.duplicate ? `任务已存在：${url}` : `任务已启动：${url}`)
  }
}

const enqueue = async (): Promise<void> => {
  const url = target.value.trim()
  if (url.length === 0) {
    appendLog('未填写下载目标')
    return
  }

  if (describeInput(url) === null) {
    appendLog('下载目标无法识别，未加入队列')
    return
  }

  await submit(options, url)
}

const reset = (): void => {
  Object.assign(options, DEFAULT_OPTIONS)
  appendLog('选项已重置')
}

const currentAsk = computed<PendingAsk | undefined>(() => pendingAsks.value[0])

const dismissAsk = (): void => {
  if (!currentAsk.value) {
    return
  }

  void answerAsk(
    currentAsk.value,
    currentAsk.value.defaultOptionId ?? currentAsk.value.options[0]?.id ?? ''
  )
}

const onSavedCredential = (next: Credential): void => {
  credential.value = next
  loginVisible.value = false
  appendLog('登录凭据已保存')
}
</script>

<template>
  <div class="flex h-screen flex-col bg-[#1e1e1e] p-5 text-[#eee]">
    <!-- 顶部连接栏 -->
    <div class="relative mb-2 flex items-center">
      <ConnectionBar
        :config="config"
        :connected="connected"
        :event-stream="eventStream"
        :error="connectionError"
        @save="setConfig" />
    </div>

    <div class="flex min-h-0 flex-1 gap-2.5">
      <!-- 左侧主区 -->
      <div class="flex min-w-0 flex-1 flex-col gap-2.5">
        <!-- 顶部：下载目标 -->
        <div>
          <div class="flex items-center gap-1.5">
            <span class="text-sm text-[#ddd]">目标</span>
            <input
              v-model="target"
              class="field-input flex-1"
              placeholder="粘贴链接，或输入 av / BV / live / opus 等号…" />
          </div>
          <div class="mt-2 text-sm" :class="targetHint ? 'text-[#4caf50]' : 'text-[#9e9e9e]'">
            {{ targetHint ? `✓ ${targetHint}` : '未能识别' }}
          </div>
        </div>

        <!-- 内容复选框：由 ContentSelector.Order 生成（字符/名称单一来源） -->
        <div
          class="grid grid-cols-3 gap-x-3 gap-y-1 rounded border border-[#3c3c3c] bg-[#252526] p-2.5"
          :class="{ 'pointer-events-none opacity-50': options.infoOnly }">
          <label v-for="item in CONTENT_ORDER" :key="item.ch" class="check-row">
            <input
              :checked="contentChecked.has(item.ch)"
              type="checkbox"
              @change="toggleContent(item.ch, ($event.target as HTMLInputElement).checked)" />
            {{ item.name }} (_{{ item.ch }})
          </label>
        </div>

        <!-- 中部滚动区：登录 + 选项 -->
        <div class="min-h-0 flex-1 overflow-y-auto pr-0.5">
          <div class="flex flex-col gap-1.5">
            <div class="flex items-center gap-2.5">
              <button class="btn-ghost" type="button" @click="loginVisible = true">登录</button>
              <span class="text-sm text-[#9e9e9e]">{{ loginStatusText }}</span>
            </div>
            <OptionsPanel
              v-model="options"
              :class="{ 'pointer-events-none opacity-50': options.infoOnly }" />
          </div>
        </div>

        <!-- 底部：操作按钮 -->
        <div class="flex gap-2.5">
          <button class="btn-action" type="button" @click="run">执行</button>
          <button class="btn-action" type="button" @click="enqueue">加入任务队列</button>
          <button class="btn-action" type="button" @click="reset">重置选项</button>
        </div>
      </div>

      <!-- 右侧边栏：任务队列 + 日志 -->
      <div class="flex w-80 min-w-0 shrink-0 flex-col gap-2.5">
        <div class="flex min-h-0 flex-1 flex-col gap-1.5">
          <div class="text-xs text-[#9e9e9e]">
            等待 {{ statusCounts.waiting }} · 运行 {{ statusCounts.running }} · 成功
            {{ statusCounts.success }} · 失败 {{ statusCounts.failed }} · 已取消
            {{ statusCounts.cancelled }}
          </div>
          <div class="min-h-0 flex-1 overflow-y-auto pr-0.5">
            <TaskList
              :tasks="tasks"
              @stop="(view) => void stop(view)"
              @cancel="(view) => void stop(view)"
              @retry="(view) => void retry(view, options)"
              @remove="(view) => void remove(view)" />
          </div>
        </div>
        <div class="flex items-center gap-2.5">
          <button class="btn-ghost" type="button" @click="clearAll">清空已完成</button>
          <button class="btn-ghost" type="button" @click="clearFailed">清空失败</button>
        </div>

        <!-- 日志区 -->
        <details open class="option-group">
          <summary class="option-header">日志</summary>
          <p
            v-if="eventStream === 'disabled'"
            class="border-b border-[#3c3c3c] px-3 py-1.5 text-xs leading-relaxed text-[#c9a227]">
            serve 未以 --interactive 启动：下载日志与选项交互不可用，任务状态与进度由轮询提供。
          </p>
          <LogPanel :log-lines="logLines" @export="exportLog" />
        </details>
      </div>
    </div>

    <!-- 弹窗 -->
    <LoginDialog
      v-if="loginVisible"
      :credential="credential"
      @close="loginVisible = false"
      @saved="onSavedCredential" />
    <AskDialog
      v-if="currentAsk"
      :ask="currentAsk"
      @answer="(choice) => currentAsk && void answerAsk(currentAsk, choice)"
      @dismiss="dismissAsk" />
  </div>
</template>
