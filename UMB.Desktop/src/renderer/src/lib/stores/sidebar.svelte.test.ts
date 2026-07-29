import { afterEach, describe, expect, it, vi } from 'vitest'
import { installBrowserStubs, removeBrowserStubs, type Stubs } from './store-test-utils'

/** Collapse state is read at module load and written back on every toggle. */

let stubs: Stubs
let sidebarStore: typeof import('./sidebar.svelte').sidebarStore

async function load(localStorageState: Record<string, string> = {}): Promise<void> {
  vi.resetModules()
  stubs = installBrowserStubs({ localStorage: localStorageState })
  sidebarStore = (await import('./sidebar.svelte')).sidebarStore
}

afterEach(() => {
  removeBrowserStubs()
})

describe('collapsed', () => {
  it('starts expanded on a fresh profile', async () => {
    await load()
    expect(sidebarStore.collapsed).toBe(false)
  })

  it('restores a collapsed sidebar from localStorage', async () => {
    await load({ 'umb-sidebar-collapsed': 'true' })
    expect(sidebarStore.collapsed).toBe(true)
  })

  it('treats any non-"true" stored value as expanded', async () => {
    await load({ 'umb-sidebar-collapsed': 'yes' })
    expect(sidebarStore.collapsed).toBe(false)
  })

  it('toggles and persists the new state', async () => {
    await load()

    sidebarStore.toggle()
    expect(sidebarStore.collapsed).toBe(true)
    expect(stubs.storage.getItem('umb-sidebar-collapsed')).toBe('true')

    sidebarStore.toggle()
    expect(sidebarStore.collapsed).toBe(false)
    expect(stubs.storage.getItem('umb-sidebar-collapsed')).toBe('false')
  })
})

describe('activeTab', () => {
  it('defaults to build', async () => {
    await load()
    expect(sidebarStore.activeTab).toBe('build')
  })

  it('setActive switches the tab', async () => {
    await load()
    sidebarStore.setActive('config-volume')
    expect(sidebarStore.activeTab).toBe('config-volume')
  })

  it('setActive is independent of the collapse state', async () => {
    await load({ 'umb-sidebar-collapsed': 'true' })
    sidebarStore.setActive('merge')
    expect(sidebarStore.activeTab).toBe('merge')
    expect(sidebarStore.collapsed).toBe(true)
  })
})
