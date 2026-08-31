import {
  defineConfig,
  presetAttributify,
  presetIcons,
  presetMini,
  presetTagify,
  presetTypography,
  presetWebFonts,
  presetWind4
} from 'unocss'

// 单选箭头（用于 select），颜色跟随辅助文字
const CHEVRON =
  "url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='%239aa3b3' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpolyline points='6 9 12 15 18 9'/%3E%3C/svg%3E\")"

export default defineConfig({
  presets: [
    presetAttributify(),
    presetIcons(),
    presetMini(),
    presetTagify(),
    presetTypography(),
    presetWebFonts(),
    presetWind4()
  ],
  preflights: [
    {
      getCSS: () => `
        :root {
          /* 更黑的底，靠玻璃面透出微光 */
          --bg: #050608;
          --bg-glow:
            radial-gradient(1100px 560px at 82% -14%, rgba(0,174,236,0.10), transparent 60%),
            radial-gradient(820px 480px at -6% -4%, rgba(251,114,153,0.06), transparent 55%);
          /* 玻璃面：提升不透明度至 9% 达标 WCAG AA (文字对比度 ≥ 4.5:1) */
          --glass: rgba(255,255,255,0.09);
          --glass-2: rgba(255,255,255,0.12);
          --glass-3: rgba(255,255,255,0.16);
          --hairline: rgba(255,255,255,0.10);
          --hairline-strong: rgba(255,255,255,0.18);
          --text: #e9edf4;
          --text-dim: #a8b0c0;
          --text-faint: #7a8294;
          --accent: #00aeec;
          --accent-strong: #3cc4f5;
          --accent-soft: rgba(0,174,236,0.22);
          --pink: #fb7299;
          --st-pending: #a78bfa;
          --st-waiting: #eab308;
          --st-running: #00aeec;
          --st-success: #22c55e;
          --st-failed: #ef4444;
          --st-cancelled: #6b7280;
          --radius: 12px;
          --radius-sm: 8px;
          --blur: 14px;
          --shadow: 0 1px 2px rgba(0,0,0,0.5), 0 10px 30px rgba(0,0,0,0.45);
          --shadow-sm: 0 1px 2px rgba(0,0,0,0.5);
        }

        * { box-sizing: border-box; }

        html, body, #app { height: 100%; }

        body {
          margin: 0;
          background-color: var(--bg);
          background-image: var(--bg-glow);
          background-attachment: fixed;
          color: var(--text);
          font-size: 14px;
          line-height: 1.5;
          -webkit-font-smoothing: antialiased;
          text-rendering: optimizeLegibility;
        }

        input, select, textarea, button { font-family: inherit; font-size: inherit; }

        ::-webkit-scrollbar { width: 9px; height: 9px; }
        ::-webkit-scrollbar-thumb {
          background: var(--hairline-strong);
          border-radius: 8px;
          border: 2px solid transparent;
          background-clip: padding-box;
        }
        ::-webkit-scrollbar-thumb:hover { background: #4a5364; background-clip: padding-box; }
        ::-webkit-scrollbar-track { background: transparent; }

        /* 自定义复选框 */
        input[type="checkbox"] {
          appearance: none;
          -webkit-appearance: none;
          width: 15px;
          height: 15px;
          flex: 0 0 auto;
          border: 1.5px solid var(--hairline-strong);
          border-radius: 5px;
          display: inline-grid;
          place-content: center;
          cursor: pointer;
          transition: background-color .15s, border-color .15s, box-shadow .15s;
        }
        input[type="checkbox"]::before {
          content: "";
          width: 8px;
          height: 8px;
          transform: scale(0);
          transform-origin: center;
          transition: transform .12s ease;
          background: #fff;
          clip-path: polygon(14% 44%, 0 65%, 43% 100%, 100% 16%, 85% 0%, 42% 69%);
          border-radius: 1px;
        }
        input[type="checkbox"]:hover { border-color: var(--accent); }
        input[type="checkbox"]:checked { background: var(--accent); border-color: var(--accent); }
        input[type="checkbox"]:checked::before { transform: scale(1); }
        input[type="checkbox"]:focus-visible { outline: 2px solid var(--accent-soft); outline-offset: 1px; }

        input[type="radio"] { accent-color: var(--accent); width: 14px; height: 14px; cursor: pointer; }

        /* 下拉箭头 */
        select.field-input {
          appearance: none;
          -webkit-appearance: none;
          background-image: ${CHEVRON};
          background-repeat: no-repeat;
          background-position: right 0.7rem center;
          background-size: 1rem;
          padding-right: 2.25rem;
          cursor: pointer;
        }

        /* 折叠面板标题箭头 */
        .group-head::after, .exp-head::after {
          content: "";
          margin-left: auto;
          width: 16px;
          height: 16px;
          flex: 0 0 auto;
          background-image: ${CHEVRON};
          background-repeat: no-repeat;
          background-position: center;
          background-size: contain;
          transition: transform .2s ease;
          opacity: .75;
        }
        details[open] > .group-head::after, details[open] > .exp-head::after { transform: rotate(90deg); }
        /* 抽屉动画 */
        .drawer-enter-active, .drawer-leave-active { transition: opacity .2s ease, transform .2s ease; }
        .drawer-enter-from { opacity: 0; transform: translateX(100%); }
        .drawer-leave-to { opacity: 0; transform: translateX(100%); }
      `
    }
  ],
  shortcuts: {
    // 玻璃面板：半透明面 + 毛玻璃模糊 + 极细玻璃缘（Aero 玻璃边，非硬边框）+ 柔和阴影
    card: 'bg-[var(--glass)] backdrop-blur-[var(--blur)] border border-[var(--hairline)] rounded-[var(--radius)] shadow-[var(--shadow)] overflow-hidden',
    'card-pad': 'p-3',

    field:
      'w-full rounded-[var(--radius-sm)] border border-[var(--hairline)] bg-[var(--glass-2)] px-2.5 py-1.5 text-sm text-[var(--text)] outline-none transition-colors placeholder:text-[var(--text-faint)] focus:border-[var(--accent)] focus:ring-2 focus:ring-[var(--accent-soft)] disabled:opacity-50',
    label: 'block text-xs font-medium text-[var(--text-dim)]',

    btn: 'inline-flex select-none items-center justify-center gap-1.5 rounded-[var(--radius-sm)] px-3.5 py-1.5 text-sm font-medium transition-all duration-150 disabled:cursor-not-allowed disabled:opacity-50 active:scale-[0.98]',
    'btn-primary':
      'btn bg-[var(--accent)] text-white shadow-[0_4px_16px_rgba(0,174,236,0.32)] hover:bg-[var(--accent-strong)]',
    'btn-subtle':
      'btn border border-[var(--hairline)] bg-[var(--glass-2)] text-[var(--text)] hover:bg-[var(--glass-3)]',
    'btn-ghost':
      'btn border border-transparent text-[var(--text-dim)] hover:bg-[var(--glass-2)] hover:text-[var(--text)]',
    'btn-task':
      'inline-flex items-center rounded-[var(--radius-sm)] border border-[var(--hairline)] bg-[var(--glass-2)] px-2.5 py-1 text-xs text-[var(--text-dim)] transition-colors hover:border-[var(--accent)] hover:bg-[var(--accent-soft)] hover:text-[var(--text)]',

    check:
      'flex cursor-pointer select-none items-center gap-1.5 text-sm text-[var(--text-dim)] transition-colors hover:text-[var(--text)]',
    row: 'flex items-center gap-2.5',
    'row-label': 'w-24 shrink-0 text-sm text-[var(--text-dim)]',

    // 无框折叠区：仅标题 + 间距，去除盒子边框
    expander: 'overflow-hidden',
    'exp-head':
      'flex cursor-pointer list-none select-none items-center gap-2 px-1 py-2 text-sm font-semibold text-[var(--text)] transition-colors hover:text-[var(--accent)] [&::-webkit-details-marker]:hidden',

    group: 'card overflow-hidden',
    'group-head':
      'flex cursor-pointer list-none select-none items-center gap-2 px-3.5 py-2.5 text-sm font-semibold text-[var(--text)] transition-colors hover:bg-[var(--glass-2)] [&::-webkit-details-marker]:hidden',

    badge:
      'inline-flex shrink-0 items-center rounded-md border border-[var(--hairline)] bg-[var(--glass-2)] px-1.5 py-0.5 text-[11px] font-medium text-[var(--text-dim)]',
    stat: 'inline-flex items-center gap-1.5 rounded-full border border-[var(--hairline)] bg-[var(--glass-2)] px-2 py-0.5 text-xs text-[var(--text-dim)]',
    'stat-dot': 'h-1.5 w-1.5 rounded-full'
  }
})
