import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ModInfo } from '$lib/types/electron'
import { installBrowserStubs, removeBrowserStubs } from './store-test-utils'

/**
 * modsStore.load() is what every view depends on for its mod list. The parts worth
 * pinning are the auto-select of the first mod (only when nothing is selected yet)
 * and the loading flag, which must clear even when the IPC call fails or the view
 * would spin forever.
 */

let modsStore: typeof import('./mods.svelte').modsStore
let listMods: ReturnType<typeof vi.fn>

const MOD_A: ModInfo = { name: 'mod-a', path: '/mods/mod-a' } as ModInfo
const MOD_B: ModInfo = { name: 'mod-b', path: '/mods/mod-b' } as ModInfo

beforeEach(async () => {
  vi.resetModules()
  installBrowserStubs()
  listMods = vi.fn()
  ;(globalThis as any).window.electron = { umb: { listMods } }
  modsStore = (await import('./mods.svelte')).modsStore
})

afterEach(() => {
  removeBrowserStubs()
})

describe('initial state', () => {
  it('starts empty with nothing selected', () => {
    expect(modsStore.mods).toEqual([])
    expect(modsStore.activeMod).toBeNull()
    expect(modsStore.activeSeriesPath).toBeNull()
    expect(modsStore.pendingConfigVolumePath).toBeNull()
    expect(modsStore.loading).toBe(false)
  })
})

describe('load', () => {
  it('populates the list and auto-selects the first mod', async () => {
    listMods.mockResolvedValue([MOD_A, MOD_B])

    await modsStore.load()

    expect(modsStore.mods).toEqual([MOD_A, MOD_B])
    expect(modsStore.activeMod).toBe(MOD_A)
    expect(modsStore.loading).toBe(false)
  })

  it('leaves an already-chosen mod selected on a reload', async () => {
    listMods.mockResolvedValue([MOD_A, MOD_B])
    await modsStore.load()
    modsStore.setActive(MOD_B)

    await modsStore.load()

    // Re-listing mods must not yank the user back to the first one.
    expect(modsStore.activeMod).toBe(MOD_B)
  })

  it('selects nothing when there are no mods', async () => {
    listMods.mockResolvedValue([])

    await modsStore.load()

    expect(modsStore.mods).toEqual([])
    expect(modsStore.activeMod).toBeNull()
  })

  it('sets loading while in flight and clears it after', async () => {
    let resolveList: (mods: ModInfo[]) => void = () => {}
    listMods.mockReturnValue(new Promise<ModInfo[]>((r) => { resolveList = r }))

    const pending = modsStore.load()
    expect(modsStore.loading).toBe(true)

    resolveList([MOD_A])
    await pending
    expect(modsStore.loading).toBe(false)
  })

  it('clears loading when the IPC call rejects', async () => {
    listMods.mockRejectedValue(new Error('bridge down'))

    await expect(modsStore.load()).rejects.toThrow('bridge down')
    // Without the finally, the whole UI would sit on a spinner forever.
    expect(modsStore.loading).toBe(false)
  })
})

describe('setActive', () => {
  it('replaces the active mod', () => {
    modsStore.setActive(MOD_A)
    expect(modsStore.activeMod).toBe(MOD_A)

    modsStore.setActive(MOD_B)
    expect(modsStore.activeMod).toBe(MOD_B)
  })
})

describe('cross-view handoff', () => {
  it('carries a pending Config Volume series path for the receiving view to consume', () => {
    // Manage Songs sets this; Config Volume reads it once and nulls it out.
    modsStore.pendingConfigVolumePath = '/mods/mod-a/dev'
    expect(modsStore.pendingConfigVolumePath).toBe('/mods/mod-a/dev')

    modsStore.pendingConfigVolumePath = null
    expect(modsStore.pendingConfigVolumePath).toBeNull()
  })
})
