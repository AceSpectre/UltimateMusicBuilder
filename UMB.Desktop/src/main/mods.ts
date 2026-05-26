import { readdirSync, statSync } from 'fs'
import { join } from 'path'

export interface ModInfo {
  name: string
  path: string
}

export function listMods(workspace: string): ModInfo[] {
  const modsDir = join(workspace, 'Mods', 'MusicMods')
  try {
    const entries = readdirSync(modsDir)
    return entries
      .filter((entry) => {
        const fullPath = join(modsDir, entry)
        return statSync(fullPath).isDirectory()
      })
      .map((entry) => ({
        name: entry,
        path: join(modsDir, entry)
      }))
  } catch {
    return []
  }
}
