import { existsSync, mkdirSync, readFileSync, readdirSync, statSync, writeFileSync } from 'fs'
import { join, basename } from 'path'
import { resolveUnderMods } from './utils'
import { tomlEscape, tableSection, tomlString, parseGamesBlocks, readTomlIdList } from './toml-utils'
import type { CreateSeriesInput, SaveSeriesItem, SeriesFields, SeriesGame, SeriesOrderData, SeriesOrderItem } from '../shared/types'

export type { CreateSeriesInput, SaveSeriesItem, SeriesFields, SeriesGame, SeriesOrderData, SeriesOrderItem } from '../shared/types'

// Series icons are stored verbatim as icon.png; the app has no transcoder, so only PNG is accepted.
const PNG_DATA_URL_RE = /^data:image\/png;base64,([A-Za-z0-9+/=]+)$/

function writeSeriesIcon(seriesDir: string, dataUrl: string): void {
  const match = PNG_DATA_URL_RE.exec(dataUrl.trim())
  if (!match) throw new Error('Icon must be a PNG image.')
  writeFileSync(join(seriesDir, 'icon.png'), Buffer.from(match[1], 'base64'))
}

// Series id doubles as the folder name, so keep it filesystem-safe and consistent with
// the lowercase_snake convention used by game ids elsewhere.
const SERIES_ID_RE = /^[a-z0-9_]+$/

// Header-only tracks.csv: the series exists but has no songs yet (added later via Manage Songs).
const TRACKS_CSV_HEADER =
  'filename,game,title,author,copyright,record_type,special_category,volume,info1,in_soundtest\n'

interface ParsedSeriesToml {
  id: string | null
  name: string | null
  existingSeries: boolean
  fields: SeriesFields
}

function parseSeriesToml(seriesTomlPath: string): ParsedSeriesToml {
  const empty: SeriesFields = {
    name: '',
    seriesPlaylist: '',
    playlistIncidence: 100,
    games: [],
    defaultGame: '',
    defaultAuthor: '',
    defaultCopyright: '',
    defaultRecordType: 'original',
    defaultVolume: 1
  }
  if (!existsSync(seriesTomlPath)) {
    return { id: null, name: null, existingSeries: false, fields: empty }
  }

  const text = readFileSync(seriesTomlPath, 'utf8')
  const series = tableSection(text, 'series') ?? text
  const defaults = tableSection(text, 'default-track-data') ?? ''

  const id = series.match(/^\s*id\s*=\s*"([^"]+)"/m)?.[1] ?? null
  const name = series.match(/^\s*name\s*=\s*"([^"]+)"/m)?.[1] ?? null
  const incidence = series.match(/^\s*playlist-incidence\s*=\s*(\d+)/m)
  const volume = defaults.match(/^\s*volume\s*=\s*([0-9.]+)/m)

  return {
    id,
    name,
    existingSeries: series.match(/^\s*existing-series\s*=\s*(true|false)/m)?.[1] === 'true',
    fields: {
      name: name ?? id ?? '',
      seriesPlaylist: tomlString(series, 'series-playlist'),
      playlistIncidence: incidence ? Number.parseInt(incidence[1], 10) : 100,
      games: parseGamesBlocks(text),
      defaultGame: tomlString(defaults, 'game'),
      defaultAuthor: tomlString(defaults, 'author'),
      defaultCopyright: tomlString(defaults, 'copyright'),
      defaultRecordType: tomlString(defaults, 'record-type') || 'original',
      defaultVolume: volume ? Number.parseFloat(volume[1]) : 1
    }
  }
}

interface TomlEntry {
  key: string
  line: string | null // null → drop the key from the table
}

// Upserts the given keys in a TOML table, preserving unmanaged keys, comments and other tables.
// Missing table is created (at end of file) only when createIfMissing and there are lines to add.
function upsertTable(lines: string[], header: string, entries: TomlEntry[], createIfMissing: boolean): string[] {
  const headerIndex = lines.findIndex((line) => line.trim() === `[${header}]`)
  if (headerIndex < 0) {
    const additions = entries.map((e) => e.line).filter((line): line is string => line !== null)
    if (!createIfMissing || additions.length === 0) return lines
    const trimmed = [...lines]
    while (trimmed.length && trimmed[trimmed.length - 1].trim() === '') trimmed.pop()
    return [...trimmed, '', `[${header}]`, ...additions]
  }

  let endIndex = lines.length
  for (let i = headerIndex + 1; i < lines.length; i++) {
    if (/^\s*\[/.test(lines[i])) {
      endIndex = i
      break
    }
  }

  const byKey = new Map(entries.map((e) => [e.key, e.line]))
  const handled = new Set<string>()
  const body: string[] = []
  for (let i = headerIndex + 1; i < endIndex; i++) {
    const key = lines[i].match(/^\s*([A-Za-z0-9_-]+)\s*=/)?.[1]
    if (key && byKey.has(key)) {
      handled.add(key)
      const line = byKey.get(key)
      if (line) body.push(line) // null → drop
    } else {
      body.push(lines[i])
    }
  }
  for (const entry of entries) {
    if (!handled.has(entry.key) && entry.line) body.push(entry.line)
  }

  return [...lines.slice(0, headerIndex + 1), ...body, ...lines.slice(endIndex)]
}

// Replaces all [[games]] blocks with fresh ones from `games`, preserving everything else.
// Existing blocks are replaced in place; if none exist, the blocks go right after [series].
function rewriteGames(lines: string[], games: SeriesGame[]): string[] {
  const kept: string[] = []
  let insertIndex = -1
  let i = 0
  while (i < lines.length) {
    if (lines[i].trim() === '[[games]]') {
      if (insertIndex < 0) insertIndex = kept.length
      i++
      while (i < lines.length && !/^\s*\[/.test(lines[i])) i++
    } else {
      kept.push(lines[i])
      i++
    }
  }

  if (insertIndex < 0) {
    // No existing [[games]] — place them just after the [series] table.
    const seriesHeader = kept.findIndex((line) => line.trim() === '[series]')
    insertIndex = kept.length
    if (seriesHeader >= 0) {
      for (let j = seriesHeader + 1; j < kept.length; j++) {
        if (/^\s*\[/.test(kept[j])) {
          insertIndex = j
          break
        }
      }
    }
  }

  const block: string[] = []
  for (const game of games) {
    block.push('[[games]]', `id = "${tomlEscape(game.id)}"`, `name = "${tomlEscape(game.name)}"`, '')
  }
  if (block.length) block.pop() // drop trailing blank

  const before = kept.slice(0, insertIndex)
  const after = kept.slice(insertIndex)
  const result = [...before]
  if (block.length) {
    if (result.length && result[result.length - 1].trim() !== '') result.push('')
    result.push(...block)
    if (after.length && after[0].trim() !== '') result.push('')
  }
  result.push(...after)
  return result
}

// Writes editable [series] + [[games]] + [default-track-data] fields, preserving id, existing-series,
// comments, [[games]] and anything else. id and existing-series are never touched here.
function writeSeriesTomlFields(seriesTomlPath: string, fields: SeriesFields): void {
  if (!existsSync(seriesTomlPath)) return

  let lines = readFileSync(seriesTomlPath, 'utf8').split(/\r?\n/)

  lines = upsertTable(
    lines,
    'series',
    [
      { key: 'name', line: `name = "${tomlEscape(fields.name)}"` },
      { key: 'series-playlist', line: fields.seriesPlaylist ? `series-playlist = "${tomlEscape(fields.seriesPlaylist)}"` : null },
      { key: 'playlist-incidence', line: `playlist-incidence = ${Math.trunc(fields.playlistIncidence) || 0}` }
    ],
    false
  )

  lines = rewriteGames(lines, fields.games)

  const recordType = fields.defaultRecordType || 'original'
  const volume = Number.isInteger(fields.defaultVolume) ? fields.defaultVolume.toFixed(1) : String(fields.defaultVolume)
  // Only create [default-track-data] when it already exists or the user set a non-default value.
  const defaultsHaveContent =
    Boolean(fields.defaultGame || fields.defaultAuthor || fields.defaultCopyright) ||
    recordType !== 'original' ||
    fields.defaultVolume !== 1
  const sectionExists = lines.some((line) => line.trim() === '[default-track-data]')
  if (sectionExists || defaultsHaveContent) {
    lines = upsertTable(
      lines,
      'default-track-data',
      [
        { key: 'game', line: `game = "${tomlEscape(fields.defaultGame)}"` },
        { key: 'author', line: `author = "${tomlEscape(fields.defaultAuthor)}"` },
        { key: 'copyright', line: `copyright = "${tomlEscape(fields.defaultCopyright)}"` },
        { key: 'record-type', line: `record-type = "${tomlEscape(recordType)}"` },
        { key: 'volume', line: `volume = ${volume}` }
      ],
      true
    )
  }

  writeFileSync(seriesTomlPath, lines.join('\n'), 'utf8')
}

interface ScannedSeries {
  dirName: string
  id: string
  name: string
  iconDataUrl: string | null
  fields: SeriesFields
}

function scanCustomSeries(modPath: string): ScannedSeries[] {
  const results: ScannedSeries[] = []

  let entries: string[]
  try {
    entries = readdirSync(modPath)
  } catch {
    return results
  }

  for (const entry of entries) {
    if (entry.startsWith('.')) continue

    const seriesDir = join(modPath, entry)
    if (!statSync(seriesDir).isDirectory()) continue

    const seriesTomlPath = join(seriesDir, 'series.toml')
    if (!existsSync(seriesTomlPath)) continue

    const config = parseSeriesToml(seriesTomlPath)
    if (!config.id) continue
    if (config.existingSeries) continue
    if (config.id.toLowerCase() === 'etc') continue

    const iconPath = join(seriesDir, 'icon.png')

    let iconDataUrl: string | null = null
    if (existsSync(iconPath)) {
      const iconData = readFileSync(iconPath)
      iconDataUrl = `data:image/png;base64,${iconData.toString('base64')}`
    }

    results.push({
      dirName: entry,
      id: config.id,
      name: config.name ?? config.id,
      iconDataUrl,
      fields: config.fields
    })
  }

  return results
}

export function loadSeriesOrderData(workspace: string, modPath: string): SeriesOrderData {
  const resolvedModPath = resolveUnderMods(workspace, modPath)

  const customSeries = scanCustomSeries(resolvedModPath)
  const orderPath = join(resolvedModPath, 'series-order.toml')
  const existingOrder = readTomlIdList(orderPath)

  const orderedIds = existingOrder
  const sorted = [...customSeries].sort((a, b) => {
    const aIdx = orderedIds.indexOf(a.id)
    const bIdx = orderedIds.indexOf(b.id)
    const aOrder = aIdx >= 0 ? aIdx : Number.MAX_SAFE_INTEGER
    const bOrder = bIdx >= 0 ? bIdx : Number.MAX_SAFE_INTEGER
    if (aOrder !== bOrder) return aOrder - bOrder
    return a.name.localeCompare(b.name)
  })

  const items: SeriesOrderItem[] = sorted.map((s, index) => ({
    id: `series:${index}`,
    name: s.name,
    seriesId: s.id,
    iconDataUrl: s.iconDataUrl,
    originalIndex: index,
    fields: s.fields
  }))

  return {
    modName: basename(resolvedModPath),
    modPath: resolvedModPath,
    hasSeriesOrder: existingOrder.length > 0,
    items
  }
}

// Creates a new custom series folder (series.toml + header-only tracks.csv) under the mod and
// returns the reloaded series list. The first game becomes the [default-track-data] game.
export function createSeries(workspace: string, modPath: string, input: CreateSeriesInput): SeriesOrderData {
  const resolvedModPath = resolveUnderMods(workspace, modPath)

  const seriesId = input.seriesId.trim()
  if (!SERIES_ID_RE.test(seriesId)) {
    throw new Error('Series ID must contain only lowercase letters, numbers, and underscores.')
  }
  if (seriesId === 'etc') {
    throw new Error('"etc" is a reserved series ID.')
  }

  const name = input.name.trim()
  if (!name) {
    throw new Error('Series name is required.')
  }

  const games = input.games.map((g) => ({ id: g.id.trim(), name: g.name.trim() })).filter((g) => g.id)
  if (games.length === 0) {
    throw new Error('At least one game is required.')
  }

  const seriesDir = join(resolvedModPath, seriesId)
  const existingIds = new Set(scanCustomSeries(resolvedModPath).map((s) => s.id))
  if (existsSync(seriesDir) || existingIds.has(seriesId)) {
    throw new Error('A series with that ID already exists.')
  }

  mkdirSync(seriesDir, { recursive: true })

  const playlist = input.seriesPlaylist.trim()
  const lines: string[] = ['[series]', `id = "${tomlEscape(seriesId)}"`, `name = "${tomlEscape(name)}"`, 'playlist-incidence = 100']
  if (playlist) lines.push(`series-playlist = "${tomlEscape(playlist)}"`)
  lines.push('')
  for (const g of games) {
    lines.push('[[games]]', `id = "${tomlEscape(g.id)}"`, `name = "${tomlEscape(g.name)}"`, '')
  }
  lines.push('[default-track-data]', `game = "${tomlEscape(games[0].id)}"`, 'author = ""', 'copyright = ""', 'record-type = "original"', 'volume = 1.0', '')

  writeFileSync(join(seriesDir, 'series.toml'), lines.join('\n'), 'utf8')
  writeFileSync(join(seriesDir, 'tracks.csv'), TRACKS_CSV_HEADER, 'utf8')
  if (input.iconDataUrl) writeSeriesIcon(seriesDir, input.iconDataUrl)

  return loadSeriesOrderData(workspace, resolvedModPath)
}

// Writes (or replaces) icon.png for an existing custom series and returns the new data URL.
export function setSeriesIcon(workspace: string, modPath: string, seriesId: string, iconDataUrl: string): string {
  const resolvedModPath = resolveUnderMods(workspace, modPath)

  const dirName = scanCustomSeries(resolvedModPath).find((s) => s.id === seriesId)?.dirName
  if (!dirName) {
    throw new Error('Series not found.')
  }

  const seriesDir = join(resolvedModPath, dirName)
  writeSeriesIcon(seriesDir, iconDataUrl)
  const bytes = readFileSync(join(seriesDir, 'icon.png'))
  return `data:image/png;base64,${bytes.toString('base64')}`
}

export function saveSeriesOrderData(workspace: string, modPath: string, items: SaveSeriesItem[]): SeriesOrderData {
  const resolvedModPath = resolveUnderMods(workspace, modPath)

  const data = loadSeriesOrderData(workspace, resolvedModPath)
  const itemById = new Map(data.items.map((item) => [item.id, item]))
  const fieldsById = new Map(items.map((item) => [item.id, item.fields]))
  const orderedIds = items.map((item) => item.id)

  const orderedItems = orderedIds
    .map((id) => itemById.get(id))
    .filter((item): item is SeriesOrderItem => Boolean(item))

  const missingItems = data.items.filter((item) => !orderedIds.includes(item.id))
  const finalItems = [...orderedItems, ...missingItems]

  // Write each series' edited [series] fields back to its series.toml (located via dir scan).
  const dirByseriesId = new Map(scanCustomSeries(resolvedModPath).map((s) => [s.id, s.dirName]))
  for (const item of finalItems) {
    const fields = fieldsById.get(item.id)
    const dirName = dirByseriesId.get(item.seriesId)
    if (fields && dirName) {
      writeSeriesTomlFields(join(resolvedModPath, dirName, 'series.toml'), fields)
    }
  }

  const orderPath = join(resolvedModPath, 'series-order.toml')
  const lines = [
    '# Custom series display order',
    '# Listed series appear after official series, before "Other"',
    '# Unlisted custom series will be placed after these',
    'order = ['
  ]

  for (const item of finalItems) {
    lines.push(`    "${item.seriesId}",`)
  }

  lines.push(']')
  writeFileSync(orderPath, lines.join('\n') + '\n', 'utf8')

  return loadSeriesOrderData(workspace, resolvedModPath)
}
