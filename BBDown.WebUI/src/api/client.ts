import type { DownloadTask, HealthStatus, ServeRequestOptions, TaskSnapshot } from '../lib/types'

/**
 * serve REST 客户端：地址与令牌配置持久化到 localStorage，
 * 回环地址（默认）免令牌，非回环需 X-BBDown-Token 头。
 */

const BASE_URL_KEY = 'bbdown.serveBaseUrl'
const TOKEN_KEY = 'bbdown.serveToken'

export const DEFAULT_BASE_URL = 'http://127.0.0.1:23333'

export interface ServeConfig {
  baseUrl: string
  token: string
}

export function loadServeConfig(): ServeConfig {
  return {
    baseUrl: localStorage.getItem(BASE_URL_KEY) ?? DEFAULT_BASE_URL,
    token: localStorage.getItem(TOKEN_KEY) ?? ''
  }
}

export function saveServeConfig(config: ServeConfig): void {
  localStorage.setItem(BASE_URL_KEY, config.baseUrl)
  localStorage.setItem(TOKEN_KEY, config.token)
}

async function request<T>(config: ServeConfig, path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers)
  if (init?.body) {
    headers.set('Content-Type', 'application/json')
  }

  if (config.token) {
    headers.set('X-BBDown-Token', config.token)
  }

  const response = await fetch(`${config.baseUrl.replace(/\/+$/, '')}${path}`, { ...init, headers })
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
 */
export async function submitTask(
  config: ServeConfig,
  body: ServeRequestOptions
): Promise<{ task: DownloadTask; duplicate: boolean }> {
  const baseUrl = config.baseUrl.replace(/\/+$/, '')
  const headers = new Headers({ 'Content-Type': 'application/json' })
  if (config.token) {
    headers.set('X-BBDown-Token', config.token)
  }

  const response = await fetch(`${baseUrl}/api/v1/tasks`, {
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
