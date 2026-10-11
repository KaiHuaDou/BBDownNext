/** 选项可用性联动，规则与 GUI MainWindow.Availability 一致：界面可用项与实际生效项保持同步 */

import type { TargetKind } from './urlDetector'

/** 内容域，与 GUI ContentMode 相同（Video 已合并 Pgc / 课程） */
export type ContentMode = 'Video' | 'Opus' | 'Audio' | 'Live' | 'Mixed'

/** 目标域到内容域的映射（GUI ModeOf） */
export function modeOf(kind: TargetKind): ContentMode {
  return kind === 'Pgc' ? 'Video' : kind
}

/** Video 域含未识别目标（不联动）与空间动态（视频部分跟随内容勾选） */
export function videoLike(mode: ContentMode | null): boolean {
  return mode === null || mode === 'Video' || mode === 'Mixed'
}

/** 评论选项对视频域与专栏 / 图文域都生效 */
export function commentable(mode: ContentMode | null): boolean {
  return videoLike(mode) || mode === 'Opus'
}

/** 内容勾选项按目标域可编辑；未识别目标不限制（GUI 同）。禁用不清空值，提交照传、Core 自然失效 */
export function contentItemEnabled(mode: ContentMode | null, ch: string): boolean {
  switch (mode) {
    case null: {
      return true
    }

    case 'Video': {
      return ch !== 'i' && ch !== 'A' && ch !== 'M'
    }

    case 'Mixed': {
      return true
    }

    case 'Opus': {
      return ch === 'i' || ch === 'A' || ch === 'M' || ch === 'o' || ch === 'O'
    }

    case 'Audio': {
      return ch === 'a'
    }

    default: {
      return false
    }
  }
}

/** 各选项的可用标志，与 GUI 逐控件 IsEnabled 一一对应 */
export interface OptionAvailability {
  videoLike: boolean
  commentable: boolean
  commentRow: boolean
  pages: boolean
  interactivePages: boolean
  showAll: boolean
  allowPreview: boolean
  commentsCount: boolean
  commentSort: boolean
  commentFormat: boolean
  danmakuFormat: boolean
  downloadChecks: boolean
  delayPerPage: boolean
  maxRetry: boolean
  mux: boolean
  lang: boolean
  ascending: boolean
  interactiveQuality: boolean
  api: boolean
  priority: boolean
  liveQuality: boolean
}

/** 单点计算全部可用标志；任何相关输入变化后统一刷新一次（GUI RefreshAvailability 同思路） */
export function optionAvailability(args: {
  mode: ContentMode | null
  infoOnly: boolean
  commentsCount: number
  mux: string
}): OptionAvailability {
  const { mode, infoOnly, commentsCount, mux } = args
  const videoLikeMode = videoLike(mode)
  const commentableMode = commentable(mode)
  const commentRowMode = commentableMode && commentsCount > 0
  const editable = videoLikeMode && !infoOnly

  return {
    videoLike: videoLikeMode,
    commentable: commentableMode,
    commentRow: commentRowMode,
    pages: videoLikeMode,
    interactivePages: videoLikeMode,
    showAll: videoLikeMode,
    allowPreview: videoLikeMode,
    commentsCount: commentableMode,
    commentSort: commentRowMode && !infoOnly,
    commentFormat: commentRowMode && !infoOnly,
    danmakuFormat: videoLikeMode && !infoOnly,
    downloadChecks: editable,
    delayPerPage: editable,
    maxRetry: editable,
    mux: editable,
    lang: editable && mux !== 'none',
    ascending: videoLikeMode,
    interactiveQuality: videoLikeMode,
    api: videoLikeMode,
    priority: videoLikeMode,
    liveQuality: mode === null || mode === 'Live'
  }
}

export interface ContentWarnInput {
  hasTarget: boolean
  mode: ContentMode | null
  content: string
  commentsCount: number
  danmakuFormats: string
  infoOnly: boolean
}

/** 内容勾选与输出格式的组合警告，与 GUI ApplyContentWarn 相同的三条；调用方以「；」连接展示 */
export function contentWarnings(input: ContentWarnInput): string[] {
  const warnings: string[] = []
  const has = (ch: string): boolean => input.content.includes(ch)
  const commentWarn = commentable(input.mode) && input.commentsCount > 0 && !has('o') && !has('O')

  if (input.hasTarget && videoLike(input.mode) && !input.infoOnly) {
    if ((has('C') || has('m')) && !(has('a') && has('v'))) {
      warnings.push('封面嵌入（C）与嵌入元数据（m）需要同时勾选音频与视频才会生效')
    }

    if (!has('d') && input.danmakuFormats.length > 0) {
      warnings.push('已选择弹幕格式，但内容未勾选弹幕（d），弹幕不会下载')
    }
  }

  if (input.hasTarget && !input.infoOnly && commentWarn) {
    warnings.push('已设置评论条数，但内容未勾选评论（o / O），评论不会下载')
  }

  return warnings
}
