import { onUnmounted } from 'vue'

import { startTimers, stopTimers } from './connection'
import { createStore, type TaskStore } from './store'
export type { EventStreamState, PendingAsk } from './types'

/**
 * 建立任务状态并挂上轮询与重连计时。处理函数均在模块级（见 connection / snapshot / actions），
 * 调用方直接传 TaskStore。组件卸载时停表。
 */
export function useTasks(): TaskStore {
  const store = createStore()
  startTimers(store)
  onUnmounted(() => stopTimers(store))

  return store
}
