import type { Config } from 'tailwindcss'

const token = (name: string) => `rgb(var(--${name}) / <alpha-value>)`

export default {
  darkMode: 'class',
  content: ['./src/renderer/**/*.{html,svelte,js,ts}'],
  theme: {
    extend: {
      // shadcn-style names mapped onto the design tokens in app.css, plus the raw tokens.
      colors: {
        background: token('bg'),
        foreground: token('ink'),
        card: { DEFAULT: token('surf'), foreground: token('ink') },
        popover: { DEFAULT: token('surf'), foreground: token('ink') },
        primary: { DEFAULT: token('acc'), foreground: token('btnink') },
        secondary: { DEFAULT: token('secbg'), foreground: token('secink') },
        muted: { DEFAULT: token('chip'), foreground: token('mut') },
        accent: { DEFAULT: token('hov'), foreground: token('ink') },
        destructive: { DEFAULT: token('err'), foreground: token('btnink') },
        border: token('line'),
        input: token('line'),
        ring: token('acc'),
        rail: token('rail'),
        line2: token('line2'),
        dim: token('dim'),
        accbg: token('accbg'),
        accink: token('accink'),
        sec: token('sec'),
        sb: { DEFAULT: token('sb'), ink: token('sbink'), acc: token('sbacc') },
        warn: token('warn')
      },
      fontFamily: {
        sans: ['Geist Sans', 'system-ui', 'sans-serif'],
        mono: ['Geist Mono', 'ui-monospace', 'monospace']
      },
      borderRadius: {
        '2xl': '5px',
        xl: '5px',
        lg: '4px',
        md: '4px',
        sm: '3px'
      },
      opacity: {
        45: '0.45'
      }
    }
  },
  plugins: []
} satisfies Config
