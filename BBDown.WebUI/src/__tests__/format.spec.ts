import { describe, expect, it } from 'vitest'

import { buildDetail, formatEta, formatFileSize, formatSpeed, formatTime } from '../lib/format'

describe('formatFileSize', () => {
  it('按字节量级格式化', () => {
    expect(formatFileSize(0)).toBe('0 bytes')
    expect(formatFileSize(1023)).toBe('1023 bytes')
    expect(formatFileSize(1024)).toBe('1.00 KB')
    expect(formatFileSize(1024 * 1024)).toBe('1.00 MB')
    expect(formatFileSize(1024 * 1024 * 1024)).toBe('1.00 GB')
  })

  it('负数为非法输入', () => {
    expect(() => formatFileSize(-1)).toThrow(RangeError)
  })
})

describe('formatSpeed / formatTime', () => {
  it('速度带 /s 后缀', () => {
    expect(formatSpeed(1024 * 1024)).toBe('1.00 MB/s')
  })

  it('秒数格式化为 mmss / hhmmss', () => {
    expect(formatTime(83)).toBe('01m23s')
    expect(formatTime(3600 + 125)).toBe('1h02m05s')
  })
})

describe('formatEta', () => {
  it('按剩余字节与速率外推', () => {
    expect(formatEta(0.5, 100, 100)).toBe('00m01s')
  })

  it('比例过低或速度为 0 时不显示', () => {
    expect(formatEta(0.01, 100, 100)).toBeNull()
    expect(formatEta(0.5, 0, 100)).toBeNull()
  })
})

describe('buildDetail', () => {
  it('优先阶段文本并附速度', () => {
    expect(buildDetail(0.5, 100, 100, '原画')).toBe('原画 | 100 bytes/s')
  })

  it('无阶段文本时速度 + 剩余时间', () => {
    expect(buildDetail(0.5, 100, 100)).toBe('100 bytes/s · 剩余 00m01s')
  })

  it('无速度时为空', () => {
    expect(buildDetail(0.1, 0, 0)).toBe('')
  })
})
