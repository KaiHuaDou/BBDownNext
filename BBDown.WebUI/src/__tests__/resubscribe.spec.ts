import { beforeEach, describe, expect, it, vi } from 'vitest'

import { resubscribe, syncSubscriptions } from '../state/snapshot'
import { createStore, type TaskStore } from '../state/store'
import type { TaskSocket } from '../api/ws'
import type { TaskView } from '../lib/types'

function fakeSocket( ): TaskSocket {
  return {
    connect: ( ) => { },
    close: ( ) => { },
    subscribe: vi.fn( ),
    unsubscribe: vi.fn( ),
    submitChoice: vi.fn( )
  }
}

function running( id: string ): TaskView {
  return { ...baseView(id), status: 'Running', progress: 0, detail: '' }
}

function finished( id: string ): TaskView {
  return { ...baseView(id), status: 'Success', progress: 1, detail: '' }
}

function baseView( id: string ): TaskView {
  return {
    id,
    url: `https://www.bilibili.com/video/${id}`,
    status: 'Waiting',
    statusText: '',
    progress: 0,
    detail: '',
    savePaths: [],
    isLive: false,
    kind: '视频'
  }
}

function storeWith( tasks: TaskView[] ): TaskStore {
  const store = createStore()
  store.tasks.value = tasks
  store.socket = fakeSocket( )
  return store
}

describe('重连后重建订阅', ( ) => {
  beforeEach(( ) => {
    vi.clearAllMocks( )
  })

  it('丢弃本地订阅记录并按运行中任务重发', ( ) => {
    const store = storeWith([running('av1'), finished('av2')])
    // 模拟重连前的状态：本地记录里有已订阅任务，新 socket 上却什么都没有
    store.subscribed.add('av1')
    store.subscribed.add('av2')

    resubscribe(store)

    // av2 已结束，不再订阅；av1 必须重发一次（重连后的新 socket 上没有任何服务端订阅）
    expect(store.subscribed.has('av1')).toBe(true)
    expect(store.subscribed.has('av2')).toBe(false)
    expect(store.socket!.subscribe).toHaveBeenCalledTimes(1)
    expect(store.socket!.subscribe).toHaveBeenCalledWith('av1')
  })

  it('无 socket 时只清记录不抛异常', ( ) => {
    const store = storeWith([running('av1')])
    store.socket = null
    store.subscribed.add('av1')

    expect(( ) => resubscribe(store)).not.toThrow( )
    expect(store.subscribed.size).toBe(0)
  })

  it('syncSubscriptions 对已订阅任务不重复发送', ( ) => {
    const store = storeWith([running('av1')])
    store.subscribed.add('av1')

    syncSubscriptions(store, ['av1'])

    expect(store.socket!.subscribe).not.toHaveBeenCalled( )
  })
} )