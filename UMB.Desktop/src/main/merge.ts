import {
  existsSync, readdirSync, readFileSync, writeFileSync,
  copyFileSync, mkdirSync, statSync
} from 'fs'
import { join, resolve, basename } from 'path'
import { parse as parseToml } from 'smol-toml'
import { stringify as stringifyCsv } from 'csv-stringify/sync'
import { isChildOf, getMusicModsRoot, log } from './utils'
import { tomlEscape } from './toml-utils'
import { readCsvLenient, countCsvDataRows } from './csv-utils'
import type { LogLine, MergeAnalysis, MergeConflict, MergeResult, MergeSeriesSource } from '../shared/types'

export type { MergeAnalysis, MergeConflict, MergeResult, MergeSeriesSource } from '../shared/types'

const SERIES_TOML = 'series.toml'
const TRACKS_CSV = 'tracks.csv'
const SERIES_ORDER_TOML = 'series-order.toml'

export function analyzeMerge(workspace: string, modPaths: string[]): MergeAnalysis {
  const modsDir = getMusicModsRoot(workspace)

  const resolvedPaths = modPaths.map(p => resolve(p))
  for (const p of resolvedPaths) {
    if (!isChildOf(modsDir, p)) throw new Error(`Invalid mod path: ${p}`)
    if (!existsSync(p) || !statSync(p).isDirectory()) throw new Error(`Mod not found: ${p}`)
  }

  const modNames = resolvedPaths.map(p => basename(p))

  const seriesMap: Record<string, MergeSeriesSource[]> = {}

  for (const modPath of resolvedPaths) {
    const modName = basename(modPath)
    try {
      for (const entry of readdirSync(modPath)) {
        if (entry.startsWith('.')) continue
        const seriesDir = join(modPath, entry)
        if (!statSync(seriesDir).isDirectory()) continue
        if (!existsSync(join(seriesDir, SERIES_TOML))) continue

        if (!seriesMap[entry]) seriesMap[entry] = []
        seriesMap[entry].push({ modName, modPath, seriesPath: seriesDir })
      }
    } catch { /* skip unreadable dirs */ }
  }

  const series = Object.entries(seriesMap)
    .sort(([a], [b]) => a.localeCompare(b, undefined, { sensitivity: 'base' }))
    .map(([name, sources]) => ({ name, sources }))

  const conflicts: MergeConflict[] = series
    .filter(s => s.sources.length > 1)
    .map(s => ({ seriesName: s.name, mods: s.sources.map(src => src.modName) }))

  return {
    modNames,
    modPaths: resolvedPaths,
    series,
    conflicts,
    totalSeries: series.length
  }
}

export function validateOutputName(workspace: string, name: string): string | null {
  if (!name || !name.trim()) return 'Name cannot be empty.'
  const trimmed = name.trim()
  if (/[<>:"/\\|?*]/.test(trimmed)) return 'Name contains invalid characters.'

  const modsDir = getMusicModsRoot(workspace)
  if (existsSync(join(modsDir, trimmed))) return 'A mod with that name already exists.'

  return null
}

export async function executeMerge(
  workspace: string,
  modPaths: string[],
  outputName: string,
  priorityModPath: string | null,
  onLine: (line: LogLine) => void
): Promise<MergeResult> {
  const nameError = validateOutputName(workspace, outputName)
  if (nameError) throw new Error(nameError)

  const analysis = analyzeMerge(workspace, modPaths)
  if (analysis.totalSeries === 0) {
    throw new Error('No series folders found in the selected mods.')
  }

  const modsDir = getMusicModsRoot(workspace)
  const outputDir = join(modsDir, outputName.trim())
  mkdirSync(outputDir, { recursive: true })

  let totalSeries = 0
  let totalTracks = 0
  let conflictsResolved = 0

  for (const { name: seriesName, sources } of analysis.series) {
    const outputSeriesDir = join(outputDir, seriesName)
    mkdirSync(outputSeriesDir, { recursive: true })

    if (sources.length === 1) {
      copySeriesFolder(sources[0].seriesPath, outputSeriesDir)
      const trackCount = countCsvDataRows(join(outputSeriesDir, TRACKS_CSV))
      totalTracks += trackCount
      onLine(log('info', `Copied series '${seriesName}' from ${sources[0].modName} (${trackCount} tracks)`))
    } else {
      const orderedDirs = sources
        .sort((a, b) => {
          if (priorityModPath && a.modPath === resolve(priorityModPath)) return -1
          if (priorityModPath && b.modPath === resolve(priorityModPath)) return 1
          return 0
        })
        .map(s => s.seriesPath)

      const trackCount = mergeSeriesFolders(orderedDirs, outputSeriesDir, seriesName, onLine)
      totalTracks += trackCount
      conflictsResolved++
      onLine(log('info', `Merged series '${seriesName}' from ${sources.length} mods (${trackCount} tracks)`))
    }

    totalSeries++
  }

  mergeSeriesOrderToml(
    analysis.modPaths,
    priorityModPath ? resolve(priorityModPath) : null,
    outputDir,
    onLine
  )

  onLine(log('info', `Merge complete: ${totalSeries} series, ${totalTracks} tracks`))

  return { outputPath: outputDir, outputName: outputName.trim(), totalSeries, totalTracks, conflictsResolved }
}

function copySeriesFolder(sourceDir: string, outputDir: string): void {
  for (const file of readdirSync(sourceDir)) {
    const srcPath = join(sourceDir, file)
    if (statSync(srcPath).isFile()) {
      copyFileSync(srcPath, join(outputDir, file))
    }
  }
}

interface SeriesTomlData {
  series: Record<string, unknown>
  games: Record<string, unknown>[]
  playlists: Record<string, unknown>[]
  defaultTrackData: Record<string, unknown> | null
}

function parseSeriesToml(filePath: string): SeriesTomlData | null {
  try {
    const raw = parseToml(readFileSync(filePath, 'utf-8'))

    const series = (raw['series'] ?? {}) as Record<string, unknown>
    const games = (Array.isArray(raw['games']) ? raw['games'] : []) as Record<string, unknown>[]
    const playlists = (Array.isArray(raw['playlists']) ? raw['playlists'] : []) as Record<string, unknown>[]
    const defaultTrackData = (raw['default-track-data'] ?? null) as Record<string, unknown> | null

    return { series, games, playlists, defaultTrackData }
  } catch {
    return null
  }
}

function formatSongsField(songs: unknown): string {
  if (songs == null || songs === '*') return 'songs = "*"'
  if (Array.isArray(songs)) {
    if (songs.length === 0) return 'songs = "*"'
    const items = songs.map((s: unknown) => `    "${tomlEscape(String(s))}"`)
    return `songs = [\n${items.join(',\n')}\n]`
  }
  return 'songs = "*"'
}

function writeMergedSeriesToml(
  outputDir: string,
  prioritySeries: Record<string, unknown>,
  games: { id: string; name: string }[],
  playlists: { id: string; incidence: number; songs: unknown }[],
  defaultTrackData: Record<string, unknown> | null
): void {
  const lines: string[] = []
  lines.push('[series]')
  lines.push(`id = "${tomlEscape(String(prioritySeries['id'] ?? ''))}"`)
  lines.push(`name = "${tomlEscape(String(prioritySeries['name'] ?? ''))}"`)
  if (prioritySeries['existing-series']) lines.push('existing-series = true')
  const incidence = prioritySeries['playlist-incidence']
  if (incidence != null && incidence !== 100) lines.push(`playlist-incidence = ${incidence}`)
  const seriesPlaylist = prioritySeries['series-playlist']
  if (seriesPlaylist) lines.push(`series-playlist = "${tomlEscape(String(seriesPlaylist))}"`)
  lines.push('')

  for (const g of games) {
    lines.push('[[games]]')
    lines.push(`id = "${tomlEscape(g.id)}"`)
    lines.push(`name = "${tomlEscape(g.name)}"`)
    lines.push('')
  }

  for (const p of playlists) {
    lines.push('[[playlists]]')
    lines.push(`id = "${tomlEscape(p.id)}"`)
    lines.push(`incidence = ${p.incidence}`)
    lines.push(formatSongsField(p.songs))
    lines.push('')
  }

  if (defaultTrackData) {
    lines.push('[default-track-data]')
    if (defaultTrackData['game']) lines.push(`game = "${tomlEscape(String(defaultTrackData['game']))}"`)
    if (defaultTrackData['author']) lines.push(`author = "${tomlEscape(String(defaultTrackData['author']))}"`)
    if (defaultTrackData['copyright']) lines.push(`copyright = "${tomlEscape(String(defaultTrackData['copyright']))}"`)
    lines.push(`record-type = "${tomlEscape(String(defaultTrackData['record-type'] ?? 'original'))}"`)
    lines.push(`volume = ${defaultTrackData['volume'] ?? 1}`)
    lines.push('')
  }

  writeFileSync(join(outputDir, SERIES_TOML), lines.join('\n'))
}

interface TrackRow {
  filename: string
  game: string
  title: string
  author: string
  copyright: string
  record_type: string
  special_category: string
  volume: string
  info1: string
  in_soundtest: string
}

function writeTracksCsv(outputDir: string, tracks: TrackRow[]): void {
  const headers = [
    'filename', 'game', 'title', 'author', 'copyright',
    'record_type', 'special_category', 'volume', 'info1', 'in_soundtest', 'order'
  ]
  const rows = tracks.map((t, i) => ({
    filename: t.filename ?? '',
    game: t.game ?? '',
    title: t.title ?? '',
    author: t.author ?? '',
    copyright: t.copyright ?? '',
    record_type: t.record_type ?? 'original',
    special_category: t.special_category ?? '',
    volume: t.volume ?? '1',
    info1: t.info1 ?? '',
    in_soundtest: t.in_soundtest ?? 'True',
    order: String(i)
  }))

  const output = stringifyCsv(rows, { header: true, columns: headers })
  writeFileSync(join(outputDir, TRACKS_CSV), output)
}

function mergeSeriesFolders(
  orderedSourceDirs: string[],
  outputDir: string,
  seriesName: string,
  onLine: (line: LogLine) => void
): number {
  let priorityConfig: SeriesTomlData | null = null
  const mergedGames: { id: string; name: string }[] = []
  const mergedPlaylists: { id: string; incidence: number; songs: unknown }[] = []

  for (const srcDir of orderedSourceDirs) {
    const tomlPath = join(srcDir, SERIES_TOML)
    const config = parseSeriesToml(tomlPath)
    if (!config) {
      onLine(log('warn', `Failed to parse ${tomlPath}, skipping.`))
      continue
    }

    if (!priorityConfig) priorityConfig = config

    for (const g of config.games) {
      const id = String(g['id'] ?? '')
      if (id && !mergedGames.some(mg => mg.id.toLowerCase() === id.toLowerCase())) {
        mergedGames.push({ id, name: String(g['name'] ?? '') })
      }
    }

    for (const p of config.playlists) {
      const id = String(p['id'] ?? '')
      if (id && !mergedPlaylists.some(mp => mp.id.toLowerCase() === id.toLowerCase())) {
        mergedPlaylists.push({ id, incidence: Number(p['incidence'] ?? 100), songs: p['songs'] })
      }
    }
  }

  if (!priorityConfig) {
    onLine(log('error', `No valid series.toml found for series '${seriesName}'.`))
    return 0
  }

  writeMergedSeriesToml(outputDir, priorityConfig.series, mergedGames, mergedPlaylists, priorityConfig.defaultTrackData)

  const allTracks: TrackRow[] = []
  const seenFilenames = new Set<string>()

  for (const srcDir of orderedSourceDirs) {
    const tracks = readCsvLenient(join(srcDir, TRACKS_CSV)) as unknown as TrackRow[]
    for (const track of tracks) {
      const key = (track.filename ?? '').toLowerCase()
      if (key && !seenFilenames.has(key)) {
        seenFilenames.add(key)
        allTracks.push(track)
      }
    }
  }

  writeTracksCsv(outputDir, allTracks)

  // Copy audio and other files (priority first)
  const copiedFiles = new Set<string>()
  for (const srcDir of orderedSourceDirs) {
    for (const file of readdirSync(srcDir)) {
      const lower = file.toLowerCase()
      if (lower === SERIES_TOML || lower === TRACKS_CSV) continue
      if (copiedFiles.has(lower)) continue

      const srcPath = join(srcDir, file)
      if (statSync(srcPath).isFile()) {
        copiedFiles.add(lower)
        copyFileSync(srcPath, join(outputDir, file))
      }
    }
  }

  return allTracks.length
}

function mergeSeriesOrderToml(
  modPaths: string[],
  priorityModPath: string | null,
  outputDir: string,
  onLine: (line: LogLine) => void
): void {
  const orderedDirs = priorityModPath
    ? [...modPaths].sort((a, b) => {
        if (a === priorityModPath) return -1
        if (b === priorityModPath) return 1
        return 0
      })
    : modPaths

  const mergedOrder: string[] = []
  const seen = new Set<string>()

  for (const modDir of orderedDirs) {
    const orderFile = join(modDir, SERIES_ORDER_TOML)
    if (!existsSync(orderFile)) continue

    try {
      const raw = parseToml(readFileSync(orderFile, 'utf-8'))
      const order = raw['order']
      if (Array.isArray(order)) {
        for (const item of order) {
          const s = String(item)
          if (s && !seen.has(s.toLowerCase())) {
            seen.add(s.toLowerCase())
            mergedOrder.push(s)
          }
        }
      }
    } catch {
      onLine(log('warn', `Failed to parse ${orderFile}, skipping.`))
    }
  }

  if (mergedOrder.length === 0) return

  const lines: string[] = [
    '# Custom series display order',
    '# Listed series appear after official series, before "Other"',
    '# Unlisted custom series will be placed after these',
    'order = ['
  ]
  for (const id of mergedOrder) {
    lines.push(`    "${tomlEscape(id)}",`)
  }
  lines.push(']')
  lines.push('')

  writeFileSync(join(outputDir, SERIES_ORDER_TOML), lines.join('\n'))
  onLine(log('info', `Merged series-order.toml with ${mergedOrder.length} series.`))
}
