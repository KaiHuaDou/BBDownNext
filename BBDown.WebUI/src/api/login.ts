/**
 * 登录端点预留：serve 当前未提供扫码登录接口（登录仅能在 CLI / 桌面 GUI 完成），
 * 此模块为将来 serve 增加登录端点时预留实现位置；当前所有函数一律抛「未实现」。
 * 登录态凭据（cookie / accessToken）持久化于浏览器 localStorage（明文），随任务请求经
 * ServeRequestOptions 附带。非回环部署时须注意 XSS 可读取该凭据，仅建议本机 / 可信环境使用。
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

/** 登录通道枚举（与 GUI LoginChannel 对齐）。 */
export const LOGIN_CHANNELS: { value: LoginChannel; label: string }[] = [
  { value: 'web', label: 'WEB' },
  { value: 'tv', label: 'TV' },
  { value: 'app', label: 'APP' }
]

/** 登录二维码数据结构（预留：serve 增加登录端点后填充）。 */
export interface LoginQr {
  /** 二维码内容 url，前端渲染为图片 */
  url: string
  /** 轮询登录状态的凭据键 */
  qrcodeKey: string
}

function notImplemented(name: string): never {
  throw new Error(
    `登录端点尚未实现（${name}）：serve 当前未提供扫码登录接口，请在 CLI 或桌面 GUI 完成登录`
  )
}

/** 获取指定通道的登录二维码（预留，未实现）。 */
export function fetchLoginQr(channel: LoginChannel): Promise<LoginQr> {
  void channel
  return notImplemented('fetchLoginQr')
}

/** 轮询扫码登录状态（预留，未实现）；返回登录凭据后由调用方保存。 */
export function pollLoginStatus(qrcodeKey: string): Promise<Credential> {
  void qrcodeKey
  return notImplemented('pollLoginStatus')
}
