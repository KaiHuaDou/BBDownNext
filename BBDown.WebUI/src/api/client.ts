import { readLocalStorage, writeLocalStorage } from '../lib/storage'
/**
 * serve REST 客户端：地址与令牌配置持久化到 localStorage。
 * baseUrl 留空即直连本机 serve 默认地址（127.0.0.1:23333，默认免令牌且回环 Origin 默认可跨域）；
 * 若服务端以 --serve-token 启用了鉴权，则须在本页填入对应的 X-BBDown-Token 令牌（与回环 / 非回环无关）；跨机器访问时还须以 --cors-origin 允许本页来源。
 */
import type { DownloadTask, HealthStatus, ServeRequestOptions } from '../lib/types'

const BASE_URL_KEY = 'bbdown.serveBaseUrl'
const TOKEN_KEY = 'bbdown.serveToken'

/** 本机 serve 默认地址（BBDown serve；与 BBDownApiServer.DefaultListenUrl 一致）。 */
export const DEFAULT_BASE_URL = 'http://127.0.0.1:23333'

export interface ServeConfig {
  baseUrl: string
  token: string
}

/** baseUrl 留空归一化为本机 serve 默认地址，直连不依赖任何 dev server 代理。 */
export function resolveBaseUrl(baseUrl: string): string {
  // 内嵌托管：服务端注入 window.__BBDOWN_SERVE_EMBEDDED__，前端按同源调用 API（任意 --listen 均生效）
  if ((globalThis as { __BBDOWN_SERVE_EMBEDDED__?: boolean }).__BBDOWN_SERVE_EMBEDDED__) {
    return location.origin
  }

  return baseUrl.trim() || DEFAULT_BASE_URL
}

export function loadServeConfig(): ServeConfig {
  return {
    baseUrl: resolveBaseUrl(readLocalStorage(BASE_URL_KEY)),
    token: readLocalStorage(TOKEN_KEY)
  }
}

export function saveServeConfig(config: ServeConfig): void {
  writeLocalStorage(BASE_URL_KEY, config.baseUrl)
  writeLocalStorage(TOKEN_KEY, config.token)
}

/** 请求超时：serve 假死（进程挂起不响应）时避免连接状态停留在「已连接」。 */
const RequestTimeoutMs = 5000
/** 任务受理超时：服务端受理期解析 URL 可能触网（短链展开 / ss、md 换 season_id / 整页抓取），需更宽裕。 */
const SubmitTimeoutMs = 60000

/** 带超时的 fetch：超时抛「请求超时」错误（AbortController 中止）。 */
async function fetchWithTimeout(
  url: string,
  init: RequestInit,
  timeoutMs = RequestTimeoutMs
): Promise<Response> {
  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), timeoutMs)
  try {
    return await fetch(url, { ...init, signal: controller.signal })
  } catch (e) {
    if (controller.signal.aborted) {
      const err = new Error('请求超时（服务端无响应）')
      ;(err as { cause?: unknown }).cause = e
      throw err
    }

    throw e
  } finally {
    clearTimeout(timer)
  }
}

async function request<T>(config: ServeConfig, path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers)
  if (init?.body) {
    headers.set('Content-Type', 'application/json')
  }

  if (config.token) {
    headers.set('X-BBDown-Token', config.token)
  }

  const response = await fetchWithTimeout(
    `${resolveBaseUrl(config.baseUrl).replace(/\/+$/, '')}${path}`,
    {
      ...init,
      headers
    }
  )
  if (!response.ok) {
    throw new Error(`HTTP ${response.status}：${await response.text().catch(() => '')}`)
  }

  // 部分端点（移除 / 清空 / 停止等）仅返回 200 空响应体（Results.Ok()），无 JSON 可解析，直接给默认值
  const text = await response.text()
  if (text.length === 0) {
    return undefined as unknown as T
  }

  return JSON.parse(text) as T
}

/** 健康检查（保活轮询用）：探测 serve 是否存活（匿名放行）。 */
export function fetchHealth(config: ServeConfig): Promise<HealthStatus> {
  return request<HealthStatus>(config, '/healthz')
}

/**
 * 提交下载任务。202 受理新任务；200 命中已有任务；抛错携带状态码信息。
 * mode 为 enqueue 时附加 ?mode=enqueue：任务进入暂停态（待 start），不自动执行。
 */
export async function submitTask(
  config: ServeConfig,
  body: ServeRequestOptions,
  mode: 'execute' | 'enqueue' = 'execute'
): Promise<{ task: DownloadTask; duplicate: boolean }> {
  const baseUrl = resolveBaseUrl(config.baseUrl).replace(/\/+$/, '')
  const headers = new Headers({ 'Content-Type': 'application/json' })
  if (config.token) {
    headers.set('X-BBDown-Token', config.token)
  }

  const url = `${baseUrl}/api/v1/tasks${mode === 'enqueue' ? '?mode=enqueue' : ''}`
  const response = await fetchWithTimeout(
    url,
    {
      method: 'POST',
      headers,
      body: JSON.stringify(body)
    },
    SubmitTimeoutMs
  )
  if (response.status === 429) {
    throw new Error('任务队列已满（429）')
  }

  if (!response.ok) {
    throw new Error(`HTTP ${response.status}：${await response.text().catch(() => '')}`)
  }

  return { task: (await response.json()) as DownloadTask, duplicate: response.status === 200 }
}

/** 取消运行中 / 排队中任务。 */
export function stopTask(config: ServeConfig, id: string): Promise<void> {
  return request<void>(config, `/api/v1/tasks/${encodeURIComponent(id)}/stop`, { method: 'POST' })
}

/** 启动 enqueue 暂停的任务（投入执行队列）。 */
export function startTask(config: ServeConfig, id: string): Promise<void> {
  return request<void>(config, `/api/v1/tasks/${encodeURIComponent(id)}/start`, { method: 'POST' })
}

/** 移除指定已完成任务。 */
export function removeTask(config: ServeConfig, id: string): Promise<void> {
  return request<void>(config, `/api/v1/tasks/${encodeURIComponent(id)}`, { method: 'DELETE' })
}

/** 清空全部已完成任务。 */
export function clearFinished(config: ServeConfig): Promise<void> {
  return request<void>(config, '/api/v1/tasks/finished', { method: 'DELETE' })
}

/** 清空已失败的已完成任务。 */
export function clearFailed(config: ServeConfig): Promise<void> {
  return request<void>(config, '/api/v1/tasks/finished/failed', { method: 'DELETE' })
}

/** 扫码登录状态（与 serve QrLoginState 对齐，camelCase 序列化）。 */
export type QrLoginState = 'waitingScan' | 'waitingConfirm' | 'expired' | 'success' | 'failed'

export interface QrLoginStartRequest {
  channel: 'web' | 'tv' | 'app'
}

/** 扫码登录起点响应：二维码 PNG（base64）与轮询键。 */
export interface QrLoginStartResult {
  qrcodeKey: string
  qrPngBase64: string
  channel: string
}

/** 扫码登录状态轮询响应；success 时携带凭据（WEB 为 cookie，TV / APP 为 accessToken）。 */
export interface QrLoginStatusResult {
  state: QrLoginState
  accountName?: string | null
  cookie?: string | null
  accessToken?: string | null
  refreshToken?: string | null
  error?: string | null
}

/** 起点扫码登录：指定通道，返回二维码 PNG（base64）与轮询键。 */
export function fetchLoginQr(
  config: ServeConfig,
  channel: 'web' | 'tv' | 'app'
): Promise<QrLoginStartResult> {
  return request<QrLoginStartResult>(config, '/api/v1/login/qr', {
    method: 'POST',
    body: JSON.stringify({ channel } satisfies QrLoginStartRequest)
  })
}

/** 轮询扫码登录状态；success 后凭据由本函数返回一次，调用方负责保存与联动通道。 */
export function pollLoginStatus(
  config: ServeConfig,
  qrcodeKey: string
): Promise<QrLoginStatusResult> {
  return request<QrLoginStatusResult>(config, `/api/v1/login/qr/${encodeURIComponent(qrcodeKey)}`)
}
