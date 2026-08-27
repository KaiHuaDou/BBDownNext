/** 直播清晰度档位：与 Core LiveQuality.Levels 对齐（高 → 低）。经 serve 请求契约 exposed，WebUI 可选择。 */

export const LIVE_QUALITY_LEVELS: { qn: number; name: string }[] = [
  { qn: 30000, name: '杜比' },
  { qn: 20000, name: '4K' },
  { qn: 15000, name: '2K' },
  { qn: 10000, name: '原画' },
  { qn: 400, name: '蓝光' },
  { qn: 250, name: '超清' },
  { qn: 150, name: '高清' },
  { qn: 80, name: '流畅' }
]

export const DEFAULT_LIVE_QUALITY = 10000
