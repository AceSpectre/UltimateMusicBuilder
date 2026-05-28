import type { ModInfo } from '$lib/types/electron'

export const modsStore = $state({
  mods: [] as ModInfo[],
  activeMod: null as ModInfo | null,
  activeSeriesPath: null as string | null,
  loading: false,
  async load() {
    modsStore.loading = true
    try {
      modsStore.mods = await window.electron.umb.listMods()
      if (modsStore.mods.length > 0 && !modsStore.activeMod) {
        modsStore.activeMod = modsStore.mods[0]
      }
    } finally {
      modsStore.loading = false
    }
  },

  setActive(mod: ModInfo) {
    modsStore.activeMod = mod
  }
})
