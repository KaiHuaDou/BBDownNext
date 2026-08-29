/** 本地存储访问封装：非浏览器环境（测试 / 私有模式禁用存储）回落空串，避免抛错。 */

export function readLocalStorage(key: string): string {
  try {
    return typeof localStorage === 'undefined' ? '' : (localStorage.getItem(key) ?? '')
  } catch {
    return ''
  }
}

export function writeLocalStorage(key: string, value: string): void {
  try {
    if (typeof localStorage !== 'undefined') {
      localStorage.setItem(key, value)
    }
  } catch {
    // 存储不可用时静默忽略
  }
}
