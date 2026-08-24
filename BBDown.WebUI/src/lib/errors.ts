/** 安全提取异常消息：非 Error 抛出值（如 throw 字符串）回落 String 化。 */
export function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}
