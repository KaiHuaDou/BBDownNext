import { describe, expect, it } from 'vitest'

import { checkedFromContent, contentFromChecked } from '../lib/content'

describe('content', () => {
  it('按规范顺序由勾选集合构造内容字符串', () => {
    expect(contentFromChecked(new Set(['v', 'a', 'm']))).toBe('avm')
    expect(contentFromChecked(new Set())).toBe('')
    expect(contentFromChecked(new Set(['C', 'S', 'd']))).toBe('dSC')
  })

  it('由内容字符串求勾选集合', () => {
    expect(checkedFromContent('avmsCiAM')).toEqual(new Set(['a', 'v', 'm', 's', 'C', 'i', 'A', 'M']))
  })

  it('勾选集合与内容字符串往返一致', () => {
    const checked = new Set(['a', 'v', 'C', 'o'])
    expect(checkedFromContent(contentFromChecked(checked))).toEqual(checked)
  })
})
