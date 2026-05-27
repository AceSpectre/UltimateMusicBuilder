import { existsSync, readdirSync, statSync } from 'fs'
import { isAbsolute, join, relative, resolve } from 'path'

export interface ModInfo {
  name: string
  path: string
}

export interface ModSeriesInfo {
  name: string
  path: string
}

function isChildOf(parentPath: string, childPath: string): boolean {
  const rel = relative(parentPath, childPath)
  return rel !== '' && !rel.startsWith('..') && !isAbsolute(rel)
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

export function listModSeries(workspace: string, modPath: string): ModSeriesInfo[] {
  const modsDir = resolve(workspace, 'Mods', 'MusicMods')
  const resolvedModPath = resolve(modPath)

  if (!isChildOf(modsDir, resolvedModPath)) {
    return []
  }

  try {
    return readdirSync(resolvedModPath)
      .filter((entry) => {
        const fullPath = join(resolvedModPath, entry)
        if (entry.startsWith('.') || entry === 'songs-to-validate') {
          return false
        }
        return statSync(fullPath).isDirectory() && existsSync(join(fullPath, 'tracks.csv'))
      })
      .map((entry) => ({
        name: entry,
        path: join(resolvedModPath, entry)
      }))
  } catch {
    return []
  }
}
