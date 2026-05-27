import { existsSync, readFileSync, writeFileSync } from 'fs'
import { join, relative, resolve, isAbsolute, basename } from 'path'
import { parse as parseCsv } from 'csv-parse/sync'
import { stringify as stringifyCsv } from 'csv-stringify/sync'

interface CsvRow {
  [key: string]: string
}

export interface TrackOrderItem {
  id: string
  title: string
  subtitle: string
  bgmId: string
  isLocked: boolean
  originalIndex: number | null
}

export interface TrackOrderData {
  seriesName: string
  seriesPath: string
  isExistingSeries: boolean
  hasSongOrder: boolean
  items: TrackOrderItem[]
}

function isChildOf(parentPath: string, childPath: string): boolean {
  const rel = relative(parentPath, childPath)
  return rel !== '' && !rel.startsWith('..') && !isAbsolute(rel)
}

function getMusicModsRoot(workspace: string): string {
  return resolve(workspace, 'Mods', 'MusicMods')
}

function getSeriesFiles(seriesPath: string) {
  return {
    csvPath: join(seriesPath, 'tracks.csv'),
    seriesTomlPath: join(seriesPath, 'series.toml'),
    songOrderPath: join(seriesPath, 'song_order.toml')
  }
}

function readCsvRows(csvPath: string): { rows: CsvRow[]; headers: string[] } {
  const source = readFileSync(csvPath, 'utf8')
  const rows = parseCsv(source, {
    columns: true,
    skip_empty_lines: true,
    relax_column_count: true,
    bom: true,
    trim: false
  }) as CsvRow[]

  const headers = parseCsv(source, {
    to_line: 1,
    relax_column_count: true,
    bom: true
  })[0] as string[]

  return { rows, headers }
}

function writeCsvRows(csvPath: string, rows: CsvRow[], headers: string[]): void {
  const output = stringifyCsv(rows, {
    header: true,
    columns: headers,
    quoted_match: /[\n\r,]/
  })

  writeFileSync(csvPath, output, 'utf8')
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

function parseSeriesToml(seriesTomlPath: string): { id: string | null; existingSeries: boolean } {
  if (!existsSync(seriesTomlPath)) {
    return { id: null, existingSeries: false }
  }

  const text = readFileSync(seriesTomlPath, 'utf8')
  const idMatch = text.match(/^id\s*=\s*"([^"]+)"/m)
  const existingMatch = text.match(/^existing-series\s*=\s*(true|false)/m)

  return {
    id: idMatch?.[1] ?? null,
    existingSeries: existingMatch?.[1] === 'true'
  }
}

function parseSongOrder(songOrderPath: string): string[] {
  if (!existsSync(songOrderPath)) {
    return []
  }

  const text = readFileSync(songOrderPath, 'utf8')
  return Array.from(text.matchAll(/"([^"]+)"/g), (match) => match[1].trim()).filter(Boolean)
}

function buildModItems(rows: CsvRow[]): TrackOrderItem[] {
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
      isLocked: false,
      originalIndex: index
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

function buildOrderedItems(modItems: TrackOrderItem[], rows: CsvRow[], songOrder: string[]): TrackOrderItem[] {
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
          title: formatVanillaTitle(bgmId),
          subtitle: '[vanilla] preserved from song_order.toml',
          bgmId,
          isLocked: true,
          originalIndex: null
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

function loadTrackOrderDataUnsafe(seriesPath: string): { data: TrackOrderData; rows: CsvRow[]; headers: string[] } {
  const { csvPath, seriesTomlPath, songOrderPath } = getSeriesFiles(seriesPath)
  const { rows, headers } = readCsvRows(csvPath)
  const seriesInfo = parseSeriesToml(seriesTomlPath)
  const songOrder = parseSongOrder(songOrderPath)
  const modItems = buildModItems(rows)
  const items = buildOrderedItems(modItems, rows, songOrder)

  return {
    data: {
      seriesName: basename(seriesPath),
      seriesPath,
      isExistingSeries: seriesInfo.existingSeries,
      hasSongOrder: songOrder.length > 0,
      items
    },
    rows,
    headers
  }
}

export function loadTrackOrderData(workspace: string, seriesPath: string): TrackOrderData {
  const modsDir = getMusicModsRoot(workspace)
  const resolvedSeriesPath = resolve(seriesPath)
  if (!isChildOf(modsDir, resolvedSeriesPath)) {
    throw new Error('Invalid series path.')
  }

  return loadTrackOrderDataUnsafe(resolvedSeriesPath).data
}

export function saveTrackOrderData(workspace: string, seriesPath: string, orderedIds: string[]): TrackOrderData {
  const modsDir = getMusicModsRoot(workspace)
  const resolvedSeriesPath = resolve(seriesPath)
  if (!isChildOf(modsDir, resolvedSeriesPath)) {
    throw new Error('Invalid series path.')
  }

  const { data, rows, headers } = loadTrackOrderDataUnsafe(resolvedSeriesPath)
  const { csvPath, songOrderPath } = getSeriesFiles(resolvedSeriesPath)

  const itemById = new Map(data.items.map((item) => [item.id, item]))
  const orderedItems = orderedIds.map((id) => itemById.get(id)).filter((item): item is TrackOrderItem => Boolean(item))
  const missingItems = data.items.filter((item) => !orderedIds.includes(item.id))
  const finalItems = [...orderedItems, ...missingItems]

  const nextHeaders = headers.includes('order') ? headers : [...headers, 'order']
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