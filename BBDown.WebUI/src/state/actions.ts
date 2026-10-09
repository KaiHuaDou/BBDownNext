import {
  clearFailed as clearFailedRemote,
  clearFinished,
  removeTask,
  saveServeConfig,
  startTask,
  stopTask,
  submitTask,
  type ServeConfig
} from '../api/client'
import { errorMessage } from '../lib/errors'
import { toServeRequest, type TaskOptions } from '../lib/options'
import type { TaskView } from '../lib/types'
import { startSocket } from './connection'
import { appendLog } from './snapshot'
import type { TaskStore } from './store'
import type { PendingAsk } from './types'

/**
 * 提交任务；成功则记录选项快照供重试。mode 为 enqueue 时进入暂停态（待 start）。
 * 返回受理结果供调用方区分新受理与命中已有任务；抛错表示提交失败。
 */
export async function submitTaskAction(
  store: TaskStore,
  options: TaskOptions,
  url: string,
  mode: 'execute' | 'enqueue' = 'execute'
): Promise<{ taskId: string; duplicate: boolean }> {
  const { task, duplicate } = await submitTask(
    store.config.value,
    toServeRequest(options, url),
    mode
  )

  // 提交在途时组件可能已卸载，选项快照与日志已无接收方
  if (store.disposed) {
    return { taskId: task.id, duplicate }
  }

  store.submittedOptions.set(task.id, { ...options })
  appendLog(store, duplicate ? `任务已存在：${url}` : `任务已受理：${url}`)
  return { taskId: task.id, duplicate }
}

/** 停止任务：直播任务为「停止录制并合并」，其余为取消运行中 / 排队中任务。 */
export async function stop(store: TaskStore, view: TaskView): Promise<void> {
  try {
    await stopTask(store.config.value, view.id)
    appendLog(store, `任务${view.id} 已请求停止`)
  } catch (e) {
    appendLog(store, `停止失败：${errorMessage(e)}`, true)
  }
}

/** 启动 enqueue 暂停的任务（投入执行队列）。 */
export async function start(store: TaskStore, view: TaskView): Promise<void> {
  try {
    await startTask(store.config.value, view.id)
    appendLog(store, `任务${view.id} 已请求启动`)
  } catch (e) {
    appendLog(store, `启动失败：${errorMessage(e)}`, true)
  }
}

/** 移除收尾态任务（enqueue 暂停 / 已结束）；运行与排队中的任务需先取消。 */
export async function remove(store: TaskStore, view: TaskView): Promise<void> {
  if (view.status === 'Running' || view.status === 'Waiting') {
    appendLog(store, '进行中的任务请先取消', true)
    return
  }

  try {
    await removeTask(store.config.value, view.id)
  } catch (e) {
    appendLog(store, `移除失败：${errorMessage(e)}`, true)
  }
}

/** 继续：用提交时的选项快照重新提交；无快照时回落当前面板选项。 */
export async function retry(store: TaskStore, view: TaskView, fallback: TaskOptions): Promise<void> {
  try {
    await submitTaskAction(store, store.submittedOptions.get(view.id) ?? fallback, view.url)
  } catch (e) {
    appendLog(store, `继续失败：${errorMessage(e)}`, true)
  }
}

/** 清空全部已完成任务（保留运行中 / 等待中）。 */
export async function clearAll(store: TaskStore): Promise<void> {
  try {
    await clearFinished(store.config.value)
  } catch (e) {
    appendLog(store, `清空失败：${errorMessage(e)}`, true)
  }
}

/** 清空已失败的已完成任务。 */
export async function clearFailed(store: TaskStore): Promise<void> {
  try {
    await clearFailedRemote(store.config.value)
  } catch (e) {
    appendLog(store, `清空失败：${errorMessage(e)}`, true)
  }
}

/**
 * 应答选项请求。服务端 choiceResult 回来前保留在挂起列表（标为已应答），
 * 否则帧丢失时弹窗已消失而服务端仍在等应答，下载会挂到 AskTimeout。
 */
export function answerAsk(store: TaskStore, ask: PendingAsk, choice: string): void {
  store.socket?.submitChoice(ask.taskId, ask.requestId, choice)
  store.answeredAsks.set(ask.requestId, ask.taskId)
  store.pendingAsks.value = store.pendingAsks.value.map((a) =>
    a.requestId === ask.requestId ? { ...a, submitted: true } : a
  )
}

/** 服务端确认应答结果：成功才移除弹窗，失败恢复可应答状态。 */
export function settleAsk(store: TaskStore, requestId: string, ok: boolean, error?: string): void {
  const ask = store.pendingAsks.value.find((a) => a.requestId === requestId)
  if (!ask) {
    return
  }

  if (ok) {
    store.pendingAsks.value = store.pendingAsks.value.filter((a) => a.requestId !== requestId)
    return
  }

  store.pendingAsks.value = store.pendingAsks.value.map((a) =>
    a.requestId === requestId ? { ...a, submitted: false } : a
  )
  appendLog(store, `选项应答失败（${requestId}）：${error ?? '未知原因'}`, true)
}

/**
 * 更新连接配置：持久化并重建事件流。
 * 重建前对挂起提问按默认项应答，否则服务端的 AskBus 条目会挂满 AskTimeout。
 */
export function applyConfig(store: TaskStore, next: ServeConfig): void {
  // 卸载后重建事件流会留下无人关闭的连接
  if (store.disposed) {
    return
  }

  for (const ask of store.pendingAsks.value) {
    if (!ask.submitted) {
      store.socket?.submitChoice(ask.taskId, ask.requestId, ask.defaultOptionId ?? '')
    }
  }

  store.config.value = next
  saveServeConfig(next)
  startSocket(store)
}

/** 导出日志为文本文件下载。 */
export function exportLog(store: TaskStore): void {
  if (store.logLines.value.length === 0) {
    appendLog(store, '日志为空，无需导出')
    return
  }

  const blob = new Blob([store.logLines.value.map((line) => line.text).join('\n')], {
    type: 'text/plain'
  })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = `BBDown.WebUI.log.${new Date().toISOString().replace(/[:.]/g, '')}.txt`
  anchor.click()
  URL.revokeObjectURL(url)
}
