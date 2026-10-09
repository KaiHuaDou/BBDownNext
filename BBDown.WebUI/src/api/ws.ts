import { errorMessage } from '../lib/errors'
import type {
  ClientFrame,
  EventFrame,
  ProgressSample,
  TaskSnapshot,
  WorkflowEvent
} from '../lib/types'
import { resolveBaseUrl, type ServeConfig } from './client'

/**
 * 任务事件 WebSocket 通道（/hubs/tasks）：订阅任务后接收消息 / 进度快照 / 选项请求 / 全量列表（taskList），
 * 经 submitChoice 帧应答选项。握手令牌经子协议头传（浏览器无法自定义请求头；URL 会进日志与历史）。
 * 事件流始终启用（已移除 --no-interactive），任务列表与完成态均由推送驱动，无需轮询。
 */

/** 保活 ping 间隔：服务端无事件推送时连接可能被中间层空闲回收。 */
const PingIntervalMs = 30000

/** 按 kind 取帧成员：形状校验后据此收窄，各处理点不会见到可能缺失的字段。 */
type FrameOf<K extends EventFrame['kind']> = Extract<EventFrame, { kind: K }>

/** 待校验的帧：已确认是带 kind 的 JSON 对象，字段形状未校验。 */
type SourceFrame = Record<string, unknown>

export interface SocketHandlers {
  onEvent: (taskId: string, event: WorkflowEvent) => void
  onSnapshot: (taskId: string, snapshot: ProgressSample) => void
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

/** JSON 对象判定：数组与 null 也满足 typeof object，须一并排除。 */
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/** 进度样本的字段形状：数值直接参与剩余时间外推，缺一项即无法还原。 */
function isProgressSample(value: unknown): value is ProgressSample {
  return (
    isRecord(value) &&
    typeof value.scope === 'string' &&
    typeof value.ratio === 'number' &&
    typeof value.totalBytes === 'number' &&
    typeof value.speed === 'number'
  )
}

/** 以下形状校验只认各帧成员声明的必填字段：缺项说明帧来自别的协议版本，整帧丢弃而非部分应用。 */
function isEventFrame(frame: SourceFrame): frame is FrameOf<'event'> {
  return typeof frame.taskId === 'string' && isRecord(frame.event)
}

function isSnapshotFrame(frame: SourceFrame): frame is FrameOf<'snapshot'> {
  return typeof frame.taskId === 'string' && isProgressSample(frame.snapshot)
}

function isChoiceResultFrame(frame: SourceFrame): frame is FrameOf<'choiceResult'> {
  return typeof frame.requestId === 'string'
}

function isTaskListFrame(frame: SourceFrame): frame is FrameOf<'taskList'> {
  return (
    isRecord(frame.tasks) && Array.isArray(frame.tasks.running) && Array.isArray(frame.tasks.finished)
  )
}

/** 丢弃一帧并上报，返回 null 便于调用点直接返回。 */
function dropFrame(state: SocketState, reason: string): null {
  state.droppedFrames += 1
  state.handlers.onFrameDropped(`${reason}，已丢弃 ${state.droppedFrames} 帧`)
  return null
}

/**
 * 解析一帧为待校验的帧对象，只取出 JSON 与 kind；字段形状交由 onMessage 按帧类型判定。
 * 返回 null 表示这一帧无法理解，已计入丢弃计数并上报：
 * 二进制帧、反向代理改写协议、serve 与前端版本不匹配都会走到这里，静默丢帧会让进度与提问无征兆停摆
 */
function parseFrame(state: SocketState, data: unknown): SourceFrame | null {
  if (typeof data !== 'string') {
    return dropFrame(state, `收到非文本帧（${typeof data}）`)
  }

  let parsed: unknown
  try {
    parsed = JSON.parse(data)
  } catch {
    return dropFrame(state, '事件帧不是合法 JSON')
  }

  if (!isRecord(parsed)) {
    return dropFrame(state, '事件帧不是 JSON 对象')
  }

  if (typeof parsed.kind !== 'string') {
    return dropFrame(state, '事件帧缺少 kind 字段')
  }

  return parsed
}

function onMessage(state: SocketState, message: MessageEvent): void {
  const frame = parseFrame(state, message.data)
  if (frame === null) {
    return
  }

  switch (frame.kind) {
    case 'event': {
      if (!isEventFrame(frame)) {
        dropFrame(state, 'event 帧缺少 taskId 或 event 字段')
        return
      }

      state.handlers.onEvent(frame.taskId, frame.event)
      break
    }
    case 'snapshot': {
      if (!isSnapshotFrame(frame)) {
        dropFrame(state, 'snapshot 帧缺少 taskId 或 snapshot 字段')
        return
      }

      state.handlers.onSnapshot(frame.taskId, frame.snapshot)
      break
    }
    case 'choiceResult': {
      if (!isChoiceResultFrame(frame)) {
        dropFrame(state, 'choiceResult 帧缺少 requestId 字段')
        return
      }

      state.handlers.onChoiceResult(frame.requestId, frame.ok === true, frame.error)
      break
    }
    case 'error': {
      // error 帧只带可选的 error 字段，缺失时给默认文案而非丢弃
      const text = typeof frame.error === 'string' ? frame.error : '未知错误'
      state.handlers.onSubscribeError(text)
      break
    }
    case 'taskList': {
      if (!isTaskListFrame(frame)) {
        dropFrame(state, 'taskList 帧缺少 tasks 字段')
        return
      }

      state.handlers.onTaskList(frame.tasks)
      break
    }
    default: {
      // 服务端新增的帧种类在此显式暴露，便于发现版本不匹配
      dropFrame(state, `未知帧类型（${frame.kind}）`)
      return
    }
  }

  // 完整处理一帧即认为链路恢复正常，计数归零，后续再坏只报当次的增量
  state.droppedFrames = 0
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
