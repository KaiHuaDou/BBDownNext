<script setup lang="ts">
import type { TaskView } from '../lib/types'

defineProps<{
  tasks: TaskView[]
}>()

const emit = defineEmits<{
  stop: [view: TaskView]
  cancel: [view: TaskView]
  retry: [view: TaskView]
  start: [view: TaskView]
  remove: [view: TaskView]
}>()

/** 状态色（复刻 GUI StatusToBrushConverter），以主题变量驱动以保持一致。 */
function statusColor(status: TaskView['status']): string {
  switch (status) {
    case 'Pending': {
      return 'var(--st-pending)'
    }
    case 'Waiting': {
      return 'var(--st-waiting)'
    }
    case 'Running': {
      return 'var(--st-running)'
    }
    case 'Success': {
      return 'var(--st-success)'
    }
    case 'Failed': {
      return 'var(--st-failed)'
    }
    case 'Cancelled': {
      return 'var(--st-cancelled)'
    }
  }
}

/** 仅收尾态可移除：pending 暂停态与 finished 三态由服务端 RemoveTask 处理，运行与排队须先取消。 */
function removable(status: TaskView['status']): boolean {
  return ['Pending', 'Success', 'Failed', 'Cancelled'].includes(status)
}
</script>

<template>
  <div class="flex flex-col gap-1.5">
    <div
      v-for="task in tasks"
      :key="task.id"
      class="group relative overflow-hidden rounded-[var(--radius-sm)] bg-[var(--glass-2)] p-2 transition-colors hover:bg-[var(--glass-3)]">
      <!-- 运行中的背景进度条 -->
      <div
        v-if="task.status === 'Running'"
        class="pointer-events-none absolute inset-x-0 top-0 h-1 bg-[var(--accent)] transition-[width] duration-200"
        :class="{ 'animate-pulse': task.isLive }"
        :style="{ width: `${Math.round(task.progress * 100)}%` }" />
      <div class="flex items-start gap-3">
        <div class="min-w-0 flex-1">
          <div class="flex flex-wrap items-center gap-1.5">
            <span
              class="inline-flex items-center gap-1.5 text-sm font-semibold"
              :style="{ color: statusColor(task.status) }">
              <i
                class="h-1.5 w-1.5 rounded-full"
                :style="{ background: statusColor(task.status) }" />
              {{ task.statusText }}
            </span>
            <span v-if="task.isLive" class="badge text-[var(--pink)]">直播</span>
            <span class="badge">{{ task.kind }}</span>
            <span class="truncate text-sm text-[var(--text)]">{{ task.title ?? task.url }}</span>
          </div>
          <div v-if="task.detail" class="mt-1 text-xs text-[var(--text-dim)]">
            {{ task.detail }}
          </div>
          <div v-if="task.errorMessage" class="mt-1 text-xs text-[var(--st-failed)]">
            {{ task.errorMessage }}
          </div>
        </div>
        <div class="flex shrink-0 flex-col items-end gap-1">
          <button
            v-if="task.isLive && task.status === 'Running'"
            class="btn-task"
            type="button"
            @click="emit('stop', task)">
            停止
          </button>
          <button
            v-if="task.status === 'Running' || task.status === 'Waiting'"
            class="btn-task"
            type="button"
            @click="emit('cancel', task)">
            取消
          </button>
          <button
            v-if="task.status === 'Failed' || task.status === 'Cancelled'"
            class="btn-task"
            type="button"
            @click="emit('retry', task)">
            继续
          </button>
          <button
            v-if="task.status === 'Pending'"
            class="btn-task"
            type="button"
            @click="emit('start', task)">
            启动
          </button>
          <button
            v-if="removable(task.status)"
            class="btn-task"
            type="button"
            @click="emit('remove', task)">
            移除
          </button>
        </div>
      </div>
    </div>
    <div v-if="tasks.length === 0" class="py-10 text-center text-sm text-[var(--text-faint)]">
      暂无任务，粘贴链接开始下载
    </div>
  </div>
</template>
