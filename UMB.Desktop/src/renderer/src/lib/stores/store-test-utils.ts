// Browser-global stubs; must be installed before the dynamic store import.

export interface FakeStorage {
  getItem(key: string): string | null
  setItem(key: string, value: string): void
}

function fakeStorage(initial: Record<string, string> = {}): FakeStorage {
  const store = new Map(Object.entries(initial))
  return {
    getItem: (key) => store.get(key) ?? null,
    setItem: (key, value) => {
      store.set(key, value)
    }
  }
}

export interface StubOptions {
  localStorage?: Record<string, string>
  /** What `matchMedia('(prefers-color-scheme: dark)')` should report. */
  prefersDark?: boolean
}

export interface Stubs {
  storage: FakeStorage
  /** Classes currently on <html>, as applyTheme would have left them. */
  rootClasses: Set<string>
  /** Runs every queued requestAnimationFrame callback. */
  flushFrames(): void
}

/** Installs the browser globals the stores expect and returns handles to them. */
export function installBrowserStubs(options: StubOptions = {}): Stubs {
  const storage = fakeStorage(options.localStorage)
  const rootClasses = new Set<string>()
  let frames: FrameRequestCallback[] = []

  const classList = {
    toggle: (c: string, force?: boolean) => {
      const next = force ?? !rootClasses.has(c)
      if (next) rootClasses.add(c)
      else rootClasses.delete(c)
      return next
    }
  }

  const g = globalThis as any
  g.localStorage = storage
  g.document = { documentElement: { classList } }
  g.requestAnimationFrame = (cb: FrameRequestCallback): number => {
    frames.push(cb)
    return frames.length
  }
  g.matchMedia = (): { matches: boolean } => ({ matches: options.prefersDark ?? false })
  g.window = {
    localStorage: storage,
    matchMedia: g.matchMedia,
    requestAnimationFrame: g.requestAnimationFrame
  }

  return {
    storage,
    rootClasses,
    flushFrames() {
      // Drain only what is currently queued.
      const queued = frames
      frames = []
      for (const cb of queued) cb(0)
    }
  }
}

export function removeBrowserStubs(): void {
  const g = globalThis as any
  delete g.localStorage
  delete g.document
  delete g.requestAnimationFrame
  delete g.matchMedia
  delete g.window
}
