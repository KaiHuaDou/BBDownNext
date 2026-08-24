import {
  defineConfig,
  presetAttributify,
  presetTagify,
  presetTypography,
  presetWebFonts,
  presetWind4
} from 'unocss'

export default defineConfig({
  presets: [
    presetWind4(),
    presetAttributify(),
    presetTagify(),
    presetTypography(),
    presetWebFonts()
  ],
  shortcuts: {
    'field-input':
      'w-full rounded border border-[#3c3c3c] bg-[#1a1a1c] px-2 py-1 text-sm text-[#eee] outline-none focus:border-[#2f6feb] disabled:opacity-50',
    'btn-action':
      'rounded border border-[#2f6feb] bg-[#2f6feb]/15 px-5 py-2 text-sm text-[#eee] hover:bg-[#2f6feb]/30 disabled:opacity-50',
    'btn-ghost':
      'rounded border border-transparent px-3 py-1 text-sm text-[#ddd] hover:bg-[#ffffff1a]',
    'btn-task':
      'rounded border border-[#3c3c3c] px-2 py-0.5 text-xs text-[#ddd] hover:border-[#2f6feb] hover:text-[#eee]',
    'check-row': 'flex select-none items-center gap-1.5 text-sm text-[#ddd]',
    'option-group': 'rounded border border-[#3c3c3c] bg-[#252526]',
    'option-header':
      'cursor-pointer list-none select-none border-b border-[#3c3c3c] px-3 py-2 text-sm font-semibold text-[#eee] hover:bg-[#ffffff0a] [&::-webkit-details-marker]:hidden',
    'field-row': 'flex items-center gap-2',
    'field-label': 'w-24 shrink-0 text-sm text-[#ddd]'
  }
})
