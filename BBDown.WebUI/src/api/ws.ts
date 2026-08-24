import { errorMessage } from '../lib/errors'
import type { ClientFrame, EventFrame, WorkflowEvent } from '../lib/types'
import type { ServeConfig } from './client'

/**
 * 任务事件 WebSocket 通道（/hubs/tasks）：订阅任务后接收消息 / 进度快照 / 选项请求，
 * 经 submitChoice 帧应答选项。握手令牌经 query 传（浏览器无法自定义请求头）。
 * 依赖 serve 以 --interactive 启动，否则订阅返回 error 帧。
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

export function connectTaskSocket(config: ServeConfig, handlers: SocketHandlers): TaskSocket {
  let socket: WebSocket | null = null
  let closed = false
  let retryDelay = 1000
  let retryTimer: ReturnType<typeof setTimeout> | null = null
  let pingTimer: ReturnType<typeof setInterval> | null = null

  const stopPing = (): void => {
    if (pingTimer) {
      clearInterval(pingTimer)
      pingTimer = null
    }
  }

  const open = (): void => {
    if (closed) {
      return
    }

    try {
      const wsUrl = toWsUrl(config)
      socket = new WebSocket(wsUrl)
    } catch (e) {
      handlers.onStatus(`WebSocket 连接失败：${errorMessage(e)}`)
      scheduleReconnect()
      return
    }

    socket.onopen = (): void => {
      retryDelay = 1000
      handlers.onStatus(null)
      // 保活：服务端无事件推送时连接可能被中间层空闲回收，定期 ping
      stopPing()
      pingTimer = setInterval(() => send({ kind: 'ping' }), PingIntervalMs)
    }

    socket.onmessage = (message: MessageEvent): void => {
      let frame: EventFrame
      try {
        frame = JSON.parse(message.data as string) as EventFrame
      } catch {
        return
      }

      switch (frame.kind) {
        case 'event': {
          if (frame.taskId && frame.event) {
            handlers.onEvent(frame.taskId, frame.event)
          }
          break
        }
        case 'snapshot': {
          if (frame.taskId && frame.snapshot) {
            handlers.onSnapshot(frame.taskId, frame.snapshot)
          }
          break
        }
        case 'choiceResult': {
          if (frame.requestId) {
            handlers.onChoiceResult(frame.requestId, frame.ok === true, frame.error)
          }
          break
        }
        case 'error': {
          handlers.onSubscribeError(frame.error ?? '未知错误')
          break
        }
      }
    }

    socket.onclose = (): void => {
      stopPing()
      if (!closed) {
        handlers.onStatus('事件通道已断开，重连中…')
        scheduleReconnect()
      }
    }

    socket.onerror = (): void => {
      socket?.close()
    }
  }

  const scheduleReconnect = (): void => {
    if (closed || retryTimer) {
      return
    }

    retryTimer = setTimeout(() => {
      retryTimer = null
      open()
    }, retryDelay)
    retryDelay = Math.min(retryDelay * 2, 15000)
  }

  const send = (frame: ClientFrame): void => {
    if (socket?.readyState !== WebSocket.OPEN) {
      return
    }

    socket.send(JSON.stringify(frame))
  }

  return {
    connect: open,
    close: () => {
      closed = true
      if (retryTimer) {
        clearTimeout(retryTimer)
        retryTimer = null
      }

      stopPing()
      socket?.close()
      socket = null
    },
    subscribe: (taskId) => send({ kind: 'subscribe', taskId }),
    unsubscribe: (taskId) => send({ kind: 'unsubscribe', taskId }),
    submitChoice: (taskId, requestId, choice) =>
      send({ kind: 'submitChoice', taskId, requestId, choice })
  }
}

function toWsUrl(config: ServeConfig): string {
  const url = new URL(config.baseUrl)
  const protocol = url.protocol === 'https:' ? 'wss:' : 'ws:'
  const path = `${url.pathname.replace(/\/+$/, '')}/hubs/tasks`
  const token = config.token ? `?token=${encodeURIComponent(config.token)}` : ''
  return `${protocol}//${url.host}${path}${token}`
}
