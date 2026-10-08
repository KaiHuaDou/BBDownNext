/** 内容选项表：与 Core ContentSelector.Order 单一来源对齐（顺序、字符、名称完全一致）。 */

export interface ContentItem {
  ch: string
  name: string
}

export const CONTENT_ORDER: ContentItem[] = [
  { ch: 'a', name: '音频' },
  { ch: 'v', name: '视频' },
  { ch: 'c', name: '独立封面' },
  { ch: 'd', name: '弹幕' },
  { ch: 's', name: '字幕' },
  { ch: 'S', name: 'AI 字幕' },
  { ch: 'o', name: '评论' },
  { ch: 'O', name: '全部评论' },
  { ch: 'C', name: '封面嵌入' },
  { ch: 'm', name: '嵌入元数据' },
  { ch: 'A', name: '专栏 Markdown' },
  { ch: 'i', name: '专栏图片' },
  { ch: 'M', name: '专栏 YAML Frontmatter' }
]

/** 默认内容集，与 Core ContentSelector.Default（avmsCiAM）一致。 */
export const DEFAULT_CONTENT = 'avmsCiAM'

/** 由勾选集合构造内容字符串（按规范顺序）。 */
export function contentFromChecked(checked: Set<string>): string {
  return CONTENT_ORDER.filter((item) => checked.has(item.ch))
    .map((item) => item.ch)
    .join('')
}

/** 由内容字符串求勾选集合。 */
export function checkedFromContent(content: string): Set<string> {
  return new Set(content)
}
