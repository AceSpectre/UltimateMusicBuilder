import type { LogLine } from '$lib/types/electron'

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
    logStore.entries = [...logStore.entries, entry]
  },

  clear() {
    logStore.entries = []
  }
})
