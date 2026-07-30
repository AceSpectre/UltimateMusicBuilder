import { afterEach, describe, expect, it, vi } from 'vitest'
import { installBrowserStubs, removeBrowserStubs, type Stubs } from './store-test-utils'

// Covers stored-value parsing and the <html class="dark"> side effect.

let stubs: Stubs
let themeStore: typeof import('./theme.svelte').themeStore

async function load(
  localStorageState: Record<string, string> = {},
  prefersDark = false
): Promise<void> {
  vi.resetModules()
  stubs = installBrowserStubs({ localStorage: localStorageState, prefersDark })
  themeStore = (await import('./theme.svelte')).themeStore
}

const isDark = (): boolean => stubs.rootClasses.has('dark')

afterEach(() => {
  removeBrowserStubs()
})

describe('initial theme', () => {
  it('defaults to dark on a fresh profile', async () => {
    await load()
    expect(themeStore.current).toBe('dark')
    expect(isDark()).toBe(true)
  })

  it.each(['light', 'dark', 'system'] as const)('restores the stored %s theme', async (theme) => {
    await load({ 'umb-theme': theme })
    expect(themeStore.current).toBe(theme)
  })

  it('falls back to dark when the stored value is not a known theme', async () => {
    await load({ 'umb-theme': 'solarized' })
    expect(themeStore.current).toBe('dark')
    expect(isDark()).toBe(true)
  })

  it('applies the class on load, not just on the first toggle', async () => {
    await load({ 'umb-theme': 'light' })
    expect(isDark()).toBe(false)
  })
})

describe('system theme', () => {
  it('is dark when the OS prefers dark', async () => {
    await load({ 'umb-theme': 'system' }, true)
    expect(isDark()).toBe(true)
  })

  it('is light when the OS prefers light', async () => {
    await load({ 'umb-theme': 'system' }, false)
    expect(isDark()).toBe(false)
  })
})

describe('set', () => {
  it('updates the current theme, the class and the stored value', async () => {
    await load({ 'umb-theme': 'dark' })

    themeStore.set('light')
    expect(themeStore.current).toBe('light')
    expect(isDark()).toBe(false)
    expect(stubs.storage.getItem('umb-theme')).toBe('light')
  })

  it('persists system without resolving it to a concrete theme', async () => {
    await load({ 'umb-theme': 'light' }, true)

    themeStore.set('system')
    expect(stubs.storage.getItem('umb-theme')).toBe('system')
    expect(themeStore.current).toBe('system')
    expect(isDark()).toBe(true)
  })
})

describe('toggle', () => {
  it('goes dark to light', async () => {
    await load({ 'umb-theme': 'dark' })
    themeStore.toggle()
    expect(themeStore.current).toBe('light')
    expect(isDark()).toBe(false)
  })

  it('goes light to dark', async () => {
    await load({ 'umb-theme': 'light' })
    themeStore.toggle()
    expect(themeStore.current).toBe('dark')
    expect(isDark()).toBe(true)
  })

  it('treats system as non-dark and moves to dark', async () => {
    await load({ 'umb-theme': 'system' }, true)
    themeStore.toggle()
    expect(themeStore.current).toBe('dark')
  })

  it('round-trips back to the original theme', async () => {
    await load({ 'umb-theme': 'dark' })
    themeStore.toggle()
    themeStore.toggle()
    expect(themeStore.current).toBe('dark')
    expect(stubs.storage.getItem('umb-theme')).toBe('dark')
  })
})
