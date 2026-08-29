import { fetchHealth, fetchTasks } from '../api/client'
import { connectTaskSocket } from '../api/ws'
import { errorMessage } from '../lib/errors'
import { appendLog, applySnapshot, applySample, handleEvent } from './snapshot'
import type { TaskStore } from './store'

const POLL_INTERVAL_MS = 2000
const HEALTH_INTERVAL_MS = 10000

/**
 * 订阅失败文案哨兵：serve 以 --no-interactive 关闭时订阅返回该文案，据此降级为 disabled。
 * 与 serve TasksSocket 的 error 帧文案契约绑定，serve 改文案须同步此处。
 */
const INTERACTIVE_DISABLED_MARKER = '未启用交互'

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
    onChoiceResult: (requestId, ok, error) => {
      if (!ok) {
        appendLog(store, `选项应答失败（${requestId}）：${error ?? '未知原因'}`, true)
      }
    },
    // 连接生命周期与 REST 连接状态分离：WS 异常不污染 connectionError（healthz 轮询会覆盖）
    onStatus: (error) => {
      if (error) {
        // 重连期间反复断开只在首次记录，避免日志区刷屏
        if (store.eventStream.value !== 'reconnecting') {
          appendLog(store, error, true)
        }

        store.eventStream.value = 'reconnecting'
      } else {
        store.eventStream.value = 'active'
      }
    },
    // 订阅失败：事件流未启用时降级为禁用并停止重连，任务状态仍由轮询提供
    onSubscribeError: (error) => {
      if (error.includes(INTERACTIVE_DISABLED_MARKER)) {
        store.eventStream.value = 'disabled'
        store.socket?.disable()
      } else {
        appendLog(store, `任务订阅失败：${error}`, true)
      }
    }
  })
  store.socket.connect()
}

/** 健康检查：连接状态 + 读 interactive 开关决定事件流启用。 */
export async function probeHealth(store: TaskStore): Promise<void> {
  try {
    const health = await fetchHealth(store.config.value)
    store.connected.value = true
    store.connectionError.value = null
    // healthz 暴露事件流开关：--no-interactive 关闭时无需保持 WS 连接（订阅探测依赖有运行任务，空列表时不可靠）；
    // 旧版 serve 无该字段（undefined）时回退订阅探测行为
    if (health.interactive === false) {
      store.eventStream.value = 'disabled'
      store.socket?.close()
    } else if (store.eventStream.value === 'disabled') {
      // serve 重启且未以 --no-interactive 关闭后自动重建事件流，无需手动改配置
      startSocket(store)
    }
  } catch (e) {
    store.connected.value = false
    store.connectionError.value = errorMessage(e)
  }
}

/** REST 轮询：拉全量快照并合并。 */
export async function poll(store: TaskStore): Promise<void> {
  try {
    const snapshot = await fetchTasks(store.config.value)
    store.connected.value = true
    store.connectionError.value = null
    applySnapshot(store, snapshot)
  } catch (e) {
    store.connected.value = false
    store.connectionError.value = errorMessage(e)
  }
}

/** 启动轮询与事件流定时器。 */
export function startTimers(store: TaskStore): void {
  void probeHealth(store)
  void poll(store)
  store.pollTimer = setInterval(() => void poll(store), POLL_INTERVAL_MS)
  store.healthTimer = setInterval(() => void probeHealth(store), HEALTH_INTERVAL_MS)
  startSocket(store)
}

/** 停止全部定时器并关闭事件流。 */
export function stopTimers(store: TaskStore): void {
  if (store.pollTimer) {
    clearInterval(store.pollTimer)
  }

  if (store.healthTimer) {
    clearInterval(store.healthTimer)
  }

  store.socket?.close()
}
