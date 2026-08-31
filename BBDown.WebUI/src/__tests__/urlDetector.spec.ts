import { describe, expect, it } from 'vitest'

import { describeTarget } from '../lib/urlDetector'

describe('describeTarget', () => {
  it('识别各类 ID 前缀', () => {
    expect(describeTarget('av170001')).toBe('视频（av 号）')
    expect(describeTarget('BV1xx411c7mD')).toBe('视频（BV 号）')
    expect(describeTarget('ep2539')).toBe('番剧（ep 号）')
    expect(describeTarget('ss2539')).toBe('番剧（ss 号）')
    expect(describeTarget('md123')).toBe('番剧（md 号）')
    expect(describeTarget('opus123')).toBe('专栏（opus）')
    expect(describeTarget('cv123')).toBe('专栏（cv）')
    expect(describeTarget('space402787936')).toBe('用户空间')
    expect(describeTarget('live123456')).toBe('直播间（live 号）')
    expect(describeTarget('cheese/ep12345')).toBe('课程（ep 号）')
    expect(describeTarget('cheese/ss2539')).toBe('课程（ss 号）')
  })

  it('裸合理简写与 URL 同义', () => {
    expect(describeTarget('watchlater')).toBe('稍后再看列表')
  })

  it('裸数字视为 av 号', () => {
    expect(describeTarget('170001')).toBe('视频（av 号）')
  })

  it('识别 URL', () => {
    expect(describeTarget('https://www.bilibili.com/video/BV1xx411c7mD')).toBe(
      '视频（BV1xx411c7mD）'
    )
    expect(describeTarget('https://www.bilibili.com/video/av170001')).toBe('视频（av 号）')
    expect(describeTarget('https://www.bilibili.com/bangumi/play/ep2539')).toBe('番剧（ep 号）')
    expect(describeTarget('https://www.bilibili.com/read/cv123456')).toBe('专栏（cv）')
    expect(describeTarget('https://www.bilibili.com/opus/1226618629457444872')).toBe('专栏（opus）')
    expect(describeTarget('https://www.bilibili.com/cheese/play/ep12345')).toBe('课程地址')
    expect(describeTarget('https://live.bilibili.com/12345')).toBe('直播地址')
    expect(describeTarget('https://www.bilibili.com/watchlater')).toBe('稍后再看列表')
  })

  it('识别集合简写与集合 URL', () => {
    expect(describeTarget('rl75249')).toBe('文集')
    expect(describeTarget('readlist75249')).toBe('文集')
    expect(describeTarget('spaceOpus213741')).toBe('空间图文投稿')
    expect(describeTarget('spaceAudio213741')).toBe('空间音频投稿')
    expect(describeTarget('spaceDynamic213741')).toBe('空间动态（图文）')
    expect(describeTarget('https://www.bilibili.com/read/readlist/rl75249')).toBe('文集地址')
    expect(describeTarget('https://space.bilibili.com/213741/upload/opus')).toBe('空间图文投稿地址')
    expect(describeTarget('https://space.bilibili.com/213741/upload/audio')).toBe('空间音频投稿地址')
    expect(describeTarget('https://space.bilibili.com/213741/audio')).toBe('空间音频投稿地址')
    expect(describeTarget('https://space.bilibili.com/213741/dynamic')).toBe('空间动态地址（图文）')
  })

  it('非空间域的 /audio 路径不误标为空间音频', () => {
    // 单音频页 www.bilibili.com/audio/au123 未被空间子页识别覆盖（core 同样不命中），走 URL 兜底
    expect(describeTarget('https://www.bilibili.com/audio/au123')).toBe('视频地址')
  })

  it('无法识别时返回 null', () => {
    expect(describeTarget('')).toBeNull()
    expect(describeTarget('   ')).toBeNull()
    expect(describeTarget('随便什么')).toBeNull()
    expect(describeTarget('av')).toBeNull()
    expect(describeTarget('ep')).toBeNull()
    expect(describeTarget('live')).toBeNull()
    expect(describeTarget('BV2xx411c7mD')).toBeNull()
  })
})
