import { describe, expect, it } from 'vitest'

import {
  commentable,
  contentItemEnabled,
  contentWarnings,
  modeOf,
  optionAvailability,
  videoLike
} from '../lib/availability'

describe('modeOf', () => {
  it('Pgc 归入 Video，其余直传', () => {
    expect(modeOf('Video')).toBe('Video')
    expect(modeOf('Pgc')).toBe('Video')
    expect(modeOf('Opus')).toBe('Opus')
    expect(modeOf('Audio')).toBe('Audio')
    expect(modeOf('Live')).toBe('Live')
    expect(modeOf('Mixed')).toBe('Mixed')
  })
})

describe('videoLike / commentable', () => {
  it('未识别、视频与空间动态属视频域', () => {
    expect(videoLike(null)).toBe(true)
    expect(videoLike('Video')).toBe(true)
    expect(videoLike('Mixed')).toBe(true)
    expect(videoLike('Opus')).toBe(false)
    expect(videoLike('Audio')).toBe(false)
    expect(videoLike('Live')).toBe(false)
  })

  it('评论对视频域与专栏域生效', () => {
    expect(commentable(null)).toBe(true)
    expect(commentable('Video')).toBe(true)
    expect(commentable('Mixed')).toBe(true)
    expect(commentable('Opus')).toBe(true)
    expect(commentable('Audio')).toBe(false)
    expect(commentable('Live')).toBe(false)
  })
})

describe('contentItemEnabled', () => {
  it('未识别与空间动态不限制', () => {
    for (const ch of ['a', 'v', 'c', 'd', 's', 'S', 'o', 'O', 'C', 'm', 'A', 'i', 'M']) {
      expect(contentItemEnabled(null, ch)).toBe(true)
      expect(contentItemEnabled('Mixed', ch)).toBe(true)
    }
  })

  it('视频域禁用专栏三项', () => {
    expect(contentItemEnabled('Video', 'i')).toBe(false)
    expect(contentItemEnabled('Video', 'A')).toBe(false)
    expect(contentItemEnabled('Video', 'M')).toBe(false)
    expect(contentItemEnabled('Video', 'a')).toBe(true)
    expect(contentItemEnabled('Video', 'C')).toBe(true)
  })

  it('专栏域仅开放专栏与评论项', () => {
    for (const ch of ['i', 'A', 'M', 'o', 'O']) {
      expect(contentItemEnabled('Opus', ch)).toBe(true)
    }

    for (const ch of ['a', 'v', 'c', 'd', 's', 'S', 'C', 'm']) {
      expect(contentItemEnabled('Opus', ch)).toBe(false)
    }
  })

  it('音频域仅开放音频，直播全禁', () => {
    expect(contentItemEnabled('Audio', 'a')).toBe(true)
    expect(contentItemEnabled('Audio', 'v')).toBe(false)
    for (const ch of ['a', 'v', 'c', 'd', 's', 'S', 'o', 'O', 'C', 'm', 'A', 'i', 'M']) {
      expect(contentItemEnabled('Live', ch)).toBe(false)
    }
  })
})

describe('optionAvailability', () => {
  it('未识别目标仅直播清晰度外的项全部可用', () => {
    const a = optionAvailability({ mode: null, infoOnly: false, commentsCount: 0, mux: 'mpeg4' })
    expect(a.pages).toBe(true)
    expect(a.api).toBe(true)
    expect(a.priority).toBe(true)
    expect(a.liveQuality).toBe(true)
    expect(a.lang).toBe(true)
  })

  it('直播域禁用下载与解析项，保留直播清晰度', () => {
    const a = optionAvailability({ mode: 'Live', infoOnly: false, commentsCount: 0, mux: 'mpeg4' })
    expect(a.downloadChecks).toBe(false)
    expect(a.mux).toBe(false)
    expect(a.pages).toBe(false)
    expect(a.api).toBe(false)
    expect(a.priority).toBe(false)
    expect(a.liveQuality).toBe(true)
  })

  it('仅解析时禁用输出与下载项，直播清晰度仍可用', () => {
    const a = optionAvailability({ mode: null, infoOnly: true, commentsCount: 10, mux: 'mpeg4' })
    expect(a.downloadChecks).toBe(false)
    expect(a.danmakuFormat).toBe(false)
    expect(a.commentFormat).toBe(false)
    expect(a.liveQuality).toBe(true)
    expect(a.ascending).toBe(true)
  })

  it('混流方式为 none 时禁用混流音频语言', () => {
    const none = optionAvailability({ mode: null, infoOnly: false, commentsCount: 0, mux: 'none' })
    expect(none.lang).toBe(false)
    const mpeg4 = optionAvailability({
      mode: null,
      infoOnly: false,
      commentsCount: 0,
      mux: 'mpeg4'
    })
    expect(mpeg4.lang).toBe(true)
  })

  it('评论行为零条数与不可评论域禁用', () => {
    const zero = optionAvailability({ mode: null, infoOnly: false, commentsCount: 0, mux: 'mpeg4' })
    expect(zero.commentRow).toBe(false)
    expect(zero.commentSort).toBe(false)
    expect(zero.commentsCount).toBe(true)
    const opus = optionAvailability({
      mode: 'Opus',
      infoOnly: false,
      commentsCount: 5,
      mux: 'mpeg4'
    })
    expect(opus.commentRow).toBe(true)
    const live = optionAvailability({
      mode: 'Live',
      infoOnly: false,
      commentsCount: 5,
      mux: 'mpeg4'
    })
    expect(live.commentRow).toBe(false)
  })
})

describe('contentWarnings', () => {
  const base = {
    hasTarget: true,
    mode: null,
    content: 'av',
    commentsCount: 0,
    danmakuFormats: '',
    infoOnly: false
  } satisfies Parameters<typeof contentWarnings>[0]

  it('封面嵌入缺音频或视频时警告', () => {
    expect(contentWarnings({ ...base, content: 'vC' })).toContain(
      '封面嵌入（C）与嵌入元数据（m）需要同时勾选音频与视频才会生效'
    )
    expect(contentWarnings({ ...base, content: 'am' })).toContain(
      '封面嵌入（C）与嵌入元数据（m）需要同时勾选音频与视频才会生效'
    )
    expect(contentWarnings({ ...base, content: 'avCm' })).not.toContain(
      '封面嵌入（C）与嵌入元数据（m）需要同时勾选音频与视频才会生效'
    )
  })

  it('选了弹幕格式但内容未勾选弹幕时警告', () => {
    expect(contentWarnings({ ...base, danmakuFormats: 'xml' })).toContain(
      '已选择弹幕格式，但内容未勾选弹幕（d），弹幕不会下载'
    )
    expect(contentWarnings({ ...base, content: 'avd', danmakuFormats: 'xml,ass' })).not.toContain(
      '已选择弹幕格式，但内容未勾选弹幕（d），弹幕不会下载'
    )
  })

  it('设了评论条数但内容未勾选评论时警告', () => {
    expect(contentWarnings({ ...base, commentsCount: 10 })).toContain(
      '已设置评论条数，但内容未勾选评论（o / O），评论不会下载'
    )
    expect(contentWarnings({ ...base, content: 'avo', commentsCount: 10 })).not.toContain(
      '已设置评论条数，但内容未勾选评论（o / O），评论不会下载'
    )
  })

  it('未识别目标或仅解析时不警告', () => {
    const input = { ...base, commentsCount: 10, danmakuFormats: 'xml' }
    expect(contentWarnings({ ...input, hasTarget: false })).toHaveLength(0)
    expect(contentWarnings({ ...input, infoOnly: true })).toHaveLength(0)
  })

  it('直播域不产生任何警告', () => {
    const input = { ...base, mode: 'Live' as const, commentsCount: 10, danmakuFormats: 'xml' }
    expect(contentWarnings(input)).toHaveLength(0)
  })
})
