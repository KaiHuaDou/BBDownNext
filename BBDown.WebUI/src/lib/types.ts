/** serve 数据类型：与 BBDown.Serve 的 DownloadTask / ServeRequestOptions / WorkflowEvent 的序列化格式相同。 */

/** 任务状态（serve 侧枚举，JSON 为字符串）。 */
export type DownloadStatus = 'Pending' | 'Queued' | 'Running' | 'Finished'

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
  /** 任务是否被取消（用户停止 / 服务器退出），与真实失败区分 */
  isCancelled?: boolean
  status: DownloadStatus
  savePaths: string[]
}

/** 整体快照（/api/v1/tasks 响应）。 */
export interface TaskSnapshot {
  running: DownloadTask[]
  finished: DownloadTask[]
}

/** /healthz 响应（前端不轮询，保留类型供外部调用者使用）。 */
export interface HealthStatus {
  status: string
  running: number
}

/** 混流方式（与 Core MuxMode 枚举的小写形式相同）。 */
export type MuxMode = 'none' | 'mpeg4' | 'mp4box' | 'mkv'

/** API 通道（与 Core ApiType 枚举的小写形式相同）。 */
export type ApiType = 'web' | 'tv' | 'app' | 'intl'

/**
 * 任务提交体（POST /api/v1/tasks 请求体）。
 * 为 ServeRequestOptions 的前端镜像：serve 明确排除的字段（主机可控路径、进程级开关）不在此列，
 * 交互式选项随请求提交，应答经 WebSocket 事件流完成，见 lib/options.ts。
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
  uposHost: string
  delayPerPage?: number
  area: string
  /** 每个下载项的额外重试次数，未指定时为 3（与 serve ServeRequestOptions.MaxRetry 相同）。 */
  maxRetry: number
}

/** 工作流事件（type 标记与 Core WorkflowEvent 相同）。 */
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

/** 进度样本：progressSample 事件与 snapshot 帧共用同一形状。 */
export interface ProgressSample {
  scope: string
  ratio: number
  totalBytes: number
  speed: number
  detail?: string
}

/**
 * 服务端 → 客户端帧：按 kind 区分联合，各成员的必填字段即校验依据（校验见 api/ws.ts）。
 * taskList 帧携带全量任务列表（running + finished），用于免轮询刷新
 */
export type EventFrame =
  | { kind: 'event'; taskId: string; event: WorkflowEvent }
  | { kind: 'snapshot'; taskId: string; snapshot: ProgressSample }
  | { kind: 'choiceResult'; requestId: string; ok?: boolean; error?: string }
  | { kind: 'error'; error?: string }
  | { kind: 'taskList'; tasks: TaskSnapshot }

/** 客户端 → 服务端帧。 */
export interface ClientFrame {
  kind: 'subscribe' | 'unsubscribe' | 'submitChoice' | 'ping'
  taskId?: string
  requestId?: string
  choice?: string
}

/** 前端任务视图状态（与 GUI TaskState 的五态展示相同）。 */
export type TaskViewStatus = 'Pending' | 'Waiting' | 'Running' | 'Success' | 'Failed' | 'Cancelled'

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
  /** 资源类型中文（由规范 id 前缀推导：视频 / 番剧 / 专栏 / 直播 …） */
  kind: string
}
