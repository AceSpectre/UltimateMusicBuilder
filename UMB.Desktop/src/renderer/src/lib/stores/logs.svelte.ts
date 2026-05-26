import type { LogLine } from '$lib/types/electron'

let entries = $state<LogLine[]>([])
let drawerOpen = $state(localStorage.getItem('umb-log-open') !== 'false')
let filter = $state<'all' | 'info' | 'warn' | 'error'>('all')

export const logStore = {
  get entries() { return entries },
  get lineCount() { return entries.length },

  get filtered() {
    if (filter === 'all') return entries
    return entries.filter(e => e.level === filter)
  },

  get drawerOpen() { return drawerOpen },
  get filter() { return filter },

  setFilter(f: 'all' | 'info' | 'warn' | 'error') {
    filter = f
  },

  toggleDrawer() {
    drawerOpen = !drawerOpen
    localStorage.setItem('umb-log-open', String(drawerOpen))
  },

  push(entry: LogLine) {
    entries = [...entries, entry]
  },

  clear() {
    entries = []
  }
}
