<script setup lang="ts">
import { LIVE_QUALITY_LEVELS } from '../lib/live'
import { API_CHOICES, MUX_CHOICES, type TaskOptions } from '../lib/options'

const options = defineModel<TaskOptions>({ required: true })

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
          <div class="row">
            <label class="row-label" for="pages">分 P 选择</label>
            <input
              id="pages"
              v-model="options.pages"
              class="field"
              title="all / 8 / 1,2,5 / 3-5 / 16- / -22 / latest / last" />
          </div>
          <div class="row">
            <label class="row-label" for="comment-count">评论条数</label>
            <input
              id="comment-count"
              v-model="options.commentsCount"
              class="field"
              title="下载评论区前 N 条评论，0 表示不下载" />
          </div>
          <div class="row">
            <span class="row-label">评论排序</span>
            <div class="flex items-center gap-3">
              <label class="check"
                ><input v-model="options.commentsSort" type="radio" value="hot" />热度</label
              >
              <label class="check"
                ><input v-model="options.commentsSort" type="radio" value="time" />时间</label
              >
            </div>
          </div>
          <div class="row">
            <span class="row-label">评论格式</span>
            <div class="flex items-center gap-4">
              <label class="check">
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
              <label class="check">
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
          <div class="row">
            <span class="row-label">弹幕格式</span>
            <div class="flex items-center gap-4">
              <label class="check">
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
              <label class="check">
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
        <div class="flex flex-col gap-2.5">
          <label class="check">
            <input v-model="options.interactivePages" type="checkbox" />
            逐集确认（每集询问是否下载）
          </label>
          <label class="check"
            ><input v-model="options.showAll" type="checkbox" />展示全部分 P 标题</label
          >
          <label class="check"
            ><input v-model="options.allowPreview" type="checkbox" />允许下载试看片段</label
          >
        </div>
      </div>
    </details>

    <!-- 下载选项 -->
    <details class="expander">
      <summary class="exp-head">下载选项</summary>
      <div class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 p-3 pt-2">
        <div class="flex flex-col gap-2.5">
          <label class="check"
            ><input v-model="options.useAria2c" type="checkbox" />使用 aria2c</label
          >
          <label class="check"
            ><input v-model="options.singleThread" type="checkbox" />单线程下载</label
          >
          <label class="check"
            ><input v-model="options.saveRecords" type="checkbox" />下载记录去重</label
          >
          <label class="check"
            ><input v-model="options.stopOnError" type="checkbox" />失败即停止</label
          >
          <label class="check"
            ><input v-model="options.hideStreams" type="checkbox" />隐藏可用音视频流</label
          >
          <label class="check"
            ><input v-model="options.encodingFirst" type="checkbox" />编码优先于清晰度</label
          >
          <label class="check"
            ><input v-model="options.allowPcdn" type="checkbox" />允许 PCDN</label
          >
          <label class="check"
            ><input v-model="options.noForceHost" type="checkbox" />不强制替换 host</label
          >
          <label class="check"
            ><input v-model="options.noForceHttp" type="checkbox" />避免降级 HTTP</label
          >
        </div>
        <div class="flex flex-col gap-2.5">
          <div class="row">
            <label class="row-label" for="delay-per-page">分 P 间隔（秒）</label>
            <input
              id="delay-per-page"
              v-model="options.delayPerPage"
              class="field"
              title="合集分 P 之间的下载间隔（单位：秒）" />
          </div>
          <div class="row">
            <label class="row-label" for="max-retry">重试次数</label>
            <input
              id="max-retry"
              v-model.number="options.maxRetry"
              class="field"
              type="number"
              min="0"
              title="每个下载项失败后的额外重试次数，0 表示不重试，缺省 3" />
          </div>
          <div class="row">
            <label class="row-label" for="lang">混流音频语言</label>
            <input
              id="lang"
              v-model="options.lang"
              class="field"
              title="混流的音频语言代码，如 chi、jpn" />
          </div>
          <div class="row">
            <label class="row-label" for="mux">混流方式</label>
            <select id="mux" v-model="options.mux" class="field">
              <option v-for="choice in MUX_CHOICES" :key="choice.value" :value="choice.value">
                {{ choice.label }}
              </option>
            </select>
          </div>
        </div>
      </div>
    </details>

    <!-- 解析选项 -->
    <details class="expander">
      <summary class="exp-head">解析选项</summary>
      <div class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 p-3 pt-2">
        <div class="flex flex-col gap-2.5">
          <label class="check"
            ><input v-model="options.infoOnly" type="checkbox" />仅解析不下载</label
          >
          <label class="check"
            ><input v-model="options.videoAscending" type="checkbox" />视频升序（体积小优先）</label
          >
          <label class="check"
            ><input v-model="options.audioAscending" type="checkbox" />音频升序（体积小优先）</label
          >
          <label class="check">
            <input v-model="options.interactiveQuality" type="checkbox" />
            交互选清晰度 / 轨道
          </label>
        </div>
        <div class="flex flex-col gap-2.5">
          <div class="row">
            <label class="row-label" for="api">API 通道</label>
            <select
              id="api"
              v-model="options.api"
              class="field"
              title="web / tv / app / intl，默认 web">
              <option v-for="api in API_CHOICES" :key="api" :value="api">{{ api }}</option>
            </select>
          </div>
          <div class="row">
            <label class="row-label" for="live-quality">直播清晰度</label>
            <select id="live-quality" v-model="options.liveQuality" class="field">
              <option v-for="level in LIVE_QUALITY_LEVELS" :key="level.qn" :value="level.qn">
                {{ level.qn }} {{ level.name }}
              </option>
            </select>
          </div>
          <div class="row">
            <label class="row-label" for="encoding-priority">编码优先级</label>
            <input
              id="encoding-priority"
              v-model="options.encodingPriority"
              class="field"
              title="逗号分隔，如 hevc,av1,avc,flac,eac3,m4a" />
          </div>
          <div class="row">
            <label class="row-label" for="dfn-priority">画质优先级</label>
            <input
              id="dfn-priority"
              v-model="options.dfnPriority"
              class="field"
              title="逗号分隔，如 8K 超高清,1080P 高码率,HDR 真彩,杜比视界" />
          </div>
          <div class="row">
            <label class="row-label" for="audio-quality">音频档位</label>
            <input
              id="audio-quality"
              v-model="options.audioQuality"
              class="field"
              title="逗号分隔，如 杜比全景声,Hi-Res 无损,192K" />
          </div>
          <div class="row">
            <label class="row-label" for="area">Area</label>
            <input
              id="area"
              v-model="options.area"
              class="field"
              title="BiliPlus 区域：hk / tw / th" />
          </div>
          <div class="row">
            <label class="row-label" for="upos-host">Upos Host</label>
            <input
              id="upos-host"
              v-model="options.uposHost"
              class="field"
              title="自定义 upos 服务器" />
          </div>
        </div>
      </div>
    </details>
  </div>
</template>
