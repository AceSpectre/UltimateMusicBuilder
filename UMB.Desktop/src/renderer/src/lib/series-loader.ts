import type { ModSeriesInfo } from '$lib/types/electron'

/**
 * Token-guarded series-list loader shared by the views with a SeriesPicker sidebar.
 * load() resolves to null when a newer load()/invalidate() superseded it — stale
 * responses must be ignored by the caller. The loading flag is only cleared by the
 * most recent request, so rapid mod switches don't flicker it off early.
 */
export function createSeriesLoader(setLoading: (loading: boolean) => void) {
  let token = 0
  return {
    invalidate(): void {
      token += 1
    },
    async load(modPath: string): Promise<ModSeriesInfo[] | null> {
      token += 1
      const current = token
      setLoading(true)
      try {
        const next = await window.electron.umb.listModSeries(modPath)
        return current === token ? next : null
      } finally {
        if (current === token) setLoading(false)
      }
    }
  }
}
