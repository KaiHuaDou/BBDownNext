import { errorMessage } from '../lib/errors'
import type { ClientFrame, EventFrame, TaskSnapshot, WorkflowEvent } from '../lib/types'
import { resolveBaseUrl, type ServeConfig } from './client'

/**
 * 任务事件 WebSocket 通道（/hubs/tasks）：订阅任务后接收消息 / 进度快照 / 选项请求 / 全量列表（taskList），
 * 经 submitChoice 帧应答选项。握手令牌经 query 传（浏览器无法自定义请求头）。
 * 事件流始终启用（已移除 --no-interactive），任务列表与完成态均由推送驱动，无需轮询。
 */

/** 快照样本（ProgressSampleEvent 子集）。 */
export interface ProgressSnapshot {
  scope: string
  ratio: number
  totalBytes: number
  speed: number
  detail?: string
}

/** 保活 ping 间隔：服务端无事件推送时连接可能被中间层空闲回收。 */
const PingIntervalMs = 30000

export interface SocketHandlers {
  onEvent: (taskId: string, event: WorkflowEvent) => void
  onSnapshot: (taskId: string, snapshot: ProgressSnapshot) => void
  /** taskList 帧：全量任务列表（running + finished），用于免轮询刷新。 */
  onTaskList: (snapshot: TaskSnapshot) => void
  onChoiceResult: (requestId: string, ok: boolean, error?: string) => void
  /** 连接生命周期通知：null 表示已连接，非 null 为连接错误 / 断开信息。 */
  onStatus: (error: string | null) => void
  /** 订阅失败（任务不存在 / 事件流未启用），与连接生命周期无关。 */
  onSubscribeError: (error: string) => void
}

export interface TaskSocket {
  connect: () => void
  close: () => void
  subscribe: (taskId: string) => void
  unsubscribe: (taskId: string) => void
  submitChoice: (taskId: string, requestId: string, choice: string) => void
}

/** 连接状态机持有对象：以显式状态传递，使各处理函数为模块级（无嵌套闭包）。 */
interface SocketState {
  config: ServeConfig
  handlers: SocketHandlers
  socket: WebSocket | null
  closed: boolean
  retryDelay: number
  retryTimer: ReturnType<typeof setTimeout> | null
  pingTimer: ReturnType<typeof setInterval> | null
}

function stopPing(state: SocketState): void {
  if (state.pingTimer) {
    clearInterval(state.pingTimer)
    state.pingTimer = null
  }
}

function scheduleReconnect(state: SocketState): void {
  if (state.closed || state.retryTimer) {
    return
  }

  state.retryTimer = setTimeout(() => {
    state.retryTimer = null
    open(state)
  }, state.retryDelay)
  state.retryDelay = Math.min(state.retryDelay * 2, 15000)
}

function send(state: SocketState, frame: ClientFrame): void {
  if (state.socket?.readyState !== WebSocket.OPEN) {
    return
  }

  state.socket.send(JSON.stringify(frame))
}

function onOpen(state: SocketState): void {
  state.retryDelay = 1000
  state.handlers.onStatus(null)
  // 保活：服务端无事件推送时连接可能被中间层空闲回收，定期 ping
  stopPing(state)
  state.pingTimer = setInterval(() => send(state, { kind: 'ping' }), PingIntervalMs)
}

function onMessage(state: SocketState, message: MessageEvent): void {
  let frame: EventFrame
  try {
    frame = JSON.parse(message.data as string) as EventFrame
  } catch {
    return
  }

  switch (frame.kind) {
    case 'event': {
      if (frame.taskId && frame.event) {
        state.handlers.onEvent(frame.taskId, frame.event)
      }
      break
    }
    case 'snapshot': {
      if (frame.taskId && frame.snapshot) {
        state.handlers.onSnapshot(frame.taskId, frame.snapshot)
      }
      break
    }
    case 'choiceResult': {
      if (frame.requestId) {
        state.handlers.onChoiceResult(frame.requestId, frame.ok === true, frame.error)
      }
      break
    }
    case 'error': {
      state.handlers.onSubscribeError(frame.error ?? '未知错误')
      break
    }
    case 'taskList': {
      if (frame.tasks) {
        state.handlers.onTaskList(frame.tasks)
      }
      break
    }
  }
}

function onClose(state: SocketState): void {
  stopPing(state)
  if (!state.closed) {
    state.handlers.onStatus('事件通道已断开，重连中…')
    scheduleReconnect(state)
  }
}

function onError(state: SocketState): void {
  state.socket?.close()
}

function open(state: SocketState): void {
  if (state.closed) {
    return
  }

  try {
    state.socket = new WebSocket(toWsUrl(state.config))
  } catch (e) {
    state.handlers.onStatus(`WebSocket 连接失败：${errorMessage(e)}`)
    scheduleReconnect(state)
    return
  }

  state.socket.onopen = (): void => onOpen(state)
  state.socket.onmessage = (message): void => onMessage(state, message)
  state.socket.onclose = (): void => onClose(state)
  state.socket.onerror = (): void => onError(state)
}

function toWsUrl(config: ServeConfig): string {
  // 始终按 baseUrl（留空归一为本机 serve 默认地址）直连，不依赖 dev server 代理；
  // 鉴权令牌经 query 传（浏览器无法自定义握手头），仅建议回环或 TLS 场景使用
  const url = new URL(resolveBaseUrl(config.baseUrl))
  const protocol = url.protocol === 'https:' ? 'wss:' : 'ws:'
  const path = `${url.pathname.replace(/\/+$/, '')}/hubs/tasks`
  const token = config.token ? `?token=${encodeURIComponent(config.token)}` : ''
  return `${protocol}//${url.host}${path}${token}`
}

export function connectTaskSocket(config: ServeConfig, handlers: SocketHandlers): TaskSocket {
  const state: SocketState = {
    config,
    handlers,
    socket: null,
    closed: false,
    retryDelay: 1000,
    retryTimer: null,
    pingTimer: null
  }

  return {
    connect: () => open(state),
    close: () => {
      state.closed = true
      if (state.retryTimer) {
        clearTimeout(state.retryTimer)
        state.retryTimer = null
      }

      stopPing(state)
      state.socket?.close()
      state.socket = null
    },
    subscribe: (taskId) => send(state, { kind: 'subscribe', taskId }),
    unsubscribe: (taskId) => send(state, { kind: 'unsubscribe', taskId }),
    submitChoice: (taskId, requestId, choice) =>
      send(state, { kind: 'submitChoice', taskId, requestId, choice })
  }
}
