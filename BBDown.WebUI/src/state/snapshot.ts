import { buildDetail } from '../lib/format'
import type { TaskSnapshot, WorkflowEvent } from '../lib/types'
import type { TaskStore } from './store'
import { toView } from './taskView'

/** 保留的日志行数（超出部分从头丢弃）。 */
const MAX_LOG_LINES = 5000
/** 超限时的裁剪批量：逐行裁剪会每行搬移整个数组 */
const TRIM_BATCH = 200

/** 追加日志行并在超限时批量截断。 */
export function appendLog(store: TaskStore, text: string, isError = false): void {
  // 日志是所有写入的必经之处，卸载后在此收口即可拦住各处的在途写入
  if (store.disposed) {
    return
  }

  store.logLines.value.push({ text, isError })
  const lines = store.logLines.value
  if (lines.length > MAX_LOG_LINES + TRIM_BATCH) {
    lines.splice(0, lines.length - MAX_LOG_LINES)
  }
}

/** 处理一条工作流事件：消息入日志，进度样本改视图，选项请求入挂起队列（去重）。 */
export function handleEvent(store: TaskStore, taskId: string, event: WorkflowEvent): void {
  switch (event.type) {
    case 'message': {
      appendLog(store, `[任务${taskId}] ${event.text}`)
      break
    }
    case 'progressStart': {
      // 阶段开始即重置耗时基准，否则新阶段的首帧样本会沿用上一阶段的耗时外推剩余时间
      resetElapsed(store, event.scope)
      break
    }
    case 'progressEnd': {
      // 阶段结束：基准留到下一个 progressStart 再重置
      break
    }
    case 'progressSample': {
      applySample(store, taskId, event)
      break
    }
    case 'optionRequest': {
      if (!store.answeredAsks.has(event.requestId)) {
        store.answeredAsks.set(event.requestId, event.scope)
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

/** 重置某任务的耗时基准。阶段切换与进度回退（分 P 切换）都要调，否则剩余时间会沿用上一段的外推。 */
function resetElapsed(store: TaskStore, taskId: string): void {
  store.elapsedBase.set(taskId, Date.now())
}

/** 已耗时（秒）；无基准时返回 null，此时不显示剩余时间。 */
function elapsedOf(store: TaskStore, taskId: string): number | null {
  const base = store.elapsedBase.get(taskId)
  return base === undefined ? null : (Date.now() - base) / 1000
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

  // 进度回退视为分 P 切换，重置基准
  if (!store.elapsedBase.has(taskId) || sample.ratio < view.progress) {
    resetElapsed(store, taskId)
  }

  view.progress = Math.min(Math.max(sample.ratio, 0), 1)
  view.detail = buildDetail(sample.ratio, sample.speed, elapsedOf(store, taskId), sample.detail)
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
 * 重连后重建订阅：服务端按 socket 保存订阅表，新连接上没有任何订阅，
 * 故本地 subscribed 必须整体丢弃后按当前运行中任务重发，否则事件流永久静默。
 */
export function resubscribe(store: TaskStore): void {
  store.subscribed.clear()
  syncSubscriptions(
    store,
    store.tasks.value.filter((t) => t.status === 'Running').map((t) => t.id)
  )
}

/**
 * 用全量快照重建任务列表；保留快照不覆盖的运行中 detail（快照无阶段文本，仅 WS 有）。
 */
export function applySnapshot(store: TaskStore, snapshot: TaskSnapshot): void {
  const running = snapshot.running.map((t) => t.id)
  const views = [
    ...snapshot.running.map((t) => toView(t, elapsedOf(store, t.id))),
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

/**
 * 挂起提问的存废由服务端 choiceResult 决定（settleAsk），任务收尾时弹窗可能仍停在等待确认；
 * 重试按钮还要读已完成任务的选项快照。两者因此随任务留在列表中即保留。
 * 已应答记录与耗时基准只服务运行中任务：任务离开 running 即摘除，
 * 否则长命标签页下增长边界是未清空的已完成任务数，而非运行中任务数
 */
function pruneDeadState(store: TaskStore, runningIds: string[], finishedIds: string[]): void {
  const running = new Set(runningIds)
  const inList = new Set([...runningIds, ...finishedIds])

  store.pendingAsks.value = store.pendingAsks.value.filter((ask) => inList.has(ask.taskId))
  for (const id of store.submittedOptions.keys()) {
    if (!inList.has(id)) {
      store.submittedOptions.delete(id)
    }
  }

  for (const [requestId, taskId] of store.answeredAsks) {
    if (!running.has(taskId)) {
      store.answeredAsks.delete(requestId)
    }
  }

  for (const taskId of store.elapsedBase.keys()) {
    if (!running.has(taskId)) {
      store.elapsedBase.delete(taskId)
    }
  }
}
