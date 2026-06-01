import { existsSync, readdirSync, readFileSync, statSync } from 'fs'
import { isAbsolute, join, relative, resolve } from 'path'

export interface ModInfo {
  name: string
  path: string
}

export interface ModSeriesInfo {
  name: string
  path: string
}

export interface ModStats {
  seriesCount: number
  trackCount: number
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

/** Counts data rows (non-empty, excluding the header) in a tracks.csv. */
function countCsvTracks(csvPath: string): number {
  try {
    const lines = readFileSync(csvPath, 'utf-8')
      .split(/\r?\n/)
      .filter((l) => l.trim().length > 0)
    return Math.max(0, lines.length - 1)
  } catch {
    return 0
  }
}

export function getModStats(workspace: string, modPath: string): ModStats {
  const series = listModSeries(workspace, modPath)
  let trackCount = 0
  for (const s of series) {
    trackCount += countCsvTracks(join(s.path, 'tracks.csv'))
  }
  return { seriesCount: series.length, trackCount }
}
