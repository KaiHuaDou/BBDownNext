import { DEFAULT_CONTENT } from './content'
import { DEFAULT_LIVE_QUALITY } from './live'
import type { ApiType, MuxMode, ServeRequestOptions } from './types'

/**
 * 面板选项快照：复刻 GUI TaskParams 的字段与默认值，均随任务经 serve 请求契约（ServeRequestOptions）提交。
 * 主机可控字段（路径 / 可执行文件 / 进程级开关）不在此处，由 serve 启动参数固定。
 */
export interface TaskOptions {
  content: string
  useAria2c: boolean
  singleThread: boolean
  infoOnly: boolean
  showAll: boolean
  allowPreview: boolean
  saveRecords: boolean
  stopOnError: boolean
  videoAscending: boolean
  audioAscending: boolean
  interactivePages: boolean
  interactiveQuality: boolean
  hideStreams: boolean
  encodingFirst: boolean
  allowPcdn: boolean
  noForceHost: boolean
  noForceHttp: boolean
  mux: MuxMode
  encodingPriority: string
  dfnPriority: string
  audioQuality: string
  pages: string
  danmakuFormats: string
  commentsCount: string
  commentsSort: string
  commentsFormats: string
  lang: string
  delayPerPage: string
  liveQuality: number
  api: ApiType
  area: string
  uposHost: string
  /** 每个下载项的额外重试次数，缺省回落 3（与 serve MaxRetry 对齐）。 */
  maxRetry: number
}

export const DEFAULT_OPTIONS: TaskOptions = {
  content: DEFAULT_CONTENT,
  useAria2c: false,
  singleThread: false,
  infoOnly: false,
  showAll: false,
  allowPreview: false,
  saveRecords: false,
  stopOnError: false,
  videoAscending: false,
  audioAscending: false,
  interactivePages: false,
  interactiveQuality: false,
  hideStreams: false,
  encodingFirst: false,
  allowPcdn: false,
  noForceHost: false,
  noForceHttp: false,
  mux: 'mpeg4',
  encodingPriority: '',
  dfnPriority: '',
  audioQuality: '',
  pages: '',
  danmakuFormats: 'xml,ass',
  commentsCount: '0',
  commentsSort: 'hot',
  commentsFormats: 'json,txt',
  lang: '',
  delayPerPage: '0',
  liveQuality: DEFAULT_LIVE_QUALITY,
  api: 'web',
  area: '',
  uposHost: '',
  maxRetry: 3
}

/** 混流方式选项（值 + 显示名，与 GUI MuxChoices 一致）。 */
export const MUX_CHOICES: { value: MuxMode; label: string }[] = [
  { value: 'mpeg4', label: 'FFmpeg 混流为 MPEG4' },
  { value: 'mp4box', label: 'MP4Box 混流' },
  { value: 'mkv', label: 'FFmpeg 混流为 Matroska' },
  { value: 'none', label: '不混流（保留裸轨）' }
]

/** API 通道（与 Core ApiType 枚举小写对齐）。 */
export const API_CHOICES: ApiType[] = ['web', 'tv', 'app', 'intl']

/** 把面板选项映射为 serve 提交契约；cookie / accessToken 由外部凭据合并。 */
export function toServeRequest(
  options: TaskOptions,
  url: string,
  credential: { cookie: string; accessToken: string }
): ServeRequestOptions {
  const orUndefined = (value: string): string | undefined =>
    value.length === 0 ? undefined : value
  return {
    url,
    api: options.api,
    content: options.content,
    mux: options.mux,
    encodingPriority: orUndefined(options.encodingPriority),
    dfnPriority: orUndefined(options.dfnPriority),
    audioQuality: orUndefined(options.audioQuality),
    encodingFirst: options.encodingFirst,
    onlyShowInfo: options.infoOnly,
    showAll: options.showAll,
    useAria2c: options.useAria2c,
    hideStreams: options.hideStreams,
    singleThread: options.singleThread,
    noForceHttp: options.noForceHttp,
    downloadDanmakuFormats: options.danmakuFormats,
    commentCount: Number.parseInt(options.commentsCount, 10) || 0,
    commentSort: options.commentsSort,
    commentFormats: options.commentsFormats,
    videoAscending: options.videoAscending,
    audioAscending: options.audioAscending,
    allowPcdn: options.allowPcdn,
    allowPreview: options.allowPreview,
    noForceHost: options.noForceHost,
    saveArchivesToFile: options.saveRecords,
    stopOnError: options.stopOnError,
    interactivePages: options.interactivePages,
    interactiveQuality: options.interactiveQuality,
    liveQuality: options.liveQuality,
    pages: options.pages,
    lang: options.lang,
    cookie: credential.cookie,
    accessToken: credential.accessToken,
    uposHost: options.uposHost,
    delayPerPage: options.delayPerPage,
    area: options.area,
    // 数值输入清空/非法时回落 0（不重试）；负数经 Math.max 夹为 0
    maxRetry: Math.max(0, Math.trunc(options.maxRetry) || 0)
  }
}
