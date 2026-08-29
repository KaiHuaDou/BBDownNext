import { readLocalStorage, writeLocalStorage } from '../lib/storage'
/**
 * serve REST 客户端：地址与令牌配置持久化到 localStorage。
 * baseUrl 留空即直连本机 serve 默认地址（127.0.0.1:23333，默认免令牌且回环 Origin 默认可跨域）；
 * 若服务端以 --serve-token 启用了鉴权，则须在本页填入对应的 X-BBDown-Token 令牌（与回环 / 非回环无关）；跨机器访问时还须以 --cors-origin 允许本页来源。
 */
import type { DownloadTask, HealthStatus, ServeRequestOptions, TaskSnapshot } from '../lib/types'

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

/** 带超时的 fetch：超时抛「请求超时」错误（AbortController 中止）。 */
async function fetchWithTimeout(url: string, init: RequestInit): Promise<Response> {
  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), RequestTimeoutMs)
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

  return (await response.json()) as T
}

/** 健康检查：探测 serve 是否存活（匿名放行）。 */
export function fetchHealth(config: ServeConfig): Promise<HealthStatus> {
  return request<HealthStatus>(config, '/healthz')
}

/** 整体快照：运行中 + 已完成任务。 */
export function fetchTasks(config: ServeConfig): Promise<TaskSnapshot> {
  return request<TaskSnapshot>(config, '/api/v1/tasks')
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
  const response = await fetchWithTimeout(url, {
    method: 'POST',
    headers,
    body: JSON.stringify(body)
  })
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
