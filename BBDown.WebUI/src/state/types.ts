import type { Ref } from 'vue'

import type { ServeConfig } from '../api/client'
import type { TaskOptions } from '../lib/options'
import type { TaskView } from '../lib/types'

/** 日志行。 */
export interface LogLine {
  text: string
  isError: boolean
}

/** 挂起的选项请求（逐集确认 / 选轨等），由 UI 弹窗应答。deadline 为服务端 AskBus 超时（ISO），到点本地回落。 */
export interface PendingAsk {
  requestId: string
  taskId: string
  prompt: string
  options: { id: string; label: string }[]
  defaultOptionId?: string
  deadline: string
}

/**
 * 事件流（WebSocket）状态：connecting 连接中 / active 已连接并推送 / reconnecting 断开重连中。
 * serve 事件流始终启用（已移除 --no-interactive），不再有 disabled 降级态。
 */
export type EventStreamState = 'connecting' | 'active' | 'reconnecting'

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
