import { existsSync, readFileSync, writeFileSync } from 'fs'
import { join, basename } from 'path'
import { getVanillaBgmTitles, getVanillaGameTitles, getVanillaSongs } from './playlist-info'
import { resolveUnderMods } from './utils'
import { tomlEscape, tableSection, tomlString, parseGamesBlocks, readTomlIdList } from './toml-utils'
import { readCsvWithHeaders, writeCsvRows, type CsvRow } from './csv-utils'
import type {
  DefaultTrackData, SaveTrackItem, SeriesGame, TrackFields, TrackOrderData, TrackOrderItem, VanillaSongOption
} from '../shared/types'

export type {
  DefaultTrackData, SaveTrackItem, SeriesGame, TrackFields, TrackOrderData, TrackOrderItem, VanillaSongOption
} from '../shared/types'

const SERIES_ID_PREFIX = 'ui_series_'

const EDITABLE_COLUMNS: (keyof TrackFields)[] = [
  'title',
  'game',
  'author',
  'copyright',
  'record_type',
  'special_category',
  'info1',
  'in_soundtest'
]

function getSeriesFiles(seriesPath: string) {
  return {
    csvPath: join(seriesPath, 'tracks.csv'),
    seriesTomlPath: join(seriesPath, 'series.toml'),
    songOrderPath: join(seriesPath, 'song_order.toml')
  }
}

function deriveToneId(filename: string): string {
  const nameOnly = basename(filename).replace(/\.[^.]+$/, '').toLowerCase()
  const normalized = Array.from(nameOnly, (char) => /[a-z0-9_]/.test(char) ? char : '_').join('')
  return normalized.replace(/^_+|_+$/g, '').slice(0, 128)
}

function formatVanillaTitle(bgmId: string): string {
  return bgmId
    .replace(/^ui_bgm_/, '')
    .split('_')
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ')
}

function parseSeriesToml(seriesTomlPath: string): {
  id: string | null
  existingSeries: boolean
  games: SeriesGame[]
  defaultTrackData: DefaultTrackData | null
} {
  if (!existsSync(seriesTomlPath)) {
    return { id: null, existingSeries: false, games: [], defaultTrackData: null }
  }

  const text = readFileSync(seriesTomlPath, 'utf8')
  const idMatch = text.match(/^id\s*=\s*"([^"]+)"/m)
  const existingMatch = text.match(/^existing-series\s*=\s*(true|false)/m)

  return {
    id: idMatch?.[1] ?? null,
    existingSeries: existingMatch?.[1] === 'true',
    games: parseGamesBlocks(text),
    defaultTrackData: parseDefaultTrackData(text)
  }
}

// Reads the [default-track-data] table (note `record-type` uses a dash in the TOML).
function parseDefaultTrackData(seriesTomlText: string): DefaultTrackData | null {
  const scoped = tableSection(seriesTomlText, 'default-track-data')
  if (scoped === null) {
    return null
  }

  return {
    game: tomlString(scoped, 'game'),
    author: tomlString(scoped, 'author'),
    copyright: tomlString(scoped, 'copyright'),
    record_type: tomlString(scoped, 'record-type') || 'original'
  }
}

// Appends [[games]] blocks for any game ids used by rows but not yet declared in series.toml.
// Only games present in `knownGames` (custom + vanilla catalog) are added.
// Unknown ids are left alone (the build skips them). Quotes are escaped for TOML safety.
function ensureSeriesGames(seriesTomlPath: string, rows: CsvRow[], knownGames: SeriesGame[]): void {
  if (!existsSync(seriesTomlPath)) {
    return
  }

  const text = readFileSync(seriesTomlPath, 'utf8')
  const declared = new Set(parseGamesBlocks(text).map((game) => game.id))
  const nameById = new Map(knownGames.map((game) => [game.id, game.name]))

  const used = new Set(rows.map((row) => row.game?.trim()).filter((id): id is string => Boolean(id)))
  const toAdd = [...used].filter((id) => !declared.has(id) && nameById.has(id))
  if (toAdd.length === 0) {
    return
  }

  const blocks = toAdd
    .map((id) => `\n[[games]]\nid = "${tomlEscape(id)}"\nname = "${tomlEscape(nameById.get(id) ?? id)}"\n`)
    .join('')

  writeFileSync(seriesTomlPath, `${text.replace(/\s*$/, '')}\n${blocks}`, 'utf8')
}

function buildFields(row: CsvRow): TrackFields {
  return {
    title: row.title ?? '',
    game: row.game ?? '',
    author: row.author ?? '',
    copyright: row.copyright ?? '',
    record_type: row.record_type || 'original',
    special_category: row.special_category ?? '',
    info1: row.info1 ?? '',
    in_soundtest: row.in_soundtest || 'True'
  }
}

function buildModItems(rows: CsvRow[]): TrackOrderItem[] {
  // A track is a "pinch target" when another row's info1 references its filename.
  const referencedFilenames = new Set(
    rows
      .map((row) => row.info1?.trim())
      .filter((info1): info1 is string => Boolean(info1) && !info1.startsWith('info_'))
  )

  return rows.map((row, index) => {
    const filename = row.filename ?? ''
    const title = row.title || filename || `Track ${index + 1}`
    const game = row.game || ''
    const subtitle = game ? `${game} - ${filename}` : filename
    const bgmId = filename ? `ui_bgm_${deriveToneId(filename)}` : `ui_bgm_track_${index}`

    return {
      id: `mod:${index}`,
      title,
      subtitle,
      bgmId,
      filename,
      isLocked: false,
      originalIndex: index,
      fields: buildFields(row),
      isPinchTarget: filename ? referencedFilenames.has(filename) : false
    }
  })
}

function parseOrder(row: CsvRow): number | null {
  const value = row.order
  if (!value) {
    return null
  }

  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) ? parsed : null
}

function buildOrderedItems(
  modItems: TrackOrderItem[],
  rows: CsvRow[],
  songOrder: string[],
  resolveVanillaTitle: (bgmId: string) => string
): TrackOrderItem[] {
  if (songOrder.length > 0) {
    const modByBgmId = new Map(modItems.map((item) => [item.bgmId, item]))
    const seen = new Set<string>()
    const result: TrackOrderItem[] = []
    let vanillaIndex = 0

    for (const bgmId of songOrder) {
      const modItem = modByBgmId.get(bgmId)
      if (modItem) {
        seen.add(modItem.id)
        result.push(modItem)
      } else {
        result.push({
          id: `vanilla:${vanillaIndex}:${bgmId}`,
          title: resolveVanillaTitle(bgmId),
          subtitle: '[vanilla] preserved from song_order.toml',
          bgmId,
          filename: '',
          isLocked: true,
          originalIndex: null,
          fields: null,
          isPinchTarget: false
        })
        vanillaIndex += 1
      }
    }

    for (const item of modItems) {
      if (!seen.has(item.id)) {
        result.push(item)
      }
    }

    return result
  }

  const orderedMods = modItems
    .map((item) => ({ item, order: parseOrder(rows[item.originalIndex ?? 0]) }))
    .sort((left, right) => {
      const leftOrder = left.order ?? Number.MAX_SAFE_INTEGER
      const rightOrder = right.order ?? Number.MAX_SAFE_INTEGER
      return leftOrder - rightOrder
    })
    .map((entry) => entry.item)

  return orderedMods
}

interface VanillaCatalog {
  resolveTitle: (bgmId: string) => string
  games: SeriesGame[] // vanilla games belonging to this series
  songs: VanillaSongOption[] // vanilla songs belonging to this series
}

// Loads the vanilla game catalog for a series, falling back to empty data / prettified
// titles when the game resource dump is missing (e.g. CI / unconfigured workspace).
function loadVanillaCatalog(workspace: string, uiSeriesId: string | null): VanillaCatalog {
  let titles: Map<string, string> | null = null
  try {
    titles = getVanillaBgmTitles(workspace)
  } catch {
    titles = null
  }

  let games: SeriesGame[] = []
  let songs: VanillaSongOption[] = []
  if (uiSeriesId) {
    try {
      games = getVanillaGameTitles(workspace)
        .filter((game) => game.seriesId === uiSeriesId)
        .map((game) => ({ id: game.id, name: game.name }))
    } catch {
      games = []
    }
    try {
      songs = getVanillaSongs(workspace)
        .filter((song) => song.seriesId === uiSeriesId)
        .map((song) => ({ infoId: song.infoId, name: song.name }))
    } catch {
      songs = []
    }
  }

  return {
    resolveTitle: (bgmId) => titles?.get(bgmId) ?? formatVanillaTitle(bgmId),
    games,
    songs
  }
}

// Custom (series.toml) games first, then vanilla series games not already declared.
function mergeGames(custom: SeriesGame[], vanilla: SeriesGame[]): SeriesGame[] {
  const seen = new Set(custom.map((game) => game.id))
  return [...custom, ...vanilla.filter((game) => !seen.has(game.id))]
}

function loadTrackOrderDataUnsafe(
  workspace: string,
  seriesPath: string
): { data: TrackOrderData; rows: CsvRow[]; headers: string[] } {
  const { csvPath, seriesTomlPath, songOrderPath } = getSeriesFiles(seriesPath)
  const { rows, headers } = readCsvWithHeaders(csvPath)
  const seriesInfo = parseSeriesToml(seriesTomlPath)
  const songOrder = readTomlIdList(songOrderPath)
  const uiSeriesId = seriesInfo.id ? SERIES_ID_PREFIX + seriesInfo.id : null
  const vanilla = loadVanillaCatalog(workspace, uiSeriesId)
  const modItems = buildModItems(rows)
  const items = buildOrderedItems(modItems, rows, songOrder, vanilla.resolveTitle)

  return {
    data: {
      seriesName: basename(seriesPath),
      seriesPath,
      isExistingSeries: seriesInfo.existingSeries,
      hasSongOrder: songOrder.length > 0,
      games: mergeGames(seriesInfo.games, vanilla.games),
      vanillaSongs: vanilla.songs,
      defaultTrackData: seriesInfo.defaultTrackData,
      items
    },
    rows,
    headers
  }
}

export function loadTrackOrderData(workspace: string, seriesPath: string): TrackOrderData {
  const resolvedSeriesPath = resolveUnderMods(workspace, seriesPath, 'Invalid series path.')
  return loadTrackOrderDataUnsafe(workspace, resolvedSeriesPath).data
}

export function saveTrackOrderData(workspace: string, seriesPath: string, items: SaveTrackItem[]): TrackOrderData {
  const resolvedSeriesPath = resolveUnderMods(workspace, seriesPath, 'Invalid series path.')

  const { data, rows, headers } = loadTrackOrderDataUnsafe(workspace, resolvedSeriesPath)
  const { csvPath, seriesTomlPath, songOrderPath } = getSeriesFiles(resolvedSeriesPath)

  const orderedIds = items.map((item) => item.id)
  const fieldsById = new Map(items.map((item) => [item.id, item.fields]))
  const itemById = new Map(data.items.map((item) => [item.id, item]))
  const orderedItems = orderedIds.map((id) => itemById.get(id)).filter((item): item is TrackOrderItem => Boolean(item))
  const missingItems = data.items.filter((item) => !orderedIds.includes(item.id))
  const finalItems = [...orderedItems, ...missingItems]

  // Write edited field values back into the matching CSV rows (filename/volume untouched).
  for (const item of finalItems) {
    if (item.originalIndex === null) {
      continue
    }
    const fields = fieldsById.get(item.id)
    if (!fields) {
      continue
    }
    const row = rows[item.originalIndex]
    for (const column of EDITABLE_COLUMNS) {
      row[column] = fields[column] ?? ''
    }
  }

  // Ensure every edited column is present in the header before writing.
  let nextHeaders = headers
  for (const column of EDITABLE_COLUMNS) {
    if (!nextHeaders.includes(column)) {
      nextHeaders = [...nextHeaders, column]
    }
  }
  nextHeaders = nextHeaders.includes('order') ? nextHeaders : [...nextHeaders, 'order']
  for (const row of rows) {
    row.order = ''
  }

  const reorderedRows: CsvRow[] = []
  for (const [index, item] of finalItems.entries()) {
    if (item.originalIndex === null) {
      continue
    }

    const row = rows[item.originalIndex]
    row.order = String(index)
    reorderedRows.push(row)
  }

  for (const row of rows) {
    if (!reorderedRows.includes(row)) {
      reorderedRows.push(row)
    }
  }

  writeCsvRows(csvPath, reorderedRows, nextHeaders)

  // A row may now reference a vanilla game title that isn't yet declared in series.toml.
  // The build only accepts games listed under [[games]], so add any missing ones (with the
  // localised name from the merged catalog) — keeps newly-assigned vanilla games building.
  ensureSeriesGames(seriesTomlPath, rows, data.games)

  if (data.isExistingSeries) {
    const output = [
      '# Generated by UltimateMusicBuilder Desktop — ordering for an existing-series mod.',
      '# Listed in the order they will appear in the in-game Sound Test / My Music view.',
      'song_order = [',
      ...finalItems
        .filter((item) => item.bgmId)
        .map((item, index, list) => `  "${item.bgmId}"${index < list.length - 1 ? ',' : ''} # ${item.isLocked ? 'vanilla' : 'mod'}`),
      ']'
    ].join('\n')

    writeFileSync(songOrderPath, `${output}\n`, 'utf8')
  }

  return loadTrackOrderData(workspace, resolvedSeriesPath)
}
