import { describe, expect, it } from 'vitest'

import { DEFAULT_OPTIONS, toServeRequest } from '../lib/options'

describe('toServeRequest', () => {
  const credential = { cookie: 'SESSDATA=abc', accessToken: 'token123' }

  it('默认选项映射为 serve 契约', () => {
    const request = toServeRequest(DEFAULT_OPTIONS, 'av170001', credential)
    expect(request.url).toBe('av170001')
    expect(request.api).toBe('web')
    expect(request.mux).toBe('mpeg4')
    expect(request.content).toBe('avmsCiM')
    expect(request.commentCount).toBe(0)
    expect(request.onlyShowInfo).toBe(false)
    expect(request.cookie).toBe('SESSDATA=abc')
    expect(request.accessToken).toBe('token123')
  })

  it('空串字段映射为 undefined', () => {
    const request = toServeRequest(DEFAULT_OPTIONS, 'av170001', { cookie: '', accessToken: '' })
    expect(request.encodingPriority).toBeUndefined()
    expect(request.dfnPriority).toBeUndefined()
    expect(request.audioQuality).toBeUndefined()
  })

  it('数值与布尔字段正确转换', () => {
    const options = {
      ...DEFAULT_OPTIONS,
      commentsCount: '12',
      infoOnly: true,
      useAria2c: true,
      api: 'tv' as const
    }
    const request = toServeRequest(options, 'av170001', { cookie: '', accessToken: '' })
    expect(request.commentCount).toBe(12)
    expect(request.onlyShowInfo).toBe(true)
    expect(request.useAria2c).toBe(true)
    expect(request.api).toBe('tv')
  })

  it('serve 排除字段不进请求契约', () => {
    const request = toServeRequest(DEFAULT_OPTIONS, 'av170001', { cookie: '', accessToken: '' })
    expect('workDir' in request).toBe(false)
    expect('ffmpegPath' in request).toBe(false)
    expect('debug' in request).toBe(false)
  })

  it('maxRetry 数值兜底：负数夹 0、小数截断、非法回落 0', () => {
    const cases: [number, number][] = [
      [-5, 0],
      [2.7, 2],
      [Number.NaN, 0],
      [0, 0],
      [4, 4]
    ]
    for (const [input, expected] of cases) {
      const request = toServeRequest({ ...DEFAULT_OPTIONS, maxRetry: input }, 'av170001', {
        cookie: '',
        accessToken: ''
      })
      expect(request.maxRetry).toBe(expected)
    }
  })
})
