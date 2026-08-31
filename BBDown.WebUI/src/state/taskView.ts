import { buildDetail } from '../lib/format'
import type { DownloadTask, TaskView, TaskViewStatus } from '../lib/types'

/** 取消态只消费服务端的结构化 isCancelled 字段，不解析错误文案。 */
function statusOf(task: DownloadTask): { status: TaskViewStatus; statusText: string } {
  if (task.status === 'Pending') {
    return { status: 'Pending', statusText: '待启动' }
  }

  if (task.status === 'Queued') {
    return { status: 'Waiting', statusText: '等待中' }
  }

  if (task.status === 'Running') {
    return { status: 'Running', statusText: '运行中' }
  }

  if (task.isSuccessful) {
    return { status: 'Success', statusText: '成功' }
  }

  if (task.isCancelled) {
    return { status: 'Cancelled', statusText: '已取消' }
  }

  return { status: 'Failed', statusText: '失败' }
}

/** 由规范 id 前缀推导资源类型中文（与 Core ResourceId 的规范形态及 TypePrefixes 对齐）。 */
export function kindOfId(id: string): string {
  const lowered = id.toLowerCase()
  const starts = (p: string): boolean => lowered.startsWith(p)
  if (starts('opus') || starts('cv')) {
    return '专栏'
  }

  if (starts('live')) {
    return '直播'
  }

  if (starts('ep') || starts('ss') || starts('season') || starts('md')) {
    return '番剧'
  }

  if (starts('cheeseep') || starts('cheeseseason')) {
    return '课程'
  }

  if (starts('space')) {
    return '空间'
  }

  if (starts('fav')) {
    return '收藏'
  }

  if (starts('medialist')) {
    return '合集'
  }

  if (starts('series')) {
    return '系列'
  }

  if (starts('watchlater')) {
    return '稍后再看'
  }

  return '视频'
}

/** 由 serve 任务对象构造前端视图。 */
export function toView(task: DownloadTask): TaskView {
  const { status, statusText } = statusOf(task)
  const isLive = /live\.bilibili\.com|^live\d+/i.test(task.url)
  const detail =
    status === 'Running' && task.progress > 0
      ? buildDetail(task.progress, task.downloadSpeed, task.totalDownloadedBytes)
      : ''
  return {
    id: task.id,
    url: task.url,
    title: task.title ?? undefined,
    status,
    statusText,
    progress: task.progress,
    detail,
    errorMessage: task.errorMessage ?? undefined,
    savePaths: task.savePaths,
    isLive,
    kind: kindOfId(task.id)
  }
}
