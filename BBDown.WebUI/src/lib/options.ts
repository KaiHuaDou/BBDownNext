import { DEFAULT_CONTENT } from './content'
import { DEFAULT_LIVE_QUALITY } from './live'
import { readLocalStorage, writeLocalStorage } from './storage'
import type { ApiType, MuxMode, ServeRequestOptions } from './types'

/**
 * 面板选项快照：复刻 GUI TaskParams 的字段与默认值，均随任务经 serve 请求体（ServeRequestOptions）提交。
 * 主机可控字段（路径 / 可执行文件 / 进程级开关）不在此处，由 serve 启动参数固定。
 */
export interface TaskOptions {
  content: string
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
  /** 每个下载项的额外重试次数，未指定时为 3（与 serve MaxRetry 相同）。 */
  maxRetry: number
}

export const DEFAULT_OPTIONS: TaskOptions = {
  content: DEFAULT_CONTENT,
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

const OPTIONS_STORAGE_KEY = 'bbdown.options'

/** 从 localStorage 载入面板选项；无存储或内容损坏时回落默认值。 */
export function loadOptions(): TaskOptions {
  const raw = readLocalStorage(OPTIONS_STORAGE_KEY)
  if (raw.length === 0) {
    return { ...DEFAULT_OPTIONS }
  }

  try {
    const parsed = JSON.parse(raw) as Partial<TaskOptions>
    return { ...DEFAULT_OPTIONS, ...parsed }
  } catch {
    return { ...DEFAULT_OPTIONS }
  }
}

/** 面板选项持久化到 localStorage（凭据 / serve 配置各自独立存储，不在此键内）。 */
export function saveOptions(options: TaskOptions): void {
  writeLocalStorage(OPTIONS_STORAGE_KEY, JSON.stringify(options))
}

/** 混流方式选项（值 + 显示名，与 GUI MuxChoices 一致）。 */
export const MUX_CHOICES: { value: MuxMode; label: string }[] = [
  { value: 'mpeg4', label: 'FFmpeg 混流为 MPEG4' },
  { value: 'mp4box', label: 'MP4Box 混流' },
  { value: 'mkv', label: 'FFmpeg 混流为 Matroska' },
  { value: 'none', label: '不混流（保留裸轨）' }
]

/** API 通道（与 Core ApiType 枚举的小写形式相同）。 */
export const API_CHOICES: ApiType[] = ['web', 'tv', 'app', 'intl']

/** 编码优先级候选（与 Core Config.EncodingNames 一致）。 */
export const ENCODING_CHOICES: string[] = ['HEVC', 'AV1', 'AVC', 'FLAC', 'EAC3', 'M4A']

/** 画质优先级候选（与 Core Config.QualityNameList 一致，高 → 低，同名去重）。 */
export const DFN_CHOICES: string[] = [
  '8K 超高清',
  '杜比视界',
  'HDR 真彩',
  'HDR Vivid',
  '4K 超清',
  '1080P 高帧率',
  '1080P 高码率',
  '1080P 高清',
  '智能修复',
  '720P 高帧率',
  '720P 高清',
  '480P 清晰',
  '360P 流畅',
  '240P 流畅',
  '144P 流畅'
]

/** 音频档位候选（与 Core Config.AudioQualityNameList 一致，码率高 → 低）。 */
export const AUDIO_QUALITY_CHOICES: string[] = [
  '杜比全景声',
  'Hi-Res 无损',
  '192K',
  '132K',
  '64K',
  '杜比音效'
]

/** 解析优先级逗号串：去空白、忽略候选外的 token、去重（GUI PriorityPicker.Reset 同规则） */
export function parsePriority(value: string, choices: string[]): string[] {
  const known = new Set(choices.map((c) => c.toLowerCase()))
  const picked: string[] = []
  for (const token of value.split(',')) {
    const name = token.trim()
    if (
      name.length > 0 &&
      known.has(name.toLowerCase()) &&
      !picked.some((p) => p.toLowerCase() === name.toLowerCase())
    ) {
      const match = choices.find((c) => c.toLowerCase() === name.toLowerCase())
      if (match) {
        picked.push(match)
      }
    }
  }

  return picked
}

/** 把面板选项映射为 serve 提交体。cookie / accessToken 不在此列：凭据由服务端的 BBDown.data 提供。 */
export function toServeRequest(options: TaskOptions, url: string): ServeRequestOptions {
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
    uposHost: options.uposHost,
    delayPerPage: Number.parseInt(options.delayPerPage, 10) || 0,
    area: options.area,
    // 数值输入清空/非法时回落 0（不重试）；负数经 Math.max 夹为 0
    maxRetry: Math.max(0, Math.trunc(options.maxRetry) || 0)
  }
}
