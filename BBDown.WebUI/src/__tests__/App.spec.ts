import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'

import App from '../App.vue'

/** jsdom 的 WebSocket 会真去连 ws://localhost；桩掉并记录构造参数，令用例对网络完全免疫。 */
function stubWebSocket(): { urls: string[]; protocols: (string | string[] | undefined)[] } {
  const urls: string[] = []
  const protocols: (string | string[] | undefined)[] = []
  class StubWebSocket {
    static readonly OPEN = 1

    constructor(url: string, protocol?: string | string[]) {
      urls.push(url)
      protocols.push(protocol)
    }

    send(): void {}

    close(): void {}
  }

  vi.stubGlobal('WebSocket', StubWebSocket)
  return { urls, protocols }
}

function mockFetch(): void {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url =
        typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
      return url.endsWith('/healthz')
        ? Response.json({ status: 'ok', running: 0 })
        : new Response('not found', { status: 404 })
    })
  )
}

describe('App', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('渲染主界面布局', async () => {
    stubWebSocket()
    mockFetch()
    const wrapper = mount(App)
    await flushPromises()

    expect(wrapper.text()).toContain('粘贴链接后将自动识别类型')
    expect(wrapper.text()).toContain('内容选项')
    expect(wrapper.text()).toContain('下载选项')
    expect(wrapper.text()).toContain('解析选项')
    expect(wrapper.text()).toContain('加入并执行')
    expect(wrapper.text()).toContain('加入队列')
    expect(wrapper.text()).toContain('重置选项')
    expect(wrapper.text()).toContain('日志')

    wrapper.unmount()
  })

  it('输入可识别的下载目标时显示识别提示', async () => {
    stubWebSocket()
    mockFetch()
    const wrapper = mount(App)
    await flushPromises()

    const input = wrapper.find('input[placeholder]')
    await input.setValue('BV1xx411c7mD')
    expect(wrapper.text()).toContain('✓ 视频（BV 号）')

    wrapper.unmount()
  })

  it('事件流建连到 /hubs/tasks，令牌走子协议头而非 URL', async () => {
    localStorage.setItem('bbdown.serveToken', 'secret-token')
    const sockets = stubWebSocket()
    mockFetch()
    const wrapper = mount(App)
    await flushPromises()

    expect(sockets.urls).toHaveLength(1)
    expect(sockets.urls[0]).toMatch(/\/hubs\/tasks$/)
    expect(sockets.urls[0]).not.toContain('secret-token')
    expect(sockets.protocols[0]).toEqual(['secret-token'])

    wrapper.unmount()
    localStorage.clear()
  })
})
