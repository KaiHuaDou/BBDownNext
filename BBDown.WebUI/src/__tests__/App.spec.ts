import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'

import App from '../App.vue'

function mockFetch(): void {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      let url: string
      if (typeof input === 'string') {
        url = input
      } else if (input instanceof URL) {
        url = input.href
      } else {
        url = input.url
      }

      if (url.endsWith('/healthz')) {
        return Response.json({ status: 'ok', running: 0 })
      }

      if (url.endsWith('/api/v1/tasks')) {
        return Response.json({ running: [], finished: [] })
      }

      return new Response('not found', { status: 404 })
    })
  )
}

describe('App', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('渲染主界面布局', async () => {
    mockFetch()
    const wrapper = mount(App)
    await flushPromises()

    expect(wrapper.text()).toContain('目标')
    expect(wrapper.text()).toContain('未能识别')
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
    mockFetch()
    const wrapper = mount(App)
    await flushPromises()

    const input = wrapper.find('input[placeholder]')
    await input.setValue('BV1xx411c7mD')
    expect(wrapper.text()).toContain('✓ 视频（BV 号）')

    wrapper.unmount()
  })
})
