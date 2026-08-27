/** serve 契约类型：与 BBDown.Serve 的 DownloadTask / ServeRequestOptions / WorkflowEvent 序列化格式对齐。 */

/** 任务状态（serve 侧枚举，JSON 为字符串）。 */
export type DownloadStatus = 'Queued' | 'Running' | 'Finished'

/** serve 侧任务对象（DownloadTask）。 */
export interface DownloadTask {
  /** 规范 id，如 av170001 / season2539 */
  id: string
  url: string
  title?: string | null
  pic?: string | null
  videoPubTime?: number | null
  taskCreateTime: number
  taskFinishTime?: number | null
  /** 当前阶段进度 0-1 */
  progress: number
  /** 每秒字节数 */
  downloadSpeed: number
  errorMessage?: string | null
  totalDownloadedBytes: number
  isSuccessful: boolean
  status: DownloadStatus
  savePaths: string[]
}

/** 整体快照（/api/v1/tasks 响应）。 */
export interface TaskSnapshot {
  running: DownloadTask[]
  finished: DownloadTask[]
}

/** /healthz 响应；interactive 为 serve 事件流开关（旧版 serve 无此字段时为 undefined）。 */
export interface HealthStatus {
  status: string
  running: number
  interactive?: boolean
}

/** 混流方式（与 Core MuxMode 枚举小写对齐）。 */
export type MuxMode = 'none' | 'mpeg4' | 'mp4box' | 'mkv'

/** API 通道（与 Core ApiType 枚举小写对齐）。 */
export type ApiType = 'web' | 'tv' | 'app' | 'intl'

/**
 * 任务提交契约（POST /api/v1/tasks 请求体）。
 * 为 ServeRequestOptions 的前端镜像：serve 明确排除的字段（主机可控路径、进程级开关、
 * 交互式选项）不在此列，见 lib/options.ts 的排除标注。
 */
export interface ServeRequestOptions {
  url: string
  api: ApiType
  content: string
  mux: MuxMode
  encodingPriority?: string
  dfnPriority?: string
  audioQuality?: string
  encodingFirst: boolean
  onlyShowInfo: boolean
  showAll: boolean
  useAria2c: boolean
  hideStreams: boolean
  singleThread: boolean
  noForceHttp: boolean
  downloadDanmakuFormats?: string
  commentCount: number
  commentSort?: string
  commentFormats?: string
  videoAscending: boolean
  audioAscending: boolean
  allowPcdn: boolean
  allowPreview: boolean
  noForceHost: boolean
  saveArchivesToFile: boolean
  stopOnError: boolean
  interactivePages: boolean
  interactiveQuality: boolean
  liveQuality: number
  pages: string
  lang: string
  cookie: string
  accessToken: string
  uposHost: string
  delayPerPage: string
  area: string
  callBackWebHook?: string
}

/** 工作流事件（type 判别符与 Core WorkflowEvent 对齐）。 */
export type WorkflowEvent =
  | { type: 'message'; text: string; time: string }
  | { type: 'progressStart'; scope: string; stageName: string }
  | {
      type: 'progressSample'
      scope: string
      ratio: number
      totalBytes: number
      speed: number
      detail?: string
    }
  | { type: 'progressEnd'; scope: string }
  | {
      type: 'optionRequest'
      requestId: string
      scope: string
      prompt: string
      options: { id: string; label: string }[]
      deadline: string
      defaultOptionId?: string
    }

/** 服务端 → 客户端帧。 */
export interface EventFrame {
  kind: 'event' | 'snapshot' | 'choiceResult' | 'error'
  taskId?: string
  event?: WorkflowEvent
  snapshot?: { scope: string; ratio: number; totalBytes: number; speed: number; detail?: string }
  requestId?: string
  ok?: boolean
  error?: string
}

/** 客户端 → 服务端帧。 */
export interface ClientFrame {
  kind: 'subscribe' | 'unsubscribe' | 'submitChoice' | 'ping'
  taskId?: string
  requestId?: string
  choice?: string
}

/** 前端任务视图状态（对齐 GUI TaskState 的五态展示）。 */
export type TaskViewStatus = 'Waiting' | 'Running' | 'Success' | 'Failed' | 'Cancelled'

export interface TaskView {
  id: string
  url: string
  title?: string
  status: TaskViewStatus
  statusText: string
  progress: number
  detail: string
  errorMessage?: string
  savePaths: string[]
  isLive: boolean
  /** 提交时的选项快照，「继续」按钮据此重新提交（仅内存，不持久化） */
  retryOptions?: unknown
}
