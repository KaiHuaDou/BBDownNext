import { fetchHealth } from '../api/client'
import { connectTaskSocket } from '../api/ws'
import { errorMessage } from '../lib/errors'
import { appendLog, applySnapshot, applySample, handleEvent } from './snapshot'
import type { TaskStore } from './store'

/**
 * 任务状态完全由 WebSocket 事件流订阅驱动，不再有任何任务列表轮询：
 * - taskList 帧（serve 结构变更时推送）提供全量任务列表，免轮询刷新；
 * - snapshot / event 帧提供运行期进度与日志；
 * serve 事件流始终启用（已移除 --no-interactive），故无 disabled 降级态。
 *
 * 仅保留「保活轮询」：每 60s 探一次 /healthz，仅用于感知 serve 存活（连接状态指示灯），
 * 不参与任务列表——任务列表与完成态一律由 WS 推送。
 */

/** 保活轮询间隔：仅探测 serve 存活，低频（60s）不影响实时性。 */
const HEALTH_INTERVAL_MS = 60000

/** 建立 / 重建事件流：清空订阅与挂起，按当前配置连接 WS。 */
export function startSocket(store: TaskStore): void {
  store.socket?.close()
  store.subscribed.clear()
  store.answeredAsks.clear()
  store.pendingAsks.value = []
  store.eventStream.value = 'connecting'
  store.socket = connectTaskSocket(store.config.value, {
    onEvent: (taskId, event) => handleEvent(store, taskId, event),
    onSnapshot: (taskId, snapshot) => applySample(store, taskId, snapshot),
    // taskList 帧：serve 结构变更（增删 / 状态切换 / 完成 / 清空）时推送的全量列表，免轮询刷新
    onTaskList: (snapshot) => applySnapshot(store, snapshot),
    onChoiceResult: (requestId, ok, error) => {
      if (!ok) {
        appendLog(store, `选项应答失败（${requestId}）：${error ?? '未知原因'}`, true)
      }
    },
    // 连接生命周期即事件流状态：null = 已连接（active）；非 null = 断开 / 重连中。
    // 注意：连接存活指示灯（connected）由保活轮询 probeHealth 负责，此处不改动，避免双写冲突。
    onStatus: (error) => {
      if (error) {
        if (store.eventStream.value !== 'reconnecting') {
          appendLog(store, error, true)
        }

        store.eventStream.value = 'reconnecting'
      } else {
        store.eventStream.value = 'active'
      }
    },
    // 订阅失败（任务不存在 / 已结束）：仅记录，不影响连接；任务状态仍由 taskList 帧推送
    onSubscribeError: (error) => {
      appendLog(store, `任务订阅失败：${error}`, true)
    }
  })
  store.socket.connect()
}

/** 保活轮询：仅探测 serve 存活，更新连接指示灯；不参与任务列表（任务由 WS 推送）。 */
export async function probeHealth(store: TaskStore): Promise<void> {
  try {
    await fetchHealth(store.config.value)
    store.connected.value = true
    store.connectionError.value = null
  } catch (e) {
    store.connected.value = false
    store.connectionError.value = errorMessage(e)
  }
}

/** 启动事件流与保活轮询。任务列表与完成态完全由 WS 订阅驱动，保活轮询仅用于存活探测。 */
export function startTimers(store: TaskStore): void {
  void probeHealth(store)
  store.healthTimer = setInterval(() => void probeHealth(store), HEALTH_INTERVAL_MS)
  startSocket(store)
}

/** 停止事件流与保活轮询。 */
export function stopTimers(store: TaskStore): void {
  if (store.healthTimer) {
    clearInterval(store.healthTimer)
    store.healthTimer = null
  }

  store.socket?.close()
}
