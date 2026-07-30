import type { LogLine } from '$lib/types/electron'

// Retain at most this many lines in memory; drop oldest beyond it. A build can emit
// thousands of lines (e.g. duplicate-entry warnings) and unbounded growth makes every
// subsequent update slower.
const MAX_ENTRIES = 5000

// Incoming lines are buffered and flushed once per animation frame. During a build the
// CLI emits lines in bursts; coalescing them into one reactive update per frame turns
// thousands of re-renders into a handful.
let pending: LogLine[] = []
let flushScheduled = false

function scheduleFlush(): void {
  if (flushScheduled) return
  flushScheduled = true
  requestAnimationFrame(flush)
}

function flush(): void {
  flushScheduled = false
  if (pending.length === 0) return
  const next = logStore.entries.concat(pending)
  pending = []
  if (next.length > MAX_ENTRIES) next.splice(0, next.length - MAX_ENTRIES)
  logStore.entries = next
}

export const logStore = $state({
  entries: [] as LogLine[],
  drawerOpen: localStorage.getItem('umb-log-open') !== 'false',
  filter: 'all' as 'all' | 'info' | 'warn' | 'error',
  get lineCount() {
    return logStore.entries.length
  },
  get filtered() {
    if (logStore.filter === 'all') return logStore.entries
    return logStore.entries.filter((entry) => entry.level === logStore.filter)
  },
  setFilter(f: 'all' | 'info' | 'warn' | 'error') {
    logStore.filter = f
  },

  toggleDrawer() {
    logStore.drawerOpen = !logStore.drawerOpen
    localStorage.setItem('umb-log-open', String(logStore.drawerOpen))
  },

  push(entry: LogLine) {
    pending.push(entry)
    scheduleFlush()
  },

  clear() {
    pending = []
    logStore.entries = []
  }
})
