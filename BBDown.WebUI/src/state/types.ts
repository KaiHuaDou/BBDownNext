import type { Ref } from 'vue'

import type { ServeConfig } from '../api/client'
import type { TaskOptions } from '../lib/options'
import type { TaskView } from '../lib/types'

/** 日志行。 */
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
 * 事件流（WebSocket）状态：serve 以 --no-interactive 关闭时降级为 disabled，
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
  submit: (
    options: TaskOptions,
    url: string,
    mode?: 'execute' | 'enqueue'
  ) => Promise<{ taskId: string; duplicate: boolean } | null>
  stop: (view: TaskView) => Promise<void>
  remove: (view: TaskView) => Promise<void>
  retry: (view: TaskView, fallback: TaskOptions) => Promise<void>
  start: (view: TaskView) => Promise<void>
  clearAll: () => Promise<void>
  clearFailed: () => Promise<void>
  answerAsk: (ask: PendingAsk, choice: string) => Promise<void>
  setConfig: (next: ServeConfig) => void
  exportLog: () => void
  appendLog: (text: string, isError?: boolean) => void
}
