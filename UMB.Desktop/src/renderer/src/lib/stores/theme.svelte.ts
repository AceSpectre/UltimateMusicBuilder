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

let current = $state<Theme>(getInitialTheme())
applyTheme(current)

export const themeStore = {
  get current() { return current },

  set(theme: Theme) {
    current = theme
    localStorage.setItem('umb-theme', theme)
    applyTheme(theme)
  },

  toggle() {
    const next = current === 'dark' ? 'light' : 'dark'
    themeStore.set(next)
  }
}
