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
import { loadCredential } from '../api/login'
import { errorMessage } from '../lib/errors'
import { toServeRequest, type TaskOptions } from '../lib/options'
import type { TaskView } from '../lib/types'
import { poll, probeHealth, startSocket } from './connection'
import { appendLog } from './snapshot'
import type { TaskStore } from './store'
import type { PendingAsk } from './types'

/** 提交任务；成功则记录选项快照供重试。mode 为 enqueue 时进入暂停态（待 start）。返回 null 表示失败。 */
export async function submit(
  store: TaskStore,
  options: TaskOptions,
  url: string,
  mode: 'execute' | 'enqueue' = 'execute'
): Promise<{ taskId: string; duplicate: boolean } | null> {
  const request = toServeRequest(options, url, loadCredential())
  try {
    const { task, duplicate } = await submitTask(store.config.value, request, mode)
    store.submittedOptions.set(task.id, { ...options })
    appendLog(store, duplicate ? `任务已存在：${url}` : `任务已受理：${url}`)
    void poll(store)
    return { taskId: task.id, duplicate }
  } catch (e) {
    appendLog(store, `任务提交失败：${errorMessage(e)}`, true)
    return null
  }
}

/** 取消运行中 / 排队中任务。 */
export async function stop(store: TaskStore, view: TaskView): Promise<void> {
  try {
    await stopTask(store.config.value, view.id)
    appendLog(store, `任务${view.id} 已请求取消`)
  } catch (e) {
    appendLog(store, `取消失败：${errorMessage(e)}`, true)
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

/** 移除已完成任务；运行中的任务需先取消。 */
export async function remove(store: TaskStore, view: TaskView): Promise<void> {
  if (view.status === 'Running') {
    appendLog(store, '运行中的任务请先取消')
    return
  }

  try {
    await removeTask(store.config.value, view.id)
    store.tasks.value = store.tasks.value.filter((t) => t.id !== view.id)
  } catch (e) {
    appendLog(store, `移除失败：${errorMessage(e)}`, true)
  }
}

/** 继续：用提交时的选项快照重新提交；无快照时回落当前面板选项。 */
export async function retry(
  store: TaskStore,
  view: TaskView,
  fallback: TaskOptions
): Promise<void> {
  await submit(store, store.submittedOptions.get(view.id) ?? fallback, view.url)
}

/** 清空全部已完成任务（保留运行中 / 等待中）。 */
export async function clearAll(store: TaskStore): Promise<void> {
  try {
    await clearFinished(store.config.value)
    store.tasks.value = store.tasks.value.filter(
      (t) => t.status === 'Running' || t.status === 'Waiting'
    )
  } catch (e) {
    appendLog(store, `清空失败：${errorMessage(e)}`, true)
  }
}

/** 清空已失败的已完成任务。 */
export async function clearFailed(store: TaskStore): Promise<void> {
  try {
    await clearFailedRemote(store.config.value)
    store.tasks.value = store.tasks.value.filter((t) => t.status !== 'Failed')
  } catch (e) {
    appendLog(store, `清空失败：${errorMessage(e)}`, true)
  }
}

/** 应答选项请求；应答后从挂起列表移除。 */
export async function answerAsk(store: TaskStore, ask: PendingAsk, choice: string): Promise<void> {
  store.socket?.submitChoice(ask.taskId, ask.requestId, choice)
  store.answeredAsks.add(ask.requestId)
  store.pendingAsks.value = store.pendingAsks.value.filter((a) => a.requestId !== ask.requestId)
}

/** 更新连接配置：持久化并重建事件流与轮询。 */
export function applyConfig(store: TaskStore, next: ServeConfig): void {
  store.config.value = next
  saveServeConfig(next)
  startSocket(store)
  void probeHealth(store)
  void poll(store)
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
