import { ref, type Ref } from 'vue'

import { loadServeConfig, type ServeConfig } from '../api/client'
import type { TaskSocket } from '../api/ws'
import type { TaskOptions } from '../lib/options'
import type { TaskView } from '../lib/types'
import type { EventStreamState, LogLine, PendingAsk } from './types'

/** useTasks 的全部可变状态；以显式对象传递，使各处理函数均可置于模块级（无嵌套闭包）。 */
export interface TaskStore {
  config: Ref<ServeConfig>
  connected: Ref<boolean>
  connectionError: Ref<string | null>
  eventStream: Ref<EventStreamState>
  tasks: Ref<TaskView[]>
  logLines: Ref<LogLine[]>
  pendingAsks: Ref<PendingAsk[]>
  /** 提交时的选项快照：继续（重试）按钮据此重新提交（仅内存，不持久化）。 */
  submittedOptions: Map<string, TaskOptions>
  /** 已订阅（WS）的任务 id。 */
  subscribed: Set<string>
  /** 已应答过的选项请求 id → 所属任务 id。存任务 id 是为了能随任务结束一并摘除，否则长命标签页下无界增长。 */
  answeredAsks: Map<string, string>
  /**
   * 剩余时间外推的耗时基准：任务 id → 基准时刻（epoch 毫秒）。
   * 取墙钟耗时与 GUI 一致，同一任务两侧得出同一个数；随任务结束一并摘除
   */
  elapsedBase: Map<string, number>
  socket: TaskSocket | null
  /** 保活轮询定时器（60s 探 /healthz 感知 serve 存活），任务列表不依赖它。 */
  healthTimer: ReturnType<typeof setInterval> | null
}

export function createStore(): TaskStore {
  return {
    config: ref(loadServeConfig()),
    connected: ref(false),
    connectionError: ref<string | null>(null),
    eventStream: ref<EventStreamState>('connecting'),
    tasks: ref<TaskView[]>([]),
    logLines: ref<LogLine[]>([]),
    pendingAsks: ref<PendingAsk[]>([]),
    submittedOptions: new Map(),
    subscribed: new Set(),
    answeredAsks: new Map(),
    elapsedBase: new Map(),
    socket: null,
    healthTimer: null
  }
}
