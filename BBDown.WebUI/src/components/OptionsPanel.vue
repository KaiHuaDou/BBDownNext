<script setup lang="ts">
import { computed } from 'vue'

import { optionAvailability, type ContentMode } from '../lib/availability'
import { LIVE_QUALITY_LEVELS } from '../lib/live'
import {
  API_CHOICES,
  AUDIO_QUALITY_CHOICES,
  DFN_CHOICES,
  ENCODING_CHOICES,
  MUX_CHOICES,
  type TaskOptions
} from '../lib/options'
import PriorityPicker from './PriorityPicker.vue'

const props = defineProps<{
  /** 目标域（由下载目标识别推导），驱动字段可用性；未识别为 null 时不限制 */
  mode: ContentMode | null
}>()

const options = defineModel<TaskOptions>({ required: true })

const availability = computed(() =>
  optionAvailability({
    mode: props.mode,
    infoOnly: options.value.infoOnly,
    commentsCount: Number.parseInt(options.value.commentsCount, 10) || 0,
    mux: options.value.mux
  })
)

const fmtHas = (formats: string, value: string): boolean => formats.split(',').includes(value)
const toggleFmt = (formats: string, value: string, on: boolean): string => {
  const set = new Set(formats.split(',').filter((s) => s.length > 0))
  if (on) {
    set.add(value)
  } else {
    set.delete(value)
  }

  return [...set].join(',')
}
</script>

<template>
  <div class="flex flex-col gap-1">
    <!-- 内容选项 -->
    <details open class="expander">
      <summary class="exp-head">内容选项</summary>
      <div class="grid grid-cols-2 gap-x-4 gap-y-2 p-1 pt-1">
        <div class="flex flex-col gap-2.5">
          <div class="row" :class="{ 'opacity-50': !availability.pages }">
            <label class="row-label" for="pages">分 P 选择</label>
            <input
              id="pages"
              v-model="options.pages"
              class="field"
              :disabled="!availability.pages"
              placeholder="下载全部（链接含集数时自动选中）"
              title="all / 8 / 1,2,5 / 3-5 / 16- / -22 / latest / last" />
          </div>
          <div class="row" :class="{ 'opacity-50': !availability.commentsCount }">
            <label class="row-label" for="comment-count">评论条数</label>
            <input
              id="comment-count"
              v-model="options.commentsCount"
              class="field"
              :disabled="!availability.commentsCount"
              title="下载评论区前 N 条评论，0 表示不下载" />
          </div>
          <div class="row" :class="{ 'opacity-50': !availability.commentSort }">
            <span class="row-label">评论排序</span>
            <div class="flex items-center gap-3">
              <label class="check" :class="{ 'pointer-events-none': !availability.commentSort }">
                <input
                  v-model="options.commentsSort"
                  type="radio"
                  value="hot"
                  :disabled="!availability.commentSort" />热度</label
              >
              <label class="check" :class="{ 'pointer-events-none': !availability.commentSort }">
                <input
                  v-model="options.commentsSort"
                  type="radio"
                  value="time"
                  :disabled="!availability.commentSort" />时间</label
              >
            </div>
          </div>
          <div class="row" :class="{ 'opacity-50': !availability.commentFormat }">
            <span class="row-label">评论格式</span>
            <div class="flex items-center gap-4">
              <label class="check" :class="{ 'pointer-events-none': !availability.commentFormat }">
                <input
                  :checked="fmtHas(options.commentsFormats, 'json')"
                  type="checkbox"
                  :disabled="!availability.commentFormat"
                  @change="
                    options.commentsFormats = toggleFmt(
                      options.commentsFormats,
                      'json',
                      ($event.target as HTMLInputElement).checked
                    )
                  " />
                JSON
              </label>
              <label class="check" :class="{ 'pointer-events-none': !availability.commentFormat }">
                <input
                  :checked="fmtHas(options.commentsFormats, 'txt')"
                  type="checkbox"
                  :disabled="!availability.commentFormat"
                  @change="
                    options.commentsFormats = toggleFmt(
                      options.commentsFormats,
                      'txt',
                      ($event.target as HTMLInputElement).checked
                    )
                  " />
                TXT
              </label>
            </div>
          </div>
          <div class="row" :class="{ 'opacity-50': !availability.danmakuFormat }">
            <span class="row-label">弹幕格式</span>
            <div class="flex items-center gap-4">
              <label class="check" :class="{ 'pointer-events-none': !availability.danmakuFormat }">
                <input
                  :checked="fmtHas(options.danmakuFormats, 'xml')"
                  type="checkbox"
                  :disabled="!availability.danmakuFormat"
                  @change="
                    options.danmakuFormats = toggleFmt(
                      options.danmakuFormats,
                      'xml',
                      ($event.target as HTMLInputElement).checked
                    )
                  " />
                XML
              </label>
              <label class="check" :class="{ 'pointer-events-none': !availability.danmakuFormat }">
                <input
                  :checked="fmtHas(options.danmakuFormats, 'ass')"
                  type="checkbox"
                  :disabled="!availability.danmakuFormat"
                  @change="
                    options.danmakuFormats = toggleFmt(
                      options.danmakuFormats,
                      'ass',
                      ($event.target as HTMLInputElement).checked
                    )
                  " />
                ASS
              </label>
            </div>
          </div>
        </div>
        <div class="flex flex-col gap-2.5">
          <label class="check" :class="{ 'pointer-events-none': !availability.interactivePages }">
            <input
              v-model="options.interactivePages"
              type="checkbox"
              :disabled="!availability.interactivePages" />
            逐集确认（每集询问是否下载）
          </label>
          <label class="check" :class="{ 'pointer-events-none': !availability.showAll }">
            <input
              v-model="options.showAll"
              type="checkbox"
              :disabled="!availability.showAll" />展示全部分 P 标题</label
          >
          <label class="check" :class="{ 'pointer-events-none': !availability.allowPreview }">
            <input
              v-model="options.allowPreview"
              type="checkbox"
              :disabled="!availability.allowPreview" />允许下载试看片段</label
          >
        </div>
      </div>
    </details>

    <!-- 下载选项 -->
    <details class="expander">
      <summary class="exp-head">下载选项</summary>
      <div class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 p-3 pt-2">
        <div class="flex flex-col gap-2.5">
          <label class="check" :class="{ 'pointer-events-none': !availability.downloadChecks }">
            <input
              v-model="options.singleThread"
              type="checkbox"
              :disabled="!availability.downloadChecks" />单线程下载</label
          >
          <label class="check" :class="{ 'pointer-events-none': !availability.downloadChecks }">
            <input
              v-model="options.saveRecords"
              type="checkbox"
              :disabled="!availability.downloadChecks" />下载记录去重</label
          >
          <label class="check" :class="{ 'pointer-events-none': !availability.downloadChecks }">
            <input
              v-model="options.stopOnError"
              type="checkbox"
              :disabled="!availability.downloadChecks" />失败即停止</label
          >
          <label class="check" :class="{ 'pointer-events-none': !availability.downloadChecks }">
            <input
              v-model="options.hideStreams"
              type="checkbox"
              :disabled="!availability.downloadChecks" />隐藏可用音视频流</label
          >
          <label class="check" :class="{ 'pointer-events-none': !availability.downloadChecks }">
            <input
              v-model="options.encodingFirst"
              type="checkbox"
              :disabled="!availability.downloadChecks" />编码优先于清晰度</label
          >
          <label class="check" :class="{ 'pointer-events-none': !availability.downloadChecks }">
            <input
              v-model="options.allowPcdn"
              type="checkbox"
              :disabled="!availability.downloadChecks" />允许 PCDN</label
          >
          <label class="check" :class="{ 'pointer-events-none': !availability.downloadChecks }">
            <input
              v-model="options.noForceHost"
              type="checkbox"
              :disabled="!availability.downloadChecks" />不强制替换 host</label
          >
          <label class="check" :class="{ 'pointer-events-none': !availability.downloadChecks }">
            <input
              v-model="options.noForceHttp"
              type="checkbox"
              :disabled="!availability.downloadChecks" />避免降级 HTTP</label
          >
        </div>
        <div class="flex flex-col gap-2.5">
          <div class="row" :class="{ 'opacity-50': !availability.delayPerPage }">
            <label class="row-label" for="delay-per-page">分 P 间隔（秒）</label>
            <input
              id="delay-per-page"
              v-model="options.delayPerPage"
              class="field"
              :disabled="!availability.delayPerPage"
              title="合集分 P 之间的下载间隔（单位：秒）" />
          </div>
          <div class="row" :class="{ 'opacity-50': !availability.lang }">
            <label class="row-label" for="lang">混流音频语言</label>
            <input
              id="lang"
              v-model="options.lang"
              class="field"
              :disabled="!availability.lang"
              placeholder="不写入语言标记"
              title="混流的音频语言代码，如 chi、jpn" />
          </div>
          <div class="row" :class="{ 'opacity-50': !availability.mux }">
            <label class="row-label" for="mux">混流方式</label>
            <select id="mux" v-model="options.mux" class="field" :disabled="!availability.mux">
              <option v-for="choice in MUX_CHOICES" :key="choice.value" :value="choice.value">
                {{ choice.label }}
              </option>
            </select>
          </div>
          <div class="row" :class="{ 'opacity-50': !availability.maxRetry }">
            <label class="row-label" for="max-retry">每项下载重试次数</label>
            <input
              id="max-retry"
              v-model.number="options.maxRetry"
              class="field"
              :disabled="!availability.maxRetry"
              type="number"
              min="0"
              title="每个下载项在首次尝试之外的额外重试次数，默认 3" />
          </div>
        </div>
      </div>
    </details>

    <!-- 解析选项 -->
    <details class="expander">
      <summary class="exp-head">解析选项</summary>
      <div class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 p-3 pt-2">
        <div class="flex flex-col gap-2.5">
          <label class="check">
            <input v-model="options.infoOnly" type="checkbox" />仅解析不下载
          </label>
          <label class="check" :class="{ 'pointer-events-none': !availability.ascending }">
            <input
              v-model="options.videoAscending"
              type="checkbox"
              :disabled="!availability.ascending" />视频升序（体积小优先）</label
          >
          <label class="check" :class="{ 'pointer-events-none': !availability.ascending }">
            <input
              v-model="options.audioAscending"
              type="checkbox"
              :disabled="!availability.ascending" />音频升序（体积小优先）</label
          >
          <label class="check" :class="{ 'pointer-events-none': !availability.interactiveQuality }">
            <input
              v-model="options.interactiveQuality"
              type="checkbox"
              :disabled="!availability.interactiveQuality" />
            交互选清晰度 / 轨道
          </label>
        </div>
        <div class="flex flex-col gap-2.5">
          <div class="row" :class="{ 'opacity-50': !availability.api }">
            <span class="row-label">API 通道</span>
            <div class="flex items-center gap-3" title="默认 web">
              <label
                v-for="api in API_CHOICES"
                :key="api"
                class="check"
                :class="{ 'pointer-events-none': !availability.api }"
                ><input
                  v-model="options.api"
                  type="radio"
                  :value="api"
                  :disabled="!availability.api" />{{ api }}</label
              >
            </div>
          </div>
          <div class="row" :class="{ 'opacity-50': !availability.liveQuality }">
            <label class="row-label" for="live-quality">直播清晰度</label>
            <select
              id="live-quality"
              v-model="options.liveQuality"
              class="field"
              :disabled="!availability.liveQuality">
              <option v-for="level in LIVE_QUALITY_LEVELS" :key="level.qn" :value="level.qn">
                {{ level.qn }} {{ level.name }}
              </option>
            </select>
          </div>
          <div class="row" :class="{ 'opacity-50': !availability.priority }">
            <span class="row-label">编码优先级</span>
            <PriorityPicker
              v-model="options.encodingPriority"
              :choices="ENCODING_CHOICES"
              :disabled="!availability.priority" />
          </div>
          <div class="row" :class="{ 'opacity-50': !availability.priority }">
            <span class="row-label">画质优先级</span>
            <PriorityPicker
              v-model="options.dfnPriority"
              :choices="DFN_CHOICES"
              :disabled="!availability.priority" />
          </div>
          <div class="row" :class="{ 'opacity-50': !availability.priority }">
            <span class="row-label">音频档位</span>
            <PriorityPicker
              v-model="options.audioQuality"
              :choices="AUDIO_QUALITY_CHOICES"
              :disabled="!availability.priority" />
          </div>
        </div>
      </div>
    </details>

    <!-- 网络选项：serve 请求约定内的请求侧字段（凭据固定用服务端 BBDown.data，不在此列） -->
    <details class="expander">
      <summary class="exp-head">网络选项</summary>
      <div class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 p-3 pt-2">
        <div class="row">
          <label class="row-label" for="area">Area</label>
          <input
            id="area"
            v-model="options.area"
            class="field"
            placeholder="不启用"
            title="BiliPlus 区域：hk / tw / th" />
        </div>
        <div class="row">
          <label class="row-label" for="upos-host">Upos Host</label>
          <input
            id="upos-host"
            v-model="options.uposHost"
            class="field"
            placeholder="不替换"
            title="自定义 upos 服务器" />
        </div>
      </div>
    </details>
  </div>
</template>
