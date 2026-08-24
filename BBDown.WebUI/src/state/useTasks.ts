import { computed, onUnmounted, ref, type ComputedRef, type Ref } from 'vue'

import {
  fetchHealth,
  fetchTasks,
  loadServeConfig,
  removeTask,
  stopTask,
  clearFinished,
  clearFailed as clearFailedRemote,
  submitTask,
  saveServeConfig,
  type ServeConfig
} from '../api/client'
import { connectTaskSocket, type TaskSocket } from '../api/ws'
import { errorMessage } from '../lib/errors'
import { buildDetail } from '../lib/format'
import { toServeRequest, type TaskOptions } from '../lib/options'
import type {
  DownloadTask,
  TaskSnapshot,
  TaskView,
  TaskViewStatus,
  WorkflowEvent
} from '../lib/types'
import { describeTarget } from '../lib/urlDetector'

const POLL_INTERVAL_MS = 2000
const MAX_LOG_LINES = 5000

export interface LogLine {
  text: string
  isError: boolean
}

/** 挂起的选项请求（逐集确认 / 选轨等），由 UI 弹窗应答。 */
export interface PendingAsk {
  requestId: string
  taskId: string
  prompt: string
  options: { id: string; label: string }[]
  defaultOptionId?: string
}

/**
 * 事件流（WebSocket）状态：serve 未开 --interactive 时降级为 disabled，
 * 任务列表与进度仍由 REST 轮询提供，日志与选项交互不可用。
 */
export type EventStreamState = 'connecting' | 'active' | 'disabled' | 'reconnecting'

/** useTasks 返回契约：状态引用 + 任务操作。 */
export interface TasksState {
  config: Ref<ServeConfig>
  connected: Ref<boolean>
  connectionError: Ref<string | null>
  eventStream: Ref<EventStreamState>
  tasks: Ref<TaskView[]>
  logLines: Ref<LogLine[]>
  pendingAsks: Ref<PendingAsk[]>
  runningCount: ComputedRef<number>
  submit: (
    options: TaskOptions,
    url: string
  ) => Promise<{ taskId: string; duplicate: boolean } | null>
  stop: (view: TaskView) => Promise<void>
  remove: (view: TaskView) => Promise<void>
  retry: (view: TaskView, fallback: TaskOptions) => Promise<void>
  clearAll: () => Promise<void>
  clearFailed: () => Promise<void>
  answerAsk: (ask: PendingAsk, choice: string) => Promise<void>
  setConfig: (next: ServeConfig) => void
  exportLog: () => void
  appendLog: (text: string, isError?: boolean) => void
}

function statusOf(task: DownloadTask): { status: TaskViewStatus; statusText: string } {
  if (task.status === 'Queued') {
    return { status: 'Waiting', statusText: '等待中' }
  }

  if (task.status === 'Running') {
    return { status: 'Running', statusText: '运行中' }
  }

  if (task.isSuccessful) {
    return { status: 'Success', statusText: '成功' }
  }

  // serve 取消的任务收尾为 Finished + 失败，错误文案含「已取消」
  if (task.errorMessage?.includes('已取消')) {
    return { status: 'Cancelled', statusText: '已取消' }
  }

  return { status: 'Failed', statusText: '失败' }
}

function toView(task: DownloadTask, retryOptions?: TaskOptions): TaskView {
  const { status, statusText } = statusOf(task)
  const isLive = /live\.bilibili\.com|^live\d+/i.test(task.url)
  const detail =
    status === 'Running' && task.progress > 0
      ? buildDetail(task.progress, task.downloadSpeed, task.totalDownloadedBytes)
      : ''
  return {
    id: task.id,
    url: task.url,
    title: task.title ?? undefined,
    status,
    statusText,
    progress: task.progress,
    detail,
    errorMessage: task.errorMessage ?? undefined,
    savePaths: task.savePaths,
    isLive,
    retryOptions
  }
}

/** 任务列表 + 日志 + 连接状态；轮询快照与 WebSocket 事件流在此合并。 */
export function useTasks(): TasksState {
  const config = ref<ServeConfig>(loadServeConfig())
  const connected = ref(false)
  const connectionError = ref<string | null>(null)
  const eventStream = ref<EventStreamState>('connecting')
  const tasks = ref<TaskView[]>([])
  const logLines = ref<LogLine[]>([])
  const pendingAsks = ref<PendingAsk[]>([])

  // 提交时的选项快照：继续（重试）按钮据此重新提交
  const submittedOptions = new Map<string, TaskOptions>()
  // 订阅中的任务 id（WS）
  const subscribed = new Set<string>()
  // 应答过的选项请求 id，防止重复弹出
  const answeredAsks = new Set<string>()

  let socket: TaskSocket | null = null
  let pollTimer: ReturnType<typeof setInterval> | null = null
  let healthTimer: ReturnType<typeof setInterval> | null = null

  const runningCount = computed(() => tasks.value.filter((t) => t.status === 'Running').length)

  const appendLog = (text: string, isError = false): void => {
    logLines.value.push({ text, isError })
    if (logLines.value.length > MAX_LOG_LINES) {
      logLines.value.splice(0, logLines.value.length - MAX_LOG_LINES)
    }
  }

  const handleEvent = (taskId: string, event: WorkflowEvent): void => {
    switch (event.type) {
      case 'message': {
        appendLog(`[任务${taskId}] ${event.text}`)
        break
      }
      case 'progressStart': {
        // 阶段边界：快照样本到达前无需动作，进度条由快照驱动
        break
      }
      case 'progressEnd': {
        break
      }
      case 'progressSample': {
        applySample(taskId, event)
        break
      }
      case 'optionRequest': {
        if (!answeredAsks.has(event.requestId)) {
          pendingAsks.value.push({
            requestId: event.requestId,
            taskId: event.scope,
            prompt: event.prompt,
            options: event.options,
            defaultOptionId: event.defaultOptionId
          })
        }

        break
      }
    }
  }

  const applySample = (
    taskId: string,
    sample: { ratio: number; totalBytes: number; speed: number; detail?: string }
  ): void => {
    const view = tasks.value.find((t) => t.id === taskId)
    if (!view || view.status !== 'Running') {
      return
    }

    view.progress = Math.min(Math.max(sample.ratio, 0), 1)
    view.detail = buildDetail(sample.ratio, sample.speed, sample.totalBytes, sample.detail)
  }

  const syncSubscriptions = (running: string[]): void => {
    if (!socket) {
      return
    }

    for (const id of running) {
      if (!subscribed.has(id)) {
        subscribed.add(id)
        socket.subscribe(id)
      }
    }

    for (const id of subscribed) {
      if (!running.includes(id)) {
        subscribed.delete(id)
        socket.unsubscribe(id)
      }
    }
  }

  const applySnapshot = (snapshot: TaskSnapshot): void => {
    const running = snapshot.running.map((t) => t.id)
    const views = [
      ...snapshot.running.map((t) => toView(t, submittedOptions.get(t.id))),
      ...snapshot.finished.map((t) => toView(t, submittedOptions.get(t.id)))
    ]
    // 保留快照不覆盖的运行中 detail（快照无阶段文本，仅 WS 有）
    for (const prev of tasks.value) {
      if (prev.status === 'Running' && prev.detail) {
        const next = views.find((v) => v.id === prev.id)
        if (next && next.status === 'Running') {
          next.detail = prev.detail
        }
      }
    }

    tasks.value = views
    syncSubscriptions(running)
  }

  const poll = async (): Promise<void> => {
    try {
      const snapshot = await fetchTasks(config.value)
      connected.value = true
      connectionError.value = null
      applySnapshot(snapshot)
    } catch (e) {
      connected.value = false
      connectionError.value = errorMessage(e)
    }
  }

  const probeHealth = async (): Promise<void> => {
    try {
      await fetchHealth(config.value)
      connected.value = true
      connectionError.value = null
    } catch (e) {
      connected.value = false
      connectionError.value = errorMessage(e)
    }
  }

  const startSocket = (): void => {
    socket?.close()
    subscribed.clear()
    answeredAsks.clear()
    pendingAsks.value = []
    eventStream.value = 'connecting'
    socket = connectTaskSocket(config.value, {
      onEvent: handleEvent,
      onSnapshot: (taskId, snapshot) => applySample(taskId, snapshot),
      onChoiceResult: (requestId, ok, error) => {
        if (!ok) {
          appendLog(`选项应答失败（${requestId}）：${error ?? '未知原因'}`, true)
        }
      },
      // 连接生命周期与 REST 连接状态分离：WS 异常不污染 connectionError（healthz 轮询会覆盖）
      onStatus: (error) => {
        if (error) {
          eventStream.value = 'reconnecting'
          appendLog(error, true)
        } else {
          eventStream.value = 'active'
        }
      },
      // 订阅失败：事件流未启用（serve 未开 --interactive）时降级为禁用，任务状态仍由轮询提供
      onSubscribeError: (error) => {
        if (error.includes('未启用交互')) {
          eventStream.value = 'disabled'
          socket?.close()
        } else {
          appendLog(`任务订阅失败：${error}`, true)
        }
      }
    })
    socket.connect()
  }

  const start = (): void => {
    void probeHealth()
    void poll()
    pollTimer = setInterval(() => void poll(), POLL_INTERVAL_MS)
    healthTimer = setInterval(() => void probeHealth(), 10000)
    startSocket()
  }

  /** 提交任务；mode 决定返回提示差异。返回 null 表示失败。 */
  const submit = async (
    options: TaskOptions,
    url: string
  ): Promise<{ taskId: string; duplicate: boolean } | null> => {
    const request = toServeRequest(options, url, loadCredentialForSubmit())
    try {
      const { task, duplicate } = await submitTask(config.value, request)
      submittedOptions.set(task.id, options)
      appendLog(duplicate ? `任务已存在：${url}` : `任务已受理：${url}`)
      void poll()
      return { taskId: task.id, duplicate }
    } catch (e) {
      appendLog(`任务提交失败：${errorMessage(e)}`, true)
      return null
    }
  }

  // 与 api/login.ts 的存储键保持一致（避免循环依赖，直接读 localStorage）
  const loadCredentialForSubmit = (): { cookie: string; accessToken: string } => ({
    cookie: localStorage.getItem('bbdown.credential.cookie') ?? '',
    accessToken: localStorage.getItem('bbdown.credential.accessToken') ?? ''
  })

  const stop = async (view: TaskView): Promise<void> => {
    try {
      await stopTask(config.value, view.id)
      appendLog(`任务${view.id} 已请求取消`)
    } catch (e) {
      appendLog(`取消失败：${errorMessage(e)}`, true)
    }
  }

  /** 移除已完成任务；运行中的任务需先取消。 */
  const remove = async (view: TaskView): Promise<void> => {
    if (view.status === 'Running') {
      appendLog('运行中的任务请先取消')
      return
    }

    try {
      await removeTask(config.value, view.id)
      tasks.value = tasks.value.filter((t) => t.id !== view.id)
    } catch (e) {
      appendLog(`移除失败：${errorMessage(e)}`, true)
    }
  }

  /** 继续：用提交时的选项快照重新提交；无快照时回落当前面板选项。 */
  const retry = async (view: TaskView, fallback: TaskOptions): Promise<void> => {
    await submit(submittedOptions.get(view.id) ?? fallback, view.url)
  }

  const clearAll = async (): Promise<void> => {
    try {
      await clearFinished(config.value)
      tasks.value = tasks.value.filter((t) => t.status === 'Running' || t.status === 'Waiting')
    } catch (e) {
      appendLog(`清空失败：${errorMessage(e)}`, true)
    }
  }

  const clearFailed = async (): Promise<void> => {
    try {
      await clearFailedRemote(config.value)
      tasks.value = tasks.value.filter((t) => t.status !== 'Failed')
    } catch (e) {
      appendLog(`清空失败：${errorMessage(e)}`, true)
    }
  }

  /** 应答选项请求；应答后从挂起列表移除。 */
  const answerAsk = async (ask: PendingAsk, choice: string): Promise<void> => {
    socket?.submitChoice(ask.taskId, ask.requestId, choice)
    answeredAsks.add(ask.requestId)
    pendingAsks.value = pendingAsks.value.filter((a) => a.requestId !== ask.requestId)
  }

  const setConfig = (next: ServeConfig): void => {
    config.value = next
    saveServeConfig(next)
    startSocket()
    void probeHealth()
    void poll()
  }

  const exportLog = (): void => {
    const blob = new Blob([logLines.value.map((line) => line.text).join('\n')], {
      type: 'text/plain'
    })
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = `BBDown.WebUI.log.${new Date().toISOString().replace(/[:.]/g, '')}.txt`
    anchor.click()
    URL.revokeObjectURL(url)
  }

  start()

  onUnmounted(() => {
    if (pollTimer) {
      clearInterval(pollTimer)
    }

    if (healthTimer) {
      clearInterval(healthTimer)
    }

    socket?.close()
  })

  return {
    config,
    connected,
    connectionError,
    eventStream,
    tasks,
    logLines,
    pendingAsks,
    runningCount,
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
  }
}

/** 目标识别提示（供顶部输入框使用）。 */
export function describeInput(input: string): string | null {
  return describeTarget(input)
}
