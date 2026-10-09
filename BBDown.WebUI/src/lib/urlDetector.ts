/** 下载目标识别，复刻 GUI UrlDetector 的逻辑与文案（前缀来源与 Core IdPrefix 一致）。 */

/** 已知前缀，前缀后必须紧跟数字（BV 号固定以 BV1 开头，单独判定）。集合简写在 space 之前（前缀更长）。 */
const KNOWN_PREFIXES: [string, string][] = [
  ['av', '视频（av 号）'],
  ['ep', '番剧（ep 号）'],
  ['ss', '番剧（ss 号）'],
  ['md', '番剧（md 号）'],
  ['cheese/ep', '课程（ep 号）'],
  ['cheese/ss', '课程（ss 号）'],
  ['opus', '专栏（opus）'],
  ['cv', '专栏（cv）'],
  ['readlist', '文集'],
  ['rl', '文集'],
  ['spaceOpus', '空间图文投稿'],
  ['spaceAudio', '空间音频投稿'],
  ['spaceDynamic', '空间动态'],
  ['au', '音频（au 号）'],
  ['space', '用户空间'],
  ['live', '直播间（live 号）']
]

const BILI_HOST = 'www.bilibili.com'
const SPACE_HOST = 'space.bilibili.com'
const LIVE_HOSTS = ['live.bilibili.com', 'm.live.bilibili.com']

/** 识别输入文本，返回可读描述；无法识别返回 null。 */
export function describeTarget(input?: string): string | null {
  const text = input?.trim() ?? ''
  if (text.length === 0) {
    return null
  }

  // 域名与路径只在解析出的 URL 上判定：原始串上做子串比对会让 evil.com/?x=live.bilibili.com/1 蒙混过关
  const url = parseHttpUrl(text)

  const description = matchKnownPrefix(text, url)
  if (description !== null) {
    return description
  }

  if (/^[0-9]+$/.test(text)) {
    return '视频（av 号）'
  }

  return url === null ? null : describeUrl(url)
}

/**
 * 解析为 http / https 的 URL。协议相对与裸域名按 https 补全（与 Core LiveInputResolver 同形），
 * 其余（含非 bilibili 域的裸写法）返回 null
 */
function parseHttpUrl(text: string): URL | null {
  if (/^(?:m\.)?(?:live|space|www)\.bilibili\.com\//i.test(text)) {
    return parseHttpUrl(`https://${text}`)
  }

  const candidate = text.startsWith('//') ? `https:${text}` : text
  try {
    const url = new URL(candidate)
    return url.protocol === 'http:' || url.protocol === 'https:' ? url : null
  } catch {
    return null
  }
}

function matchKnownPrefix(text: string, url: URL | null): string | null {
  // 裸简写与 URL 形式同义（与 Core InputResolver 的相等判定及 /watchlater 路径判定相同）
  if (text.toLowerCase() === 'watchlater') {
    return '稍后再看列表'
  }

  // 稍后再看：路径 /watchlater 或 /list/watchlater，或 www.bilibili.com/?page=watchlater
  if (url !== null && isHost(url, BILI_HOST)) {
    const path = url.pathname.toLowerCase()
    if (
      path.startsWith('/watchlater') ||
      path.startsWith('/list/watchlater') ||
      url.searchParams.get('page')?.toLowerCase() === 'watchlater'
    ) {
      return '稍后再看列表'
    }
  }

  // 直播间：带协议、协议相对与裸域名三种形式（GUI 委托给 Core LiveInputResolver，故一并支持）
  if (url !== null && LIVE_HOSTS.some((host) => isHost(url, host))) {
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

function isHost(url: URL, host: string): boolean {
  return url.host.toLowerCase() === host
}

function describeUrl(url: URL): string {
  // 判定基准取 host + pathname，不含 query 与 fragment：查询串与片段里的同名片段不应影响类型判定
  const path = url.pathname.toLowerCase()

  if (path.includes('/cheese/')) {
    return '课程地址'
  }

  if (path.includes('/read/readlist/')) {
    return '文集地址'
  }

  // 空间子页限定 host（与 Core 的 TryParseCollection 守卫一致），非空间域的 /audio 等路径不误标
  const spaceHost = isHost(url, SPACE_HOST)
  if (spaceHost && path.includes('/upload/opus')) {
    return '空间图文投稿地址'
  }

  // 旧版音频页 space.bilibili.com/{mid}/audio 与新版 /upload/audio 同义（/audio 判定两者通吃）
  if (spaceHost && path.includes('/audio')) {
    return '空间音频投稿地址'
  }

  if (spaceHost && path.includes('/dynamic')) {
    return '空间动态地址'
  }

  // 合集 / 系列：space lists 页（?type=series 为系列，其余按合集）、channel 页、老版 medialist/ml 分享链接
  if (spaceHost && path.includes('/lists/')) {
    return url.searchParams.get('type')?.toLowerCase() === 'series' ? '系列地址' : '合集地址'
  }

  if (path.includes('/channel/collectiondetail')) {
    return '合集地址'
  }

  if (path.includes('/channel/seriesdetail')) {
    return '系列地址'
  }

  if (/medialist\/(?:play|detail)\/ml[0-9]+/.test(path)) {
    return '合集地址'
  }

  // 单音频页 www.bilibili.com/audio/au12345（space 域的 /audio 列表页已在上面先行识别）
  if (path.includes('/audio/au')) {
    return '音频地址（au 号）'
  }

  const bv = /BV[0-9A-Za-z]+/.exec(url.pathname)
  if (bv?.[0]) {
    return `视频（${bv[0]}）`
  }

  // av / ep / ss 的路径形式为 .../video/av123、.../bangumi/play/ep123 等，关键字与数字直接相连
  if (/av[0-9]+/.test(path)) {
    return '视频（av 号）'
  }

  if (/ep[0-9]+/.test(path)) {
    return '番剧（ep 号）'
  }

  if (/ss[0-9]+/.test(path)) {
    return '番剧（ss 号）'
  }

  // opus / cv 的路径形式为 .../opus/123...、.../cv/123...，关键字与数字间带斜杠（裸写形式 opus123 同样成立）
  if (/opus\/?[0-9]+/.test(path)) {
    return '专栏（opus）'
  }

  if (/cv\/?[0-9]+/.test(path)) {
    return '专栏（cv）'
  }

  return '视频地址'
}

function startsWithId(text: string, prefix: string): boolean {
  if (text.length <= prefix.length || !text.toLowerCase().startsWith(prefix.toLowerCase())) {
    return false
  }

  return /\d/.test(text[prefix.length] ?? '')
}