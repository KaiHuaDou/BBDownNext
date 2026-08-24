import { DEFAULT_CONTENT } from './content'
import { DEFAULT_LIVE_QUALITY } from './live'
import type { ApiType, MuxMode, ServeRequestOptions } from './types'

/**
 * 面板选项快照：复刻 GUI TaskParams 的字段与默认值。
 * serve 请求契约（ServeRequestOptions）只接收其中一部分字段，排除项见 SERVE_EXCLUDED。
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
  debug: boolean
  videoAscending: boolean
  audioAscending: boolean
  interactivePages: boolean
  interactiveQuality: boolean
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
  userAgent: string
  workDir: string
  ffmpegPath: string
  mp4boxPath: string
  aria2cPath: string
  postProcessPath: string
  aria2cArgs: string
  delayPerPage: string
  liveQuality: string
  api: ApiType
  filePattern: string
  multiFilePattern: string
  host: string
  epHost: string
  tvHost: string
  area: string
  uposHost: string
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
  debug: false,
  videoAscending: false,
  audioAscending: false,
  interactivePages: false,
  interactiveQuality: false,
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
  userAgent: '',
  workDir: '',
  ffmpegPath: '',
  mp4boxPath: '',
  aria2cPath: '',
  postProcessPath: '',
  aria2cArgs: '',
  delayPerPage: '0',
  liveQuality: DEFAULT_LIVE_QUALITY,
  api: 'web',
  filePattern: '',
  multiFilePattern: '',
  host: '',
  epHost: '',
  tvHost: '',
  area: '',
  uposHost: ''
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

/**
 * serve 请求契约排除的字段及原因：serve 无 stdin（交互式）、无本机路径注入（主机可控）、
 * 进程级全局、或改为服务端启动参数。UI 据此禁用输入并展示原因。
 */
export const SERVE_EXCLUDED: Record<string, string> = {
  debug: '进程级开关，由 serve 启动决定',
  interactivePages: 'serve 无交互 stdin，请求契约排除',
  interactiveQuality: 'serve 无交互 stdin，请求契约排除',
  userAgent: '进程级全局，由 serve 启动决定',
  workDir: '由 serve --work-dir 启动参数固定',
  ffmpegPath: '主机可控字段，由 serve 侧配置',
  mp4boxPath: '主机可控字段，由 serve 侧配置',
  aria2cPath: '主机可控字段，由 serve 侧配置',
  postProcessPath: '主机可控字段，由 serve 侧配置',
  aria2cArgs: '主机可控字段，由 serve 侧配置',
  filePattern: '主机可控字段，由 serve 侧配置',
  multiFilePattern: '主机可控字段，由 serve 侧配置',
  host: '由 serve --host 启动参数固定',
  epHost: '由 serve --ep-host 启动参数固定',
  tvHost: '由 serve --tv-host 启动参数固定',
  liveQuality: 'serve 请求契约未暴露该字段'
}

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
    encodingFirst: false,
    onlyShowInfo: options.infoOnly,
    showAll: options.showAll,
    useAria2c: options.useAria2c,
    hideStreams: false,
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
    pages: options.pages,
    lang: options.lang,
    cookie: credential.cookie,
    accessToken: credential.accessToken,
    uposHost: options.uposHost,
    delayPerPage: options.delayPerPage,
    area: options.area
  }
}
