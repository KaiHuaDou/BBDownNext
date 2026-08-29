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

/** 状态文字颜色（复刻 GUI StatusToBrushConverter）。 */
function statusColor(status: TaskView['status']): string {
  switch (status) {
    case 'Pending': {
      return '#7e57c2'
    }
    case 'Waiting': {
      return '#c9a227'
    }
    case 'Running': {
      return '#2f6feb'
    }
    case 'Success': {
      return '#4caf50'
    }
    case 'Failed': {
      return '#e53935'
    }
    case 'Cancelled': {
      return '#9e9e9e'
    }
  }
}
</script>

<template>
  <div class="flex flex-col gap-1">
    <div v-for="task in tasks" :key="task.id" class="relative overflow-hidden rounded bg-[#2d2d30]">
      <!-- 运行中的背景进度条 -->
      <div
        v-if="task.status === 'Running'"
        class="pointer-events-none absolute inset-0 bg-[#332f6feb] transition-[width] duration-200"
        :class="{ 'animate-pulse': task.isLive }"
        :style="{ width: `${Math.round(task.progress * 100)}%` }" />
      <div class="relative flex items-center gap-2 px-3 py-2">
        <div class="min-w-0 flex-1">
          <div class="flex items-baseline gap-1.5">
            <span
              class="whitespace-nowrap text-sm font-bold"
              :style="{ color: statusColor(task.status) }">
              {{ task.statusText }}
            </span>
            <span class="shrink-0 rounded bg-[#3a3a3d] px-1 text-xs text-[#bbb]">{{
              task.kind
            }}</span>
            <span class="truncate text-sm text-[#eee]">{{ task.title ?? task.url }}</span>
          </div>
          <div v-if="task.detail" class="mt-0.5 text-xs text-[#9e9e9e]">{{ task.detail }}</div>
          <div v-if="task.errorMessage" class="mt-0.5 text-xs text-[#e53935]">
            {{ task.errorMessage }}
          </div>
        </div>
        <div class="flex shrink-0 items-center gap-1">
          <button
            v-if="task.isLive && task.status === 'Running'"
            class="btn-task"
            type="button"
            @click="emit('stop', task)">
            停止
          </button>
          <button
            v-if="task.status === 'Running'"
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
            v-if="task.status !== 'Running'"
            class="btn-task"
            type="button"
            @click="emit('remove', task)">
            X
          </button>
        </div>
      </div>
    </div>
    <div v-if="tasks.length === 0" class="py-6 text-center text-sm text-[#6e6e6e]">暂无任务</div>
  </div>
</template>
