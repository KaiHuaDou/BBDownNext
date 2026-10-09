import { describe, expect, it, vi } from 'vitest'

import { connectTaskSocket, type SocketHandlers } from '../api/ws'
import type { ServeConfig } from '../api/client'

/** 记录全部回调，并可把待发消息投递给 ws 内部逻辑。 */
function handlers(): SocketHandlers & { dropped: string[]; statuses: (string | null)[] } {
  const dropped: string[] = []
  const statuses: (string | null)[] = []
  return {
    dropped,
    statuses,
    onEvent: vi.fn(),
    onSnapshot: vi.fn(),
    onTaskList: vi.fn(),
    onChoiceResult: vi.fn(),
    onStatus: (error) => statuses.push(error),
    onOpen: vi.fn(),
    onSubscribeError: vi.fn(),
    onFrameDropped: (reason) => dropped.push(reason)
  }
}

const CONFIG: ServeConfig = { baseUrl: '', token: '' }

/** 建一个只收不发、且不发 onopen 的假 WebSocket，返回用于投递消息的句柄。 */
function fakeWebSocket(): { sockets: { onmessage?: (m: MessageEvent) => void }[] } {
  const sockets: { onmessage?: (m: MessageEvent) => void }[] = []
  class StubWebSocket {
    static readonly OPEN = 1
    readonly readyState = 1

    onmessage?: (m: MessageEvent) => void
    onopen?: () => void
    onclose?: () => void
    onerror?: () => void

    constructor() {
      sockets.push(this)
    }

    send(): void {}

    close(): void {}
  }

  vi.stubGlobal('WebSocket', StubWebSocket)
  return { sockets }
}

describe('事件帧解析', () => {
  it('二进制帧与非法 JSON 上报丢弃，不改连接状态', () => {
    const { sockets } = fakeWebSocket()
    const seen = handlers()
    const socket = connectTaskSocket(CONFIG, seen)
    socket.connect()
    const live = sockets[0]!

    live.onmessage?.({ data: new ArrayBuffer(8) } as MessageEvent)
    live.onmessage?.({ data: 'not json' } as MessageEvent)
    live.onmessage?.({ data: '{"no":"kind"}' } as MessageEvent)

    expect(seen.dropped).toEqual([
      '收到非文本帧（object），已丢弃 1 帧',
      '事件帧不是合法 JSON，已丢弃 2 帧',
      '事件帧缺少 kind 字段，已丢弃 3 帧'
    ])
    // 连接本身正常，不并入 onStatus，否则界面会误显示为重连中
    expect(seen.statuses).toEqual([])
    expect(seen.onTaskList).not.toHaveBeenCalled()
  })

  it('解析成功一帧后丢弃计数归零', () => {
    const { sockets } = fakeWebSocket()
    const seen = handlers()
    const socket = connectTaskSocket(CONFIG, seen)
    socket.connect()
    const live = sockets[0]!

    live.onmessage?.({ data: 'nope' } as MessageEvent)
    live.onmessage?.({ data: '{"kind":"taskList","tasks":{"running":[],"finished":[]}}' } as MessageEvent)
    live.onmessage?.({ data: 'nope' } as MessageEvent)

    expect(seen.onTaskList).toHaveBeenCalledTimes(1)
    expect(seen.dropped).toEqual(['事件帧不是合法 JSON，已丢弃 1 帧', '事件帧不是合法 JSON，已丢弃 1 帧'])
  })

  it('缺必填字段的帧与未知帧类型一并计入丢弃', () => {
    const { sockets } = fakeWebSocket()
    const seen = handlers()
    const socket = connectTaskSocket(CONFIG, seen)
    socket.connect()
    const live = sockets[0]!

    live.onmessage?.({ data: '{"kind":"event"}' } as MessageEvent)
    live.onmessage?.({
      data: '{"kind":"snapshot","taskId":"av1","snapshot":{"scope":"下载"}}'
    } as MessageEvent)
    live.onmessage?.({ data: '{"kind":"choiceResult"}' } as MessageEvent)
    live.onmessage?.({ data: '{"kind":"taskList"}' } as MessageEvent)
    live.onmessage?.({ data: '{"kind":"future","taskId":"av1"}' } as MessageEvent)

    expect(seen.dropped).toEqual([
      'event 帧缺少 taskId 或 event 字段，已丢弃 1 帧',
      'snapshot 帧缺少 taskId 或 snapshot 字段，已丢弃 2 帧',
      'choiceResult 帧缺少 requestId 字段，已丢弃 3 帧',
      'taskList 帧缺少 tasks 字段，已丢弃 4 帧',
      '未知帧类型（future），已丢弃 5 帧'
    ])
    expect(seen.onEvent).not.toHaveBeenCalled()
    expect(seen.onSnapshot).not.toHaveBeenCalled()
    expect(seen.onChoiceResult).not.toHaveBeenCalled()
    expect(seen.onTaskList).not.toHaveBeenCalled()
    expect(seen.statuses).toEqual([])
  })

  it('字段齐全的帧仍按类型分发', () => {
    const { sockets } = fakeWebSocket()
    const seen = handlers()
    const socket = connectTaskSocket(CONFIG, seen)
    socket.connect()
    const live = sockets[0]!

    live.onmessage?.({
      data: '{"kind":"event","taskId":"av1","event":{"type":"message","text":"ok","time":""}}'
    } as MessageEvent)
    live.onmessage?.({
      data: '{"kind":"snapshot","taskId":"av1","snapshot":{"scope":"下载","ratio":0.5,"totalBytes":10,"speed":100}}'
    } as MessageEvent)

    expect(seen.onEvent).toHaveBeenCalledWith('av1', { type: 'message', text: 'ok', time: '' })
    expect(seen.onSnapshot).toHaveBeenCalledWith('av1', {
      scope: '下载',
      ratio: 0.5,
      totalBytes: 10,
      speed: 100
    })
    expect(seen.dropped).toEqual([])
  })
})