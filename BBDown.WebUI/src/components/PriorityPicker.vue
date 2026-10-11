<script setup lang="ts">
import { computed, ref } from 'vue'

import { parsePriority } from '../lib/options'

const props = defineProps<{
  /** 全集（按高 → 低），未选区按此序展示 */
  choices: string[]
  disabled?: boolean
}>()

const priority = defineModel<string>({ required: true })

const selected = computed<string[]>(() => parsePriority(priority.value, props.choices))
const available = computed<string[]>(() => {
  const picked = new Set(selected.value.map((s) => s.toLowerCase()))
  return props.choices.filter((c) => !picked.has(c.toLowerCase()))
})

const setPriority = (names: string[]): void => {
  priority.value = names.join(',')
}

const add = (name: string): void => {
  setPriority([...selected.value, name])
}

const remove = (name: string): void => {
  setPriority(selected.value.filter((n) => n !== name))
}

const clear = (): void => {
  setPriority([])
}

// 拖拽换位：chip 落到目标 chip 前面，落到空白区移到末尾（与 GUI ChipDrop / ListDrop 一致）
const dragIndex = ref(-1)

const move = (from: number, to: number): void => {
  const next = [...selected.value]
  const [name] = next.splice(from, 1)
  if (name !== undefined) {
    next.splice(to, 0, name)
  }

  setPriority(next)
}

const onDragStart = (index: number, e: DragEvent): void => {
  dragIndex.value = index
  e.dataTransfer?.setData('text/plain', String(index))
  if (e.dataTransfer) {
    e.dataTransfer.effectAllowed = 'move'
  }
}

const onDragOver = (e: DragEvent): void => {
  e.preventDefault()
  if (e.dataTransfer) {
    e.dataTransfer.dropEffect = 'move'
  }
}

const onDropOnChip = (index: number, e: DragEvent): void => {
  e.preventDefault()
  e.stopPropagation()
  const from = dragIndex.value
  dragIndex.value = -1
  if (from < 0 || from === index) {
    return
  }

  move(from, from < index ? index - 1 : index)
}

const onDropOnArea = (e: DragEvent): void => {
  e.preventDefault()
  const from = dragIndex.value
  dragIndex.value = -1
  if (from < 0 || from === selected.value.length - 1) {
    return
  }

  move(from, selected.value.length - 1)
}
</script>

<template>
  <div class="flex flex-col gap-1.5" :class="{ 'pointer-events-none opacity-50': disabled }">
    <!--已选 chips 流：从左到右即优先序，chip 可拖拽换位、点 × 移除；空白区落下移到末尾-->
    <div
      class="flex max-h-[120px] flex-wrap gap-1.5 overflow-y-auto"
      @dragover="onDragOver"
      @drop="onDropOnArea">
      <span
        v-for="(name, index) in selected"
        :key="name"
        draggable="true"
        class="chip"
        @dragstart="onDragStart(index, $event)"
        @dragover="onDragOver"
        @drop="onDropOnChip(index, $event)">
        <span class="text-[var(--accent)]">{{ index + 1 }}</span>
        {{ name }}
        <button class="chip-x" type="button" @click="remove(name)">×</button>
      </span>
      <span v-if="selected.length === 0" class="text-xs text-[var(--text-faint)]"
        >留空按服务端返回顺序</span
      >
    </div>
    <!--未选区：按全集高 → 低，点击追加到已选末尾；右侧清空按钮一键恢复默认-->
    <div class="flex items-start gap-2">
      <div class="flex max-h-[120px] flex-wrap gap-1.5 overflow-y-auto">
        <button
          v-for="name in available"
          :key="name"
          class="chip-add"
          type="button"
          @click="add(name)">
          {{ name }}
        </button>
      </div>
      <button
        v-if="selected.length > 0"
        class="btn-ghost px-2.5 py-1 text-xs"
        type="button"
        @click="clear">
        清空
      </button>
    </div>
  </div>
</template>
