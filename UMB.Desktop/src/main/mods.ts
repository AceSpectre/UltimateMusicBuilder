import { existsSync, readdirSync, statSync } from 'fs'
import { join, resolve } from 'path'
import { isChildOf } from './utils'
import { countCsvDataRows } from './csv-utils'
import type { ModInfo, ModSeriesInfo, ModStats } from '../shared/types'

export type { ModInfo, ModSeriesInfo, ModStats } from '../shared/types'

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

export function getModStats(workspace: string, modPath: string): ModStats {
  const series = listModSeries(workspace, modPath)
  let trackCount = 0
  for (const s of series) {
    trackCount += countCsvDataRows(join(s.path, 'tracks.csv'))
  }
  return { seriesCount: series.length, trackCount }
}
