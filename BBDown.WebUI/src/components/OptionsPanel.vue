<script setup lang="ts">
import { LIVE_QUALITY_LEVELS } from '../lib/live'
import { API_CHOICES, MUX_CHOICES, SERVE_EXCLUDED, type TaskOptions } from '../lib/options'

const options = defineModel<TaskOptions>({ required: true })

const isExcluded = (field: string): boolean => field in SERVE_EXCLUDED
const excludedHint = (field: string): string | undefined => SERVE_EXCLUDED[field]

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
  <div class="flex flex-col gap-1.5">
    <!-- 内容选项 -->
    <details open class="option-group">
      <summary class="option-header">内容选项</summary>
      <div class="grid grid-cols-2 gap-x-4 p-2.5">
        <div class="flex flex-col gap-1">
          <div class="field-row">
            <label class="field-label" for="pages">分 P 选择</label>
            <input
              id="pages"
              v-model="options.pages"
              class="field-input"
              title="all / 8 / 1,2,5 / 3-5 / 16- / -22 / latest / last" />
          </div>
          <div class="field-row">
            <label class="field-label" for="comment-count">评论条数</label>
            <input
              id="comment-count"
              v-model="options.commentsCount"
              class="field-input"
              title="下载评论区前 N 条评论，0 表示不下载" />
          </div>
          <div class="field-row">
            <span class="field-label">评论排序</span>
            <div class="flex items-center gap-2">
              <label class="flex items-center gap-1 text-sm text-[#ddd]">
                <input v-model="options.commentsSort" type="radio" value="hot" />
                热度
              </label>
              <label class="flex items-center gap-1 text-sm text-[#ddd]">
                <input v-model="options.commentsSort" type="radio" value="time" />
                时间
              </label>
            </div>
          </div>
          <div class="field-row">
            <span class="field-label">评论格式</span>
            <div class="flex items-center gap-3">
              <label class="flex items-center gap-1 text-sm text-[#ddd]">
                <input
                  :checked="fmtHas(options.commentsFormats, 'json')"
                  type="checkbox"
                  @change="
                    options.commentsFormats = toggleFmt(
                      options.commentsFormats,
                      'json',
                      ($event.target as HTMLInputElement).checked
                    )
                  " />
                JSON
              </label>
              <label class="flex items-center gap-1 text-sm text-[#ddd]">
                <input
                  :checked="fmtHas(options.commentsFormats, 'txt')"
                  type="checkbox"
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
          <div class="field-row">
            <span class="field-label">弹幕格式</span>
            <div class="flex items-center gap-3">
              <label class="flex items-center gap-1 text-sm text-[#ddd]">
                <input
                  :checked="fmtHas(options.danmakuFormats, 'xml')"
                  type="checkbox"
                  @change="
                    options.danmakuFormats = toggleFmt(
                      options.danmakuFormats,
                      'xml',
                      ($event.target as HTMLInputElement).checked
                    )
                  " />
                XML
              </label>
              <label class="flex items-center gap-1 text-sm text-[#ddd]">
                <input
                  :checked="fmtHas(options.danmakuFormats, 'ass')"
                  type="checkbox"
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
        <div class="flex flex-col gap-1 pt-0.5">
          <label
            class="check-row"
            :class="{ 'opacity-50': isExcluded('interactivePages') }"
            :title="excludedHint('interactivePages')">
            <input
              v-model="options.interactivePages"
              type="checkbox"
              :disabled="isExcluded('interactivePages')" />
            逐集确认（每集询问是否下载）
          </label>
          <label class="check-row">
            <input v-model="options.showAll" type="checkbox" />
            展示全部分 P 标题
          </label>
          <label class="check-row">
            <input v-model="options.allowPreview" type="checkbox" />
            允许下载试看片段
          </label>
        </div>
      </div>
    </details>

    <!-- 下载选项 -->
    <details class="option-group">
      <summary class="option-header">下载选项</summary>
      <div class="grid grid-cols-[auto_1fr] gap-x-4 p-2.5">
        <div class="flex flex-col gap-1">
          <label class="check-row">
            <input v-model="options.useAria2c" type="checkbox" />
            使用 aria2c
          </label>
          <label class="check-row">
            <input v-model="options.singleThread" type="checkbox" />
            单线程下载
          </label>
          <label class="check-row">
            <input v-model="options.saveRecords" type="checkbox" />
            下载记录去重
          </label>
          <label class="check-row">
            <input v-model="options.stopOnError" type="checkbox" />
            失败即停止
          </label>
          <label
            class="check-row"
            :class="{ 'opacity-50': isExcluded('debug') }"
            :title="excludedHint('debug')">
            <input v-model="options.debug" type="checkbox" :disabled="isExcluded('debug')" />
            调试日志
          </label>
          <label class="check-row">
            <input v-model="options.allowPcdn" type="checkbox" />
            允许 PCDN
          </label>
          <label class="check-row">
            <input v-model="options.noForceHost" type="checkbox" />
            不强制替换 host
          </label>
          <label class="check-row">
            <input v-model="options.noForceHttp" type="checkbox" />
            避免降级 HTTP
          </label>
        </div>
        <div class="flex flex-col gap-1">
          <div class="field-row">
            <label class="field-label" for="delay-per-page">分 P 间隔（秒）</label>
            <input
              id="delay-per-page"
              v-model="options.delayPerPage"
              class="field-input"
              title="合集分 P 之间的下载间隔（单位：秒）" />
          </div>
          <div class="field-row">
            <label class="field-label" for="lang">混流音频语言</label>
            <input
              id="lang"
              v-model="options.lang"
              class="field-input"
              title="混流的音频语言代码，如 chi、jpn" />
          </div>
          <div
            class="field-row"
            :class="{ 'opacity-50': isExcluded('aria2cArgs') }"
            :title="excludedHint('aria2cArgs')">
            <label class="field-label" for="aria2c-args">aria2c 附加参数</label>
            <input
              id="aria2c-args"
              v-model="options.aria2cArgs"
              class="field-input"
              :disabled="isExcluded('aria2cArgs')" />
          </div>
          <div class="field-row">
            <label class="field-label" for="mux">混流方式</label>
            <select id="mux" v-model="options.mux" class="field-input">
              <option v-for="choice in MUX_CHOICES" :key="choice.value" :value="choice.value">
                {{ choice.label }}
              </option>
            </select>
          </div>
          <div
            class="field-row"
            :class="{ 'opacity-50': isExcluded('workDir') }"
            :title="excludedHint('workDir')">
            <label class="field-label" for="work-dir">工作目录</label>
            <input
              id="work-dir"
              v-model="options.workDir"
              class="field-input"
              :disabled="isExcluded('workDir')" />
          </div>
          <div
            class="field-row"
            :class="{ 'opacity-50': isExcluded('ffmpegPath') }"
            :title="excludedHint('ffmpegPath')">
            <label class="field-label" for="ffmpeg-path">FFmpeg 路径</label>
            <input
              id="ffmpeg-path"
              v-model="options.ffmpegPath"
              class="field-input"
              :disabled="isExcluded('ffmpegPath')" />
          </div>
          <div
            class="field-row"
            :class="{ 'opacity-50': isExcluded('mp4boxPath') }"
            :title="excludedHint('mp4boxPath')">
            <label class="field-label" for="mp4box-path">MP4Box 路径</label>
            <input
              id="mp4box-path"
              v-model="options.mp4boxPath"
              class="field-input"
              :disabled="isExcluded('mp4boxPath')" />
          </div>
          <div
            class="field-row"
            :class="{ 'opacity-50': isExcluded('aria2cPath') }"
            :title="excludedHint('aria2cPath')">
            <label class="field-label" for="aria2c-path">aria2c 路径</label>
            <input
              id="aria2c-path"
              v-model="options.aria2cPath"
              class="field-input"
              :disabled="isExcluded('aria2cPath')" />
          </div>
          <div
            class="field-row"
            :class="{ 'opacity-50': isExcluded('postProcessPath') }"
            :title="excludedHint('postProcessPath')">
            <label class="field-label" for="post-process">后处理程序</label>
            <input
              id="post-process"
              v-model="options.postProcessPath"
              class="field-input"
              :disabled="isExcluded('postProcessPath')" />
          </div>
          <div
            class="field-row"
            :class="{ 'opacity-50': isExcluded('filePattern') }"
            :title="excludedHint('filePattern')">
            <label class="field-label" for="file-pattern">文件命名模式</label>
            <input
              id="file-pattern"
              v-model="options.filePattern"
              class="field-input"
              :disabled="isExcluded('filePattern')"
              title="单 P 文件名模板，内置变量见 BBDown --help" />
          </div>
          <div
            class="field-row"
            :class="{ 'opacity-50': isExcluded('multiFilePattern') }"
            :title="excludedHint('multiFilePattern')">
            <label class="field-label" for="multi-file-pattern">多 P 文件命名</label>
            <input
              id="multi-file-pattern"
              v-model="options.multiFilePattern"
              class="field-input"
              :disabled="isExcluded('multiFilePattern')"
              title="多 P 文件名模板，内置变量见 BBDown --help" />
          </div>
        </div>
      </div>
    </details>

    <!-- 解析选项 -->
    <details class="option-group">
      <summary class="option-header">解析选项</summary>
      <div class="grid grid-cols-[auto_1fr] gap-x-4 p-2.5">
        <div class="flex flex-col gap-1">
          <label class="check-row">
            <input v-model="options.infoOnly" type="checkbox" />
            仅解析不下载
          </label>
          <label class="check-row">
            <input v-model="options.videoAscending" type="checkbox" />
            视频升序（体积小优先）
          </label>
          <label class="check-row">
            <input v-model="options.audioAscending" type="checkbox" />
            音频升序（体积小优先）
          </label>
          <label
            class="check-row"
            :class="{ 'opacity-50': isExcluded('interactiveQuality') }"
            :title="excludedHint('interactiveQuality')">
            <input
              v-model="options.interactiveQuality"
              type="checkbox"
              :disabled="isExcluded('interactiveQuality')" />
            交互选清晰度 / 轨道
          </label>
        </div>
        <div class="flex flex-col gap-1">
          <div class="field-row">
            <label class="field-label" for="api">API 通道</label>
            <select
              id="api"
              v-model="options.api"
              class="field-input"
              title="web / tv / app / intl，默认 web">
              <option v-for="api in API_CHOICES" :key="api" :value="api">{{ api }}</option>
            </select>
          </div>
          <div
            class="field-row"
            :class="{ 'opacity-50': isExcluded('liveQuality') }"
            :title="excludedHint('liveQuality')">
            <label class="field-label" for="live-quality">直播清晰度</label>
            <select
              id="live-quality"
              v-model="options.liveQuality"
              class="field-input"
              :disabled="isExcluded('liveQuality')">
              <option
                v-for="level in LIVE_QUALITY_LEVELS"
                :key="level.qn"
                :value="String(level.qn)">
                {{ level.qn }} {{ level.name }}
              </option>
            </select>
          </div>
          <div class="field-row">
            <label class="field-label" for="encoding-priority">编码优先级</label>
            <input
              id="encoding-priority"
              v-model="options.encodingPriority"
              class="field-input"
              title="逗号分隔，如 hevc,av1,avc,flac,eac3,m4a" />
          </div>
          <div class="field-row">
            <label class="field-label" for="dfn-priority">画质优先级</label>
            <input
              id="dfn-priority"
              v-model="options.dfnPriority"
              class="field-input"
              title="逗号分隔，如 8K 超高清,1080P 高码率,HDR 真彩,杜比视界" />
          </div>
          <div class="field-row">
            <label class="field-label" for="audio-quality">音频档位</label>
            <input
              id="audio-quality"
              v-model="options.audioQuality"
              class="field-input"
              title="逗号分隔，如 杜比全景声,Hi-Res 无损,192K" />
          </div>
          <div
            class="field-row"
            :class="{ 'opacity-50': isExcluded('userAgent') }"
            :title="excludedHint('userAgent')">
            <label class="field-label" for="user-agent">User-Agent</label>
            <input
              id="user-agent"
              v-model="options.userAgent"
              class="field-input"
              :disabled="isExcluded('userAgent')" />
          </div>
          <div
            class="field-row"
            :class="{ 'opacity-50': isExcluded('host') }"
            :title="excludedHint('host')">
            <label class="field-label" for="host">Host</label>
            <input
              id="host"
              v-model="options.host"
              class="field-input"
              :disabled="isExcluded('host')" />
          </div>
          <div
            class="field-row"
            :class="{ 'opacity-50': isExcluded('epHost') }"
            :title="excludedHint('epHost')">
            <label class="field-label" for="ep-host">EP Host</label>
            <input
              id="ep-host"
              v-model="options.epHost"
              class="field-input"
              :disabled="isExcluded('epHost')" />
          </div>
          <div
            class="field-row"
            :class="{ 'opacity-50': isExcluded('tvHost') }"
            :title="excludedHint('tvHost')">
            <label class="field-label" for="tv-host">TV Host</label>
            <input
              id="tv-host"
              v-model="options.tvHost"
              class="field-input"
              :disabled="isExcluded('tvHost')" />
          </div>
          <div class="field-row">
            <label class="field-label" for="area">Area</label>
            <input
              id="area"
              v-model="options.area"
              class="field-input"
              title="BiliPlus 区域：hk / tw / th" />
          </div>
          <div class="field-row">
            <label class="field-label" for="upos-host">Upos Host</label>
            <input
              id="upos-host"
              v-model="options.uposHost"
              class="field-input"
              title="自定义 upos 服务器" />
          </div>
        </div>
      </div>
    </details>
  </div>
</template>
