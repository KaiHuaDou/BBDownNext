/** 下载目标识别，复刻 GUI UrlDetector 的逻辑与文案（前缀来源与 Core IdPrefix 一致）。 */

/** 已知前缀，前缀后必须紧跟数字（BV 号亦以数字开头）。 */
const KNOWN_PREFIXES: [string, string][] = [
  ['av', '视频（av 号）'],
  ['BV', '视频（BV 号）'],
  ['ep', '番剧（ep 号）'],
  ['ss', '番剧（ss 号）'],
  ['md', '番剧（md 号）'],
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

  if (text.toLowerCase().startsWith('https://live.bilibili.com')) {
    return '直播地址'
  }

  for (const [prefix, label] of KNOWN_PREFIXES) {
    if (startsWithId(text, prefix)) {
      return label
    }
  }

  return null
}

function describeUrl(text: string): string {
  const bv = /BV[0-9A-Za-z]+/.exec(text)
  if (bv?.[0]) {
    return `视频（${bv[0]}）`
  }

  if (/av[0-9]+/.test(text)) {
    return '视频（av 号）'
  }

  if (/ep[0-9]+/.test(text)) {
    return '番剧（ep 号）'
  }

  if (/ss[0-9]+/.test(text)) {
    return '番剧（ss 号）'
  }

  if (/opus[0-9]+/.test(text)) {
    return '专栏（opus 号）'
  }

  if (/cv[0-9]+/.test(text)) {
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
