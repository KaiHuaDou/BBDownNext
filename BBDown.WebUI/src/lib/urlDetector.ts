/** 下载目标识别，复刻 GUI UrlDetector 的逻辑与文案（前缀来源与 Core IdPrefix 一致）。 */

/** 已知前缀，前缀后必须紧跟数字（BV 号固定以 BV1 开头，单独判定）。 */
const KNOWN_PREFIXES: [string, string][] = [
  ['av', '视频（av 号）'],
  ['ep', '番剧（ep 号）'],
  ['ss', '番剧（ss 号）'],
  ['md', '番剧（md 号）'],
  ['cheese/ep', '课程（ep 号）'],
  ['cheese/ss', '课程（ss 号）'],
  ['opus', '专栏（opus 号）'],
  ['cv', '专栏（cv 号）'],
  ['space', '用户空间'],
  ['live', '直播间（live 号）']
]

/** 识别输入文本，返回可读描述；无法识别返回 null。 */
export function describeTarget(input?: string): string | null {
  const text = input?.trim() ?? ''
  if (text.length === 0) {
    return null
  }

  const description = matchKnownPrefix(text)
  if (description !== null) {
    return description
  }

  if (/^[0-9]+$/.test(text)) {
    return '视频（av 号）'
  }

  try {
    const uri = new URL(text)
    if (uri.protocol === 'http:' || uri.protocol === 'https:') {
      return describeUrl(text)
    }
  } catch {
    // 非合法 URL，继续走 null
  }

  return null
}

function matchKnownPrefix(text: string): string | null {
  if (text.toLowerCase().startsWith('https://www.bilibili.com/watchlater')) {
    return '稍后再看列表'
  }

  // 裸 watchlater 简写与 URL 形态同义（与 Core InputResolver 的相等判定对齐）
  if (text.toLowerCase() === 'watchlater') {
    return '稍后再看列表'
  }

  if (text.toLowerCase().startsWith('https://live.bilibili.com')) {
    return '直播地址'
  }

  // BV 号固定以 BV1 开头且为纯 base58 字符，与 Core 的 bv1 前缀判定一致（BV2 等非 BV 号不应放行）
  if (/^bv1[0-9a-z]+$/i.test(text)) {
    return '视频（BV 号）'
  }

  for (const [prefix, label] of KNOWN_PREFIXES) {
    if (startsWithId(text, prefix)) {
      return label
    }
  }

  return null
}

function describeUrl(text: string): string {
  if (/\/cheese\//.test(text)) {
    return '课程地址'
  }

  const bv = /BV[0-9A-Za-z]+/.exec(text)
  if (bv?.[0]) {
    return `视频（${bv[0]}）`
  }

  // av / ep / ss 的路径形态为 .../video/av123、.../bangumi/play/ep123 等，关键字与数字直接相连
  if (/av[0-9]+/i.test(text)) {
    return '视频（av 号）'
  }

  if (/ep[0-9]+/i.test(text)) {
    return '番剧（ep 号）'
  }

  if (/ss[0-9]+/i.test(text)) {
    return '番剧（ss 号）'
  }

  // opus / cv 的路径形态为 .../opus/123...、.../cv/123...，关键字与数字间带斜杠（裸形态 opus123 同样成立）
  if (/opus\/?[0-9]+/i.test(text)) {
    return '专栏（opus 号）'
  }

  if (/cv\/?[0-9]+/i.test(text)) {
    return '专栏（cv 号）'
  }

  return '视频地址'
}

function startsWithId(text: string, prefix: string): boolean {
  if (text.length <= prefix.length || !text.toLowerCase().startsWith(prefix.toLowerCase())) {
    return false
  }

  return /\d/.test(text[prefix.length] ?? '')
}
