import { existsSync, readFileSync, readdirSync, statSync, writeFileSync } from 'fs'
import { join, relative, resolve, isAbsolute, basename } from 'path'

export interface SeriesOrderItem {
  id: string
  name: string
  seriesId: string
  iconDataUrl: string | null
  originalIndex: number
}

export interface SeriesOrderData {
  modName: string
  modPath: string
  hasSeriesOrder: boolean
  items: SeriesOrderItem[]
}

function isChildOf(parentPath: string, childPath: string): boolean {
  const rel = relative(parentPath, childPath)
  return rel !== '' && !rel.startsWith('..') && !isAbsolute(rel)
}

function getMusicModsRoot(workspace: string): string {
  return resolve(workspace, 'Mods', 'MusicMods')
}

interface ParsedSeriesToml {
  id: string | null
  name: string | null
  existingSeries: boolean
}

function parseSeriesToml(seriesTomlPath: string): ParsedSeriesToml {
  if (!existsSync(seriesTomlPath)) {
    return { id: null, name: null, existingSeries: false }
  }

  const text = readFileSync(seriesTomlPath, 'utf8')
  const idMatch = text.match(/^id\s*=\s*"([^"]+)"/m)
  const nameMatch = text.match(/^name\s*=\s*"([^"]+)"/m)
  const existingMatch = text.match(/^existing-series\s*=\s*(true|false)/m)

  return {
    id: idMatch?.[1] ?? null,
    name: nameMatch?.[1] ?? null,
    existingSeries: existingMatch?.[1] === 'true'
  }
}

function loadSeriesOrder(orderPath: string): string[] {
  if (!existsSync(orderPath)) {
    return []
  }

  const text = readFileSync(orderPath, 'utf8')
  return Array.from(text.matchAll(/"([^"]+)"/g), (match) => match[1].trim()).filter(Boolean)
}

function scanCustomSeries(modPath: string): Array<{ dirName: string; id: string; name: string; iconDataUrl: string | null }> {
  const results: Array<{ dirName: string; id: string; name: string; iconDataUrl: string | null }> = []

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
      iconDataUrl
    })
  }

  return results
}

export function loadSeriesOrderData(workspace: string, modPath: string): SeriesOrderData {
  const modsDir = getMusicModsRoot(workspace)
  const resolvedModPath = resolve(modPath)
  if (!isChildOf(modsDir, resolvedModPath)) {
    throw new Error('Invalid mod path.')
  }

  const customSeries = scanCustomSeries(resolvedModPath)
  const orderPath = join(resolvedModPath, 'series-order.toml')
  const existingOrder = loadSeriesOrder(orderPath)

  const orderedIds = existingOrder.length > 0 ? existingOrder : []
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
    originalIndex: index
  }))

  return {
    modName: basename(resolvedModPath),
    modPath: resolvedModPath,
    hasSeriesOrder: existingOrder.length > 0,
    items
  }
}

export function saveSeriesOrderData(workspace: string, modPath: string, orderedIds: string[]): SeriesOrderData {
  const modsDir = getMusicModsRoot(workspace)
  const resolvedModPath = resolve(modPath)
  if (!isChildOf(modsDir, resolvedModPath)) {
    throw new Error('Invalid mod path.')
  }

  const data = loadSeriesOrderData(workspace, resolvedModPath)
  const itemById = new Map(data.items.map((item) => [item.id, item]))

  const orderedItems = orderedIds
    .map((id) => itemById.get(id))
    .filter((item): item is SeriesOrderItem => Boolean(item))

  const missingItems = data.items.filter((item) => !orderedIds.includes(item.id))
  const finalItems = [...orderedItems, ...missingItems]

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
