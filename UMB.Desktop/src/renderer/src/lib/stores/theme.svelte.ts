type Theme = 'light' | 'dark' | 'system'

function getInitialTheme(): Theme {
  const stored = localStorage.getItem('umb-theme')
  if (stored === 'light' || stored === 'dark' || stored === 'system') return stored
  return 'dark'
}

function applyTheme(theme: Theme) {
  const isDark = theme === 'dark' ||
    (theme === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches)

  document.documentElement.classList.toggle('dark', isDark)
}

const initialTheme = getInitialTheme()
applyTheme(initialTheme)

export const themeStore = $state({
  current: initialTheme as Theme,
  set(theme: Theme) {
    themeStore.current = theme
    localStorage.setItem('umb-theme', theme)
    applyTheme(theme)
  },

  toggle() {
    const next = themeStore.current === 'dark' ? 'light' : 'dark'
    themeStore.set(next)
  }
})
