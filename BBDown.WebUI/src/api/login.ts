/**
 * 登录态凭据（cookie / accessToken）持久化于浏览器 localStorage（明文），
 * 随任务请求经 ServeRequestOptions 附带。非回环部署时须注意 XSS 可读取该凭据，仅建议本机 / 可信环境使用。
 * 扫码登录的二维码获取与状态轮询走 serve REST 端点（见 api/client.ts fetchLoginQr / pollLoginStatus）。
 */

import { readLocalStorage, writeLocalStorage } from '../lib/storage'

export type LoginChannel = 'web' | 'tv' | 'app'

const COOKIE_KEY = 'bbdown.credential.cookie'
const TOKEN_KEY = 'bbdown.credential.accessToken'

/** 本地保存的登录凭据；空串表示未配置。 */
export interface Credential {
  cookie: string
  accessToken: string
}

export function loadCredential(): Credential {
  return {
    cookie: readLocalStorage(COOKIE_KEY),
    accessToken: readLocalStorage(TOKEN_KEY)
  }
}

export function saveCredential(credential: Credential): void {
  writeLocalStorage(COOKIE_KEY, credential.cookie)
  writeLocalStorage(TOKEN_KEY, credential.accessToken)
}

/** 登录通道选择（与 GUI LoginChannel 对齐）。 */
export const LOGIN_CHANNELS: { value: LoginChannel; label: string }[] = [
  { value: 'web', label: 'WEB' },
  { value: 'tv', label: 'TV' },
  { value: 'app', label: 'APP' }
]
