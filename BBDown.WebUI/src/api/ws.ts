import { errorMessage } from '../lib/errors'
import type { ClientFrame, EventFrame, TaskSnapshot, WorkflowEvent } from '../lib/types'
import { resolveBaseUrl, type ServeConfig } from './client'

/**
 * 任务事件 WebSocket 通道（/hubs/tasks）：订阅任务后接收消息 / 进度快照 / 选项请求 / 全量列表（taskList），
 * 经 submitChoice 帧应答选项。握手令牌经子协议头传（浏览器无法自定义请求头；URL 会进日志与历史）。
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
  /** 连接建立（含重连）时回调：服务端侧的订阅表随旧连接一同丢弃，调用方须在此重发订阅。 */
  onOpen: () => void
  /** 订阅失败（任务不存在 / 事件流未启用），与连接生命周期无关。 */
  onSubscribeError: (error: string) => void
  /** 收到无法解析的帧（协议不匹配 / 代理改写）。连接本身仍正常，故不并入 onStatus。 */
  onFrameDropped: (reason: string) => void
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
  /** 连续丢弃的帧数：解析失败时累加，成功解析后归零，用于把丢弃量带上报给用户</summary> */
  droppedFrames: number
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
  // 重连得到的是新 socket，服务端订阅表随旧连接一同丢弃，故此处通知调用方重发订阅
  state.handlers.onOpen()
  // 保活：服务端无事件推送时连接可能被中间层空闲回收，定期 ping
  stopPing(state)
  state.pingTimer = setInterval(() => send(state, { kind: 'ping' }), PingIntervalMs)
}

/**
 * 解析一帧。返回 null 表示这一帧无法理解，已计入丢弃计数并上报：
 * 二进制帧、反向代理改写协议、serve 与前端版本不匹配都会走到这里，静默丢帧会让进度与提问无征兆停摆
 */
function parseFrame(state: SocketState, data: unknown): EventFrame | null {
  const fail = (reason: string): null => {
    state.droppedFrames += 1
    state.handlers.onFrameDropped(`${reason}，已丢弃 ${state.droppedFrames} 帧`)
    return null
  }

  if (typeof data !== 'string') {
    return fail(`收到非文本帧（${typeof data}）`)
  }

  let frame: EventFrame
  try {
    frame = JSON.parse(data) as EventFrame
  } catch {
    return fail('事件帧不是合法 JSON')
  }

  if (typeof frame.kind !== 'string') {
    return fail('事件帧缺少 kind 字段')
  }

  // 一帧解析成功即认为链路恢复正常，计数归零，后续再坏只报当次的增量
  state.droppedFrames = 0
  return frame
}

function onMessage(state: SocketState, message: MessageEvent): void {
  const frame = parseFrame(state, message.data)
  if (frame === null) {
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
    // 令牌作子协议名传：浏览器会把所选子协议回显校验，非法子协议名会在此抛 SyntaxError
    const token = state.config.token
    state.socket = new WebSocket(toWsUrl(state.config), token ? [token] : undefined)
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
  // 始终按 baseUrl（留空归一为本机 serve 默认地址）直连，不依赖 dev server 代理
  const url = new URL(resolveBaseUrl(config.baseUrl))
  const protocol = url.protocol === 'https:' ? 'wss:' : 'ws:'
  const path = `${url.pathname.replace(/\/+$/, '')}/hubs/tasks`
  return `${protocol}//${url.host}${path}`
}

export function connectTaskSocket(config: ServeConfig, handlers: SocketHandlers): TaskSocket {
  const state: SocketState = {
    config,
    handlers,
    socket: null,
    closed: false,
    retryDelay: 1000,
    retryTimer: null,
    pingTimer: null,
    droppedFrames: 0
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
