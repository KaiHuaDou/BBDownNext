import { describe, expect, it } from 'vitest'

import type { DownloadTask, DownloadStatus } from '../lib/types'
import { kindOfId, toView } from './taskView'

function makeTask(overrides: Partial<DownloadTask> = { }): DownloadTask {
  return {
    id: 'av1',
    url: 'https://example.com',
    taskCreateTime: 0,
    progress: 0,
    downloadSpeed: 0,
    totalDownloadedBytes: 0,
    isSuccessful: false,
    status: 'Queued',
    savePaths: [ ],
    ...overrides
  }
}

describe('kindOfId', () => {
  it('按规范 id 前缀推导资源类型', () => {
    expect(kindOfId('opus123')).toBe('专栏')
    expect(kindOfId('cv123')).toBe('专栏')
    expect(kindOfId('live123')).toBe('直播')
    expect(kindOfId('ep123')).toBe('番剧')
    expect(kindOfId('ss123')).toBe('番剧')
    expect(kindOfId('season123')).toBe('番剧')
    expect(kindOfId('md123')).toBe('番剧')
    expect(kindOfId('cheeseep123')).toBe('课程')
    expect(kindOfId('cheeseseason123')).toBe('课程')
    expect(kindOfId('space123')).toBe('空间')
    expect(kindOfId('fav123')).toBe('收藏')
    expect(kindOfId('medialist123')).toBe('合集')
    expect(kindOfId('series123')).toBe('系列')
    expect(kindOfId('watchlater123')).toBe('稍后再看')
  })

  it('无匹配前缀回落视频', () => {
    expect(kindOfId('av123')).toBe('视频')
    expect(kindOfId('BV123')).toBe('视频')
    expect(kindOfId('anything')).toBe('视频')
  })
})

describe('toView 状态映射', () => {
  const cases: Array<[DownloadStatus, Partial<DownloadTask>, string, string]> = [
    ['Pending', { }, 'Pending', '待启动'],
    ['Queued', { }, 'Waiting', '等待中'],
    ['Running', { }, 'Running', '运行中'],
    ['Finished', { isSuccessful: true }, 'Success', '成功'],
    ['Finished', { isSuccessful: false, errorMessage: '任务已取消' }, 'Cancelled', '已取消'],
    ['Finished', { isSuccessful: false, errorMessage: '下载失败' }, 'Failed', '失败']
  ]

  it.each(cases)('status=%s → %s / %s', (status, overrides, expectedStatus, expectedText) => {
    const view = toView(makeTask({ status, ...overrides }))
    expect(view.status).toBe(expectedStatus)
    expect(view.statusText).toBe(expectedText)
  })

  it('运行态带进度与速度时构造详情文本', () => {
    const view = toView(makeTask({ status: 'Running', progress: 0.5, downloadSpeed: 100, totalDownloadedBytes: 100 }))
    expect(view.detail).toContain('剩余')
  })
})
