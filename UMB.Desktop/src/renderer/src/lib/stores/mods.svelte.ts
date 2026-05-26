import type { ModInfo } from '$lib/types/electron'

let mods = $state<ModInfo[]>([])
let activeMod = $state<ModInfo | null>(null)
let loading = $state(false)

export const modsStore = {
  get mods() { return mods },
  get activeMod() { return activeMod },
  get loading() { return loading },

  async load() {
    loading = true
    try {
      mods = await window.electron.umb.listMods()
      if (mods.length > 0 && !activeMod) {
        activeMod = mods[0]
      }
    } finally {
      loading = false
    }
  },

  setActive(mod: ModInfo) {
    activeMod = mod
  }
}
