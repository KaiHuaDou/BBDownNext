import { buildDetail } from '../lib/format'
import type { TaskSnapshot, WorkflowEvent } from '../lib/types'
import type { TaskStore } from './store'
import { toView } from './taskView'

const MAX_LOG_LINES = 5000

/** 追加日志行并在超限时从头截断。 */
export function appendLog(store: TaskStore, text: string, isError = false): void {
  store.logLines.value.push({ text, isError })
  if (store.logLines.value.length > MAX_LOG_LINES) {
    store.logLines.value.splice(0, store.logLines.value.length - MAX_LOG_LINES)
  }
}

/** 处理一条工作流事件：消息入日志，进度样本改视图，选项请求入挂起队列（去重）。 */
export function handleEvent(store: TaskStore, taskId: string, event: WorkflowEvent): void {
  switch (event.type) {
    case 'message': {
      appendLog(store, `[任务${taskId}] ${event.text}`)
      break
    }
    case 'progressStart':
    case 'progressEnd': {
      // 阶段边界：快照样本到达前无需动作，进度条由快照驱动
      break
    }
    case 'progressSample': {
      applySample(store, taskId, event)
      break
    }
    case 'optionRequest': {
      if (!store.answeredAsks.has(event.requestId)) {
        store.pendingAsks.value.push({
          requestId: event.requestId,
          taskId: event.scope,
          prompt: event.prompt,
          options: event.options,
          defaultOptionId: event.defaultOptionId,
          deadline: event.deadline
        })
      }

      break
    }
  }
}

/** 用进度样本改运行中视图的进度与详情（ratio 夹紧 0-1）。 */
export function applySample(
  store: TaskStore,
  taskId: string,
  sample: { ratio: number; totalBytes: number; speed: number; detail?: string }
): void {
  const view = store.tasks.value.find((t) => t.id === taskId)
  if (!view || view.status !== 'Running') {
    return
  }

  view.progress = Math.min(Math.max(sample.ratio, 0), 1)
  view.detail = buildDetail(sample.ratio, sample.speed, sample.totalBytes, sample.detail)
}

/** 仅对运行中任务维持 WS 订阅，退订已结束任务。 */
export function syncSubscriptions(store: TaskStore, running: string[]): void {
  if (!store.socket) {
    return
  }

  for (const id of running) {
    if (!store.subscribed.has(id)) {
      store.subscribed.add(id)
      store.socket.subscribe(id)
    }
  }

  for (const id of store.subscribed) {
    if (!running.includes(id)) {
      store.subscribed.delete(id)
      store.socket.unsubscribe(id)
    }
  }
}

/**
 * 用全量快照重建任务列表；保留快照不覆盖的运行中 detail（快照无阶段文本，仅 WS 有）。
 */
export function applySnapshot(store: TaskStore, snapshot: TaskSnapshot): void {
  const running = snapshot.running.map((t) => t.id)
  const views = [
    ...snapshot.running.map((t) => toView(t)),
    ...snapshot.finished.map((t) => toView(t))
  ]

  for (const prev of store.tasks.value) {
    if (prev.status === 'Running' && prev.detail) {
      const next = views.find((v) => v.id === prev.id)
      if (next && next.status === 'Running') {
        next.detail = prev.detail
      }
    }
  }

  store.tasks.value = views
  syncSubscriptions(store, running)
  pruneDeadState(
    store,
    running,
    snapshot.finished.map((t) => t.id)
  )
}

// 挂起提问与重试选项快照只属于存活任务：任务结束后剔除，避免交互 / 重试相关状态常驻堆积
function pruneDeadState(store: TaskStore, runningIds: string[], finishedIds: string[]): void {
  const alive = new Set([...runningIds, ...finishedIds])
  store.pendingAsks.value = store.pendingAsks.value.filter((ask) => alive.has(ask.taskId))
  for (const id of store.submittedOptions.keys()) {
    if (!alive.has(id)) {
      store.submittedOptions.delete(id)
    }
  }
}
