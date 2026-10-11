import { describe, expect, it } from 'vitest'

import {
  AUDIO_QUALITY_CHOICES,
  DEFAULT_OPTIONS,
  DFN_CHOICES,
  ENCODING_CHOICES,
  parsePriority,
  toServeRequest
} from '../lib/options'

describe('toServeRequest', () => {
  it('默认选项映射为 serve 请求体', () => {
    const request = toServeRequest(DEFAULT_OPTIONS, 'av170001')
    expect(request.url).toBe('av170001')
    expect(request.api).toBe('web')
    expect(request.mux).toBe('mpeg4')
    expect(request.content).toBe('avmsCiAM')
    expect(request.commentCount).toBe(0)
    expect(request.onlyShowInfo).toBe(false)
  })

  // 凭据由服务端 BBDown.data 提供，前端不再经请求体携带
  it('不带凭据字段', () => {
    const request = toServeRequest(DEFAULT_OPTIONS, 'av170001')
    expect('cookie' in request).toBe(false)
    expect('accessToken' in request).toBe(false)
  })

  it('空串字段映射为 undefined', () => {
    const request = toServeRequest(DEFAULT_OPTIONS, 'av170001')
    expect(request.encodingPriority).toBeUndefined()
    expect(request.dfnPriority).toBeUndefined()
    expect(request.audioQuality).toBeUndefined()
  })

  it('数值与布尔字段正确转换', () => {
    const options = {
      ...DEFAULT_OPTIONS,
      commentsCount: '12',
      infoOnly: true,
      api: 'tv' as const
    }
    const request = toServeRequest(options, 'av170001')
    expect(request.commentCount).toBe(12)
    expect(request.onlyShowInfo).toBe(true)
    expect(request.api).toBe('tv')
  })

  it('serve 排除字段不进请求体', () => {
    const request = toServeRequest(DEFAULT_OPTIONS, 'av170001')
    expect('workDir' in request).toBe(false)
    expect('ffmpegPath' in request).toBe(false)
    expect('debug' in request).toBe(false)
    expect('useAria2c' in request).toBe(false)
  })

  it('maxRetry 数值处理：负数夹 0、小数截断、非法值取 0', () => {
    const cases: [number, number][] = [
      [-5, 0],
      [2.7, 2],
      [Number.NaN, 0],
      [0, 0],
      [4, 4]
    ]
    for (const [input, expected] of cases) {
      const request = toServeRequest({ ...DEFAULT_OPTIONS, maxRetry: input }, 'av170001')
      expect(request.maxRetry).toBe(expected)
    }
  })
})

describe('parsePriority', () => {
  it('按逗号拆分并保留顺序', () => {
    expect(parsePriority('HEVC,AV1', ENCODING_CHOICES)).toEqual(['HEVC', 'AV1'])
  })

  it('忽略候选外的 token 与空白段，大小写不敏感并归一为候选写法', () => {
    expect(parsePriority(' hevc , 未知 , ,av1', ENCODING_CHOICES)).toEqual(['HEVC', 'AV1'])
  })

  it('重复 token 去重', () => {
    expect(parsePriority('AV1,av1,AVC', ENCODING_CHOICES)).toEqual(['AV1', 'AVC'])
  })

  it('空串得空数组', () => {
    expect(parsePriority('', DFN_CHOICES)).toEqual([])
  })
})

describe('优先级候选集', () => {
  it('编码候选与 Core Config.EncodingNames 一致', () => {
    expect(ENCODING_CHOICES).toEqual(['HEVC', 'AV1', 'AVC', 'FLAC', 'EAC3', 'M4A'])
  })

  it('画质候选按高 → 低且无重复', () => {
    expect(DFN_CHOICES[0]).toBe('8K 超高清')
    expect(DFN_CHOICES[DFN_CHOICES.length - 1]).toBe('144P 流畅')
    expect(new Set(DFN_CHOICES).size).toBe(DFN_CHOICES.length)
  })

  it('音频档位候选以杜比全景声开头、杜比音效收尾', () => {
    expect(AUDIO_QUALITY_CHOICES[0]).toBe('杜比全景声')
    expect(AUDIO_QUALITY_CHOICES[AUDIO_QUALITY_CHOICES.length - 1]).toBe('杜比音效')
  })
})
