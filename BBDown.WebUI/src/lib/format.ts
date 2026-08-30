/** 速度 / 剩余时间格式化，复刻 Core Utils 的文案（FormatFileSize / FormatTime / FormatEta）。 */

export function formatFileSize(fileSize: number): string {
  if (fileSize < 0) {
    throw new RangeError('fileSize 不能为负')
  }

  if (fileSize >= 1024 * 1024 * 1024) {
    return `${(fileSize / (1024 * 1024 * 1024)).toFixed(2)} GB`
  }

  if (fileSize >= 1024 * 1024) {
    return `${(fileSize / (1024 * 1024)).toFixed(2)} MB`
  }

  if (fileSize >= 1024) {
    return `${(fileSize / 1024).toFixed(2)} KB`
  }

  return `${Math.round(fileSize)} bytes`
}

export function formatSpeed(bytesPerSecond: number): string {
  return `${formatFileSize(Math.round(bytesPerSecond))}/s`
}

/** 秒数 → mmss / hhmmss 文本（Core FormatTime 非 absolute 分支）。 */
export function formatTime(totalSeconds: number): string {
  const ts = Math.max(0, Math.round(totalSeconds))
  const hours = Math.floor(ts / 3600)
  const minutes = Math.floor((ts % 3600) / 60)
  const seconds = ts % 60
  const pad = (n: number): string => String(n).padStart(2, '0')
  return hours === 0
    ? `${pad(minutes)}m${pad(seconds)}s`
    : `${hours}h${pad(minutes)}m${pad(seconds)}s`
}

/** 按已下载比例与当前速率外推剩余时间；比例过低（<=2%）时发散，返回 null 不显示。 */
export function formatEta(ratio: number, speed: number, downloadedBytes: number): string | null {
  if (ratio <= 0.02 || speed <= 0) {
    return null
  }

  const remainingBytes = (downloadedBytes * (1 - ratio)) / ratio
  return formatTime(remainingBytes / speed)
}

/** 运行中任务的详情文本：优先阶段文本（直播等），否则速度 + 剩余时间。 */
export function buildDetail(
  ratio: number,
  speed: number,
  totalBytes: number,
  stageDetail?: string
): string {
  if (stageDetail) {
    return speed > 0 ? `${stageDetail} | ${formatSpeed(speed)}` : stageDetail
  }

  const speedText = speed > 0 ? formatSpeed(speed) : ''
  const eta = formatEta(ratio, speed, totalBytes)
  if (eta) {
    return speedText.length === 0 ? `剩余 ${eta}` : `${speedText} · 剩余 ${eta}`
  }

  return speedText
}
