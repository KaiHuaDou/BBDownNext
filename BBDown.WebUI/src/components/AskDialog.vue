<script setup lang="ts">
import { onUnmounted, ref, watch } from 'vue'

import type { PendingAsk } from '../state/useTasks'

const props = defineProps<{
  ask: PendingAsk
}>()

const emit = defineEmits<{
  answer: [choice: string]
  dismiss: []
}>()

const remaining = ref(0)
let timer: ReturnType<typeof setTimeout> | null = null

// 服务端 AskBus 超时后提问已回落（选项不可再应答），本地按默认项作答避免弹窗滞留；
// 随 ask 切换重置计时，避免按上一任务的剩余时间误答当前提问
function schedule(): void {
  if (timer) {
    clearTimeout(timer)
  }

  const millis = Date.parse(props.ask.deadline) - Date.now()
  remaining.value = Math.max(0, Math.ceil(millis / 1000))
  if (millis <= 0) {
    emit('answer', props.ask.defaultOptionId ?? props.ask.options[0]?.id ?? '')
    return
  }

  timer = setTimeout(schedule, 1000)
}

watch(() => props.ask, schedule, { immediate: true })
onUnmounted(() => {
  if (timer) {
    clearTimeout(timer)
  }
})
</script>

<template>
  <div
    class="fixed inset-0 z-40 flex items-center justify-center bg-black/60"
    @click.self="emit('dismiss')">
    <div
      class="flex w-[26rem] max-w-[90vw] flex-col gap-3 rounded border border-[#3c3c3c] bg-[#252526] p-5">
      <div class="break-all text-sm text-[#eee]">{{ ask.prompt }}</div>
      <div class="flex max-h-72 flex-col gap-1 overflow-y-auto">
        <button
          v-for="option in ask.options"
          :key="option.id"
          class="rounded border border-transparent px-3 py-1.5 text-left text-sm text-[#ddd] hover:border-[#2f6feb] hover:bg-[#2f6feb]/10"
          type="button"
          @click="emit('answer', option.id)">
          {{ option.label }}
        </button>
      </div>
      <div class="flex justify-center">
        <button class="btn-ghost" type="button" @click="emit('dismiss')">取消</button>
      </div>
      <p v-if="ask.defaultOptionId" class="text-center text-xs text-[#6e6e6e]">
        未选择将回落默认选项
      </p>
      <p class="text-center text-xs text-[#6e6e6e]">
        {{ remaining }}s 后自动{{ ask.defaultOptionId ? '选择默认选项' : '应答首项' }}
      </p>
    </div>
  </div>
</template>
