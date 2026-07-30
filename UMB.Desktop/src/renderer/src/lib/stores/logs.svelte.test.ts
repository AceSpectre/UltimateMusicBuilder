import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import type { LogLine } from '$lib/types/electron'
import { installBrowserStubs, removeBrowserStubs, type Stubs } from './store-test-utils'

// Covers frame coalescing and MAX_ENTRIES.

const MAX_ENTRIES = 5000

let stubs: Stubs
let logStore: typeof import('./logs.svelte').logStore

function line(message: string, level: LogLine['level'] = 'info'): LogLine {
  return { timestamp: '12:00:00', level, message }
}

/** Pushes lines and runs the pending frame so they land in `entries`. */
function push(...lines: LogLine[]): void {
  for (const l of lines) logStore.push(l)
  stubs.flushFrames()
}

async function load(localStorageState: Record<string, string> = {}): Promise<void> {
  const { vi } = await import('vitest')
  vi.resetModules()
  stubs = installBrowserStubs({ localStorage: localStorageState })
  logStore = (await import('./logs.svelte')).logStore
}

beforeEach(async () => {
  await load()
})

afterEach(() => {
  removeBrowserStubs()
})

describe('push coalescing', () => {
  it('buffers pushes until the next animation frame', () => {
    logStore.push(line('a'))
    logStore.push(line('b'))

    expect(logStore.entries).toEqual([])

    stubs.flushFrames()
    expect(logStore.entries.map((e) => e.message)).toEqual(['a', 'b'])
  })

  it('schedules exactly one frame per burst, and re-arms after it fires', () => {
    let frames = 0
    const queued: FrameRequestCallback[] = []
    ;(globalThis as any).requestAnimationFrame = (cb: FrameRequestCallback): number => {
      frames++
      queued.push(cb)
      return frames
    }

    logStore.push(line('a'))
    logStore.push(line('b'))
    logStore.push(line('c'))

    expect(frames).toBe(1)

    queued.splice(0).forEach((cb) => cb(0))
    expect(logStore.lineCount).toBe(3)

    logStore.push(line('d'))
    expect(frames).toBe(2)
    queued.splice(0).forEach((cb) => cb(0))
    expect(logStore.lineCount).toBe(4)
  })

  it('appends across separate bursts in order', () => {
    push(line('a'))
    push(line('b'))
    expect(logStore.entries.map((e) => e.message)).toEqual(['a', 'b'])
  })
})

describe('MAX_ENTRIES cap', () => {
  it('keeps every line below the cap', () => {
    push(...Array.from({ length: 100 }, (_, i) => line(`l${i}`)))
    expect(logStore.lineCount).toBe(100)
  })

  it('drops the oldest lines once the cap is exceeded', () => {
    push(...Array.from({ length: MAX_ENTRIES + 10 }, (_, i) => line(`l${i}`)))

    expect(logStore.lineCount).toBe(MAX_ENTRIES)
    expect(logStore.entries[0].message).toBe('l10')
    expect(logStore.entries.at(-1)!.message).toBe(`l${MAX_ENTRIES + 9}`)
  })

  it('enforces the cap across multiple bursts, not just one', () => {
    push(...Array.from({ length: MAX_ENTRIES }, (_, i) => line(`a${i}`)))
    push(...Array.from({ length: 5 }, (_, i) => line(`b${i}`)))

    expect(logStore.lineCount).toBe(MAX_ENTRIES)
    expect(logStore.entries.at(-1)!.message).toBe('b4')
    expect(logStore.entries[0].message).toBe('a5')
  })
})

describe('filter', () => {
  beforeEach(() => {
    push(line('i1'), line('w1', 'warn'), line('e1', 'error'), line('i2'), line('w2', 'warn'))
  })

  it('defaults to all', () => {
    expect(logStore.filter).toBe('all')
    expect(logStore.filtered).toHaveLength(5)
  })

  it.each([
    ['info', ['i1', 'i2']],
    ['warn', ['w1', 'w2']],
    ['error', ['e1']]
  ] as const)('narrows to %s only', (level, expected) => {
    logStore.setFilter(level)
    expect(logStore.filtered.map((e) => e.message)).toEqual(expected)
  })

  it('returns to the full list on all', () => {
    logStore.setFilter('error')
    logStore.setFilter('all')
    expect(logStore.filtered).toHaveLength(5)
  })

  it('reports the unfiltered total in lineCount', () => {
    logStore.setFilter('error')
    expect(logStore.lineCount).toBe(5)
    expect(logStore.filtered).toHaveLength(1)
  })
})

describe('clear', () => {
  it('empties the store', () => {
    push(line('a'), line('b'))
    logStore.clear()
    expect(logStore.entries).toEqual([])
    expect(logStore.lineCount).toBe(0)
  })

  it('discards lines that were pushed but not yet flushed', () => {
    logStore.push(line('pending'))
    logStore.clear()
    stubs.flushFrames()

    expect(logStore.entries).toEqual([])
  })

  it('leaves the active filter alone', () => {
    logStore.setFilter('error')
    logStore.clear()
    expect(logStore.filter).toBe('error')
  })
})

describe('drawer', () => {
  it('defaults to open on a fresh profile', () => {
    expect(logStore.drawerOpen).toBe(true)
  })

  it('reopens closed when localStorage says so', async () => {
    await load({ 'umb-log-open': 'false' })
    expect(logStore.drawerOpen).toBe(false)
  })

  it('treats any other stored value as open', async () => {
    await load({ 'umb-log-open': 'true' })
    expect(logStore.drawerOpen).toBe(true)
  })

  it('toggles and persists the new state', () => {
    logStore.toggleDrawer()
    expect(logStore.drawerOpen).toBe(false)
    expect(stubs.storage.getItem('umb-log-open')).toBe('false')

    logStore.toggleDrawer()
    expect(logStore.drawerOpen).toBe(true)
    expect(stubs.storage.getItem('umb-log-open')).toBe('true')
  })
})
