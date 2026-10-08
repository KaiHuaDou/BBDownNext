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
  /** 已发出应答帧，等待服务端 choiceResult 确认；确认前弹窗保留不消失。 */
  submitted?: boolean
}

/**
 * 事件流（WebSocket）状态：connecting 连接中 / active 已连接并推送 / reconnecting 断开重连中。
 * serve 事件流始终启用（已移除 --no-interactive），无 disabled 降级态。
 */
export type EventStreamState = 'connecting' | 'active' | 'reconnecting'
