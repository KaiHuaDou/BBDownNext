<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'

import type { ServeConfig } from './api/client'
import { loadCredential, type Credential, type LoginChannel } from './api/login'
import AskDialog from './components/AskDialog.vue'
import ConnectionBar from './components/ConnectionBar.vue'
import LoginDialog from './components/LoginDialog.vue'
import LogPanel from './components/LogPanel.vue'
import OptionsPanel from './components/OptionsPanel.vue'
import ServeSettingsDialog from './components/ServeSettingsDialog.vue'
import TaskList from './components/TaskList.vue'
import { CONTENT_ORDER, checkedFromContent, contentFromChecked } from './lib/content'
import { DEFAULT_OPTIONS, loadOptions, saveOptions, type TaskOptions } from './lib/options'
import { describeTarget } from './lib/urlDetector'
import { useTasks, type PendingAsk } from './state/useTasks'

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
  start,
  clearAll,
  clearFailed,
  answerAsk,
  setConfig,
  exportLog,
  appendLog
} = useTasks()

const target = ref('')
let options = reactive<TaskOptions>(loadOptions())
const loginVisible = ref(false)
const serveSettingsVisible = ref(false)
const credential = ref<Credential>(loadCredential())
const submitting = ref(false)
// 选项变化即持久化，刷新后保留（凭据 / serve 配置各有独立存储，不经此键）
watch(options, () => saveOptions(options), { deep: true })

const targetHint = computed(() => describeTarget(target.value))
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
  pending: tasks.value.filter((t) => t.status === 'Pending').length,
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

const enqueue = async (mode: 'execute' | 'enqueue'): Promise<void> => {
  if (submitting.value) {
    return
  }

  const url = target.value.trim()
  if (url.length === 0) {
    appendLog('未填写下载目标')
    return
  }

  if (describeTarget(url) === null) {
    appendLog('下载目标无法识别，未加入队列')
    return
  }

  submitting.value = true
  try {
    await submit(options, url, mode)
  } finally {
    submitting.value = false
  }
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

const onSavedCredential = (next: Credential, channel?: LoginChannel): void => {
  credential.value = next
  loginVisible.value = false
  if (channel) {
    // 与 GUI 登录成功后自动切换 ApiBox 对齐：扫码通道即 API 通道
    options.api = channel
    appendLog(`已按登录通道切换 API 通道：${channel}`)
  }

  appendLog('登录凭据已保存')
}

const onSaveServeSettings = (next: ServeConfig): void => {
  setConfig(next)
  serveSettingsVisible.value = false
}
</script>

<template>
  <div class="flex h-screen flex-col text-[var(--text)]">
    <!-- 顶栏 -->
    <header
      class="flex h-10 shrink-0 items-center bg-[var(--glass)] px-3 backdrop-blur-[var(--blur)]">
      <span class="select-none text-sm font-semibold text-[var(--text)]"
        >BBDown<span class="text-[var(--accent)]">.WebUI</span></span
      >
      <div class="ml-auto flex items-center gap-2">
        <ConnectionBar
          :config="config"
          :connected="connected"
          :event-stream="eventStream"
          :error="connectionError"
          @settings="serveSettingsVisible = true" />
        <button class="btn-ghost" type="button" @click="reset">重置选项</button>
      </div>
    </header>

    <!-- 主体 -->
    <main class="flex min-h-0 flex-1 gap-2.5 p-2.5">
      <!-- 左侧工作区 -->
      <section class="flex min-w-0 flex-1 flex-col gap-2.5">
        <!-- 提交区 -->
        <div class="card card-pad">
          <div class="flex items-end gap-2.5">
            <div class="flex-1">
              <input
                id="target"
                v-model="target"
                class="field"
                placeholder="粘贴 B 站链接，或输入 av / BV / live / opus 等号…" />
            </div>
            <button
              class="btn-primary"
              type="button"
              :disabled="submitting"
              @click="enqueue('execute')">
              加入并执行
            </button>
            <button
              class="btn-subtle"
              type="button"
              :disabled="submitting"
              @click="enqueue('enqueue')">
              加入队列
            </button>
          </div>

          <div
            class="mt-1.5 text-xs"
            :class="targetHint ? 'text-[var(--st-success)]' : 'text-[var(--text-faint)]'">
            <template v-if="targetHint">✓ {{ targetHint }}</template>
            <template v-else>粘贴链接后将自动识别类型</template>
          </div>

          <!-- 内容选择（12 项，3 列紧凑布局） -->
          <div class="mt-2.5">
            <div
              class="grid grid-cols-3 gap-x-3 gap-y-1"
              :class="{ 'pointer-events-none opacity-50': options.infoOnly }">
              <label v-for="item in CONTENT_ORDER" :key="item.ch" class="check">
                <input
                  type="checkbox"
                  :checked="contentChecked.has(item.ch)"
                  @change="toggleContent(item.ch, ($event.target as HTMLInputElement).checked)" />
                {{ item.name }}
                <span class="text-[var(--text-faint)]">({{ item.ch }})</span>
              </label>
            </div>
          </div>
        </div>

        <!-- 登录 + 选项（整体滚动） -->
        <div class="card card-pad flex min-h-0 flex-1 flex-col overflow-y-auto">
          <div class="mb-1.5 flex shrink-0 items-center gap-2.5">
            <button class="btn-ghost" type="button" @click="loginVisible = true">登录</button>
            <span class="text-sm text-[var(--text-dim)]">{{ loginStatusText }}</span>
          </div>
          <OptionsPanel
            v-model="options"
            :class="{ 'pointer-events-none opacity-50': options.infoOnly }" />
        </div>
      </section>

      <!-- 右侧边栏 -->
      <aside class="flex w-[360px] shrink-0 flex-col gap-2.5">
        <!-- 任务队列 -->
        <div class="card flex min-h-0 flex-1 flex-col">
          <div class="flex items-center gap-2 px-3.5 py-2.5">
            <span class="text-sm font-semibold">任务队列</span>
            <div class="ml-auto flex flex-wrap items-center justify-end gap-1.5">
              <span class="stat"
                ><i class="stat-dot bg-[var(--st-running)]" />{{ statusCounts.running }}</span
              >
              <span class="stat"
                ><i class="stat-dot bg-[var(--st-success)]" />{{ statusCounts.success }}</span
              >
              <span class="stat"
                ><i class="stat-dot bg-[var(--st-failed)]" />{{ statusCounts.failed }}</span
              >
              <span class="stat"
                ><i class="stat-dot bg-[var(--st-cancelled)]" />{{ statusCounts.cancelled }}</span
              >
            </div>
          </div>
          <div class="min-h-0 flex-1 overflow-y-auto px-2.5 py-2.5">
            <TaskList
              :tasks="tasks"
              @stop="(view) => void stop(view)"
              @cancel="(view) => void stop(view)"
              @retry="(view) => void retry(view, options)"
              @start="(view) => void start(view)"
              @remove="(view) => void remove(view)" />
          </div>
          <div class="flex items-center gap-2 px-2.5 py-2">
            <button class="btn-ghost" type="button" @click="clearAll">清空已完成</button>
            <button class="btn-ghost" type="button" @click="clearFailed">清空失败</button>
          </div>
        </div>

        <!-- 日志 -->
        <details open class="expander">
          <summary class="exp-head">
            <span>日志</span>
            <button
              class="btn-ghost px-2.5 py-1 text-xs"
              type="button"
              @click.stop.prevent="exportLog">
              导出日志
            </button>
          </summary>
          <div class="p-1 pt-0">
            <LogPanel :log-lines="logLines" />
          </div>
        </details>
      </aside>
    </main>

    <!-- 弹窗 -->
    <LoginDialog
      v-if="loginVisible"
      :config="config"
      :credential="credential"
      @close="loginVisible = false"
      @saved="onSavedCredential" />
    <AskDialog
      v-if="currentAsk"
      :ask="currentAsk"
      @answer="(choice) => currentAsk && void answerAsk(currentAsk, choice)"
      @dismiss="dismissAsk" />
    <ServeSettingsDialog
      v-if="serveSettingsVisible"
      :config="config"
      @save="onSaveServeSettings"
      @close="serveSettingsVisible = false" />
  </div>
</template>
