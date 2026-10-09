import { describe, expect, it, vi } from 'vitest'

import { applySnapshot, handleEvent } from './snapshot'
import { createStore } from './store'
import type { TaskSocket } from '../api/ws'
import { DEFAULT_OPTIONS } from '../lib/options'
import type { DownloadStatus, DownloadTask, TaskSnapshot } from '../lib/types'

function fakeSocket(): TaskSocket {
  return {
    connect: () => {},
    close: () => {},
    subscribe: vi.fn(),
    unsubscribe: vi.fn(),
    submitChoice: vi.fn()
  }
}

function task(id: string, status: DownloadStatus): DownloadTask {
  return {
    id,
    url: `https://www.bilibili.com/video/${id}`,
    taskCreateTime: 0,
    progress: 0.5,
    downloadSpeed: 100,
    totalDownloadedBytes: 100,
    isSuccessful: status === 'Finished',
    status,
    savePaths: []
  }
}

function snapshot(runningIds: string[], finishedIds: string[] = []): TaskSnapshot {
  return {
    running: runningIds.map((id) => task(id, 'Running')),
    finished: finishedIds.map((id) => task(id, 'Finished'))
  }
}

describe('挂起状态随任务结束清理', () => {
  it('任务不在快照中时摘除其挂起提问、已应答记录与耗时基准', () => {
    const store = createStore()
    store.socket = fakeSocket()
    for (const taskId of ['av1', 'av2']) {
      handleEvent(store, taskId, {
        type: 'optionRequest',
        requestId: `req-${taskId}`,
        scope: taskId,
        prompt: '选一个',
        options: [{ id: 'a', label: 'A' }],
        deadline: ''
      })
      handleEvent(store, taskId, {
        type: 'progressStart',
        scope: taskId,
        stageName: '下载'
      })
    }

    expect(store.answeredAsks.size).toBe(2)
    expect(store.elapsedBase.size).toBe(2)

    // av1 已收尾，只剩 av2 存活
    applySnapshot(store, snapshot(['av2']))

    expect(store.answeredAsks.has('req-av1')).toBe(false)
    expect(store.answeredAsks.get('req-av2')).toBe('av2')
    expect(store.elapsedBase.has('av1')).toBe(false)
    expect(store.elapsedBase.has('av2')).toBe(true)
    expect(store.pendingAsks.value).toHaveLength(1)
  })

  it('任务转入已完成后立即摘除已应答记录与耗时基准', () => {
    const store = createStore()
    store.socket = fakeSocket()
    handleEvent(store, 'av1', {
      type: 'optionRequest',
      requestId: 'req-av1',
      scope: 'av1',
      prompt: '选一个',
      options: [{ id: 'a', label: 'A' }],
      deadline: ''
    })
    handleEvent(store, 'av1', { type: 'progressStart', scope: 'av1', stageName: '下载' })
    store.submittedOptions.set('av1', DEFAULT_OPTIONS)

    // av1 已完成但仍留在任务列表中
    applySnapshot(store, snapshot([], ['av1']))

    expect(store.answeredAsks.has('req-av1')).toBe(false)
    expect(store.elapsedBase.has('av1')).toBe(false)
    // 挂起提问与选项快照随任务列表保留，重试按钮仍能读到提交时的选项
    expect(store.pendingAsks.value).toHaveLength(1)
    expect(store.submittedOptions.has('av1')).toBe(true)
  })

  it('同一请求 id 重发不重复挂起', () => {
    const store = createStore()
    store.socket = fakeSocket()
    const event = {
      type: 'optionRequest' as const,
      requestId: 'req-1',
      scope: 'av1',
      prompt: '选一个',
      options: [{ id: 'a', label: 'A' }],
      deadline: ''
    }

    handleEvent(store, 'av1', event)
    handleEvent(store, 'av1', event)

    expect(store.pendingAsks.value).toHaveLength(1)
  })
})

describe('剩余时间的耗时基准', () => {
  it('阶段开始重置基准，进度回退同样重置', () => {
    const store = createStore()
    store.socket = fakeSocket()
    applySnapshot(store, snapshot(['av1']))

    handleEvent(store, 'av1', { type: 'progressStart', scope: 'av1', stageName: '下载' })
    const base = store.elapsedBase.get('av1')
    expect(base).toBeTypeOf('number')

    handleEvent(store, 'av1', {
      type: 'progressSample',
      scope: 'av1',
      ratio: 0.5,
      totalBytes: 100,
      speed: 100
    })
    // 进度回退（分 P 切换）也要重置，否则剩余时间沿用上一段的外推
    handleEvent(store, 'av1', {
      type: 'progressSample',
      scope: 'av1',
      ratio: 0.1,
      totalBytes: 100,
      speed: 100
    })

    expect(store.elapsedBase.get('av1')).toBeGreaterThanOrEqual(base!)
  })
})