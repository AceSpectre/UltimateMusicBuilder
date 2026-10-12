import { modsStore } from '$lib/stores/mods.svelte'
import { createSeriesLoader } from '$lib/series-loader'
import type { ModInfo, ModSeriesInfo } from '$lib/types/electron'

/**
 * The active mod's series for a SeriesPicker view. Keeps modsStore.activeSeriesPath on one of
 * them (the first when the current one is gone). Call during component init.
 */
export function createSeriesSelection(getMod: () => ModInfo | null) {
  let series = $state<ModSeriesInfo[]>([])
  let loading = $state(false)
  const loader = createSeriesLoader((v) => (loading = v))

  async function reload() {
    const modPath = getMod()?.path
    if (!modPath) {
      loader.invalidate()
      series = []
      modsStore.activeSeriesPath = null
      return
    }

    const next = await loader.load(modPath)
    if (next === null) {
      return
    }
    series = next
    if (!next.some((entry) => entry.path === modsStore.activeSeriesPath)) {
      modsStore.activeSeriesPath = next[0]?.path ?? null
    }
  }

  $effect(() => {
    void reload()
  })

  return {
    get series() {
      return series
    },
    get loading() {
      return loading
    },
    get selected() {
      return series.find((entry) => entry.path === modsStore.activeSeriesPath) ?? null
    },
    reload
  }
}
