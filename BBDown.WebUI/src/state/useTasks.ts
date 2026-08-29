import { onUnmounted } from 'vue'

import {
  answerAsk,
  applyConfig,
  clearAll,
  clearFailed,
  exportLog,
  remove,
  retry,
  start,
  stop,
  submit
} from './actions'
import { startTimers, stopTimers } from './connection'
import { appendLog } from './snapshot'
import { createStore } from './store'
import type { TasksState } from './types'
export type { EventStreamState, PendingAsk } from './types'

/**
 * 任务列表 + 日志 + 连接状态：轮询快照与 WebSocket 事件流在此合并。
 * 状态存于 TaskStore，处理函数均在模块级（见 connection / snapshot / actions），本函数仅做装配。
 */
export function useTasks(): TasksState {
  const store = createStore()
  startTimers(store)
  onUnmounted(() => stopTimers(store))

  return {
    config: store.config,
    connected: store.connected,
    connectionError: store.connectionError,
    eventStream: store.eventStream,
    tasks: store.tasks,
    logLines: store.logLines,
    pendingAsks: store.pendingAsks,
    submit: (options, url, mode) => submit(store, options, url, mode),
    stop: (view) => stop(store, view),
    remove: (view) => remove(store, view),
    retry: (view, fallback) => retry(store, view, fallback),
    start: (view) => start(store, view),
    clearAll: () => clearAll(store),
    clearFailed: () => clearFailed(store),
    answerAsk: (ask, choice) => answerAsk(store, ask, choice),
    setConfig: (next) => applyConfig(store, next),
    exportLog: () => exportLog(store),
    appendLog: (text, isError) => appendLog(store, text, isError)
  }
}
