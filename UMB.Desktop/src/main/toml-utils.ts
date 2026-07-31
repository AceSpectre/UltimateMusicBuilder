// Lightweight regex-based helpers for the hand-edited series.toml / *-order.toml files.
// These deliberately preserve unknown keys and comments (a real TOML round-trip would not),
// mirroring how the C# build reads the same files.

import { existsSync, readFileSync } from 'fs'
import type { SeriesGame } from '../shared/types'

export const tomlEscape = (value: string): string =>
  (value ?? '').replace(/\\/g, '\\\\').replace(/"/g, '\\"')

/**
 * Returns the body of a [header] table (up to the next table header) so field matches
 * don't bleed across tables ([[games]] / [default-track-data] reuse keys like `name`).
 */
export function tableSection(text: string, header: string): string | null {
  const after = text.split(new RegExp(`^\\s*\\[${header}\\]\\s*$`, 'm'))[1]
  if (after === undefined) return null
  return after.split(/^\s*\[/m)[0]
}

/** Returns the scoped body of each [[name]] array-of-tables block. */
export function tableArrayBlocks(text: string, name: string): string[] {
  return text
    .split(new RegExp(`^\\s*\\[\\[${name}\\]\\]\\s*$`, 'm'))
    .slice(1)
    .map((block) => block.split(/^\s*\[/m)[0])
}

/** Reads `key = "value"` from a table body ('' when absent). */
export function tomlString(section: string, key: string): string {
  return section.match(new RegExp(`^\\s*${key}\\s*=\\s*"([^"]*)"`, 'm'))?.[1] ?? ''
}

/** Parses the repeating [[games]] array-of-tables (id + name per block, name defaults to id). */
export function parseGamesBlocks(text: string): SeriesGame[] {
  const games: SeriesGame[] = []
  for (const scoped of tableArrayBlocks(text, 'games')) {
    const id = scoped.match(/^\s*id\s*=\s*"([^"]+)"/m)?.[1]
    if (!id) continue
    games.push({ id, name: scoped.match(/^\s*name\s*=\s*"([^"]*)"/m)?.[1] ?? id })
  }
  return games
}

/** Reads every quoted string from an id-list file (song_order.toml / series-order.toml). */
export function readTomlIdList(path: string): string[] {
  if (!existsSync(path)) {
    return []
  }
  const text = readFileSync(path, 'utf8')
  return Array.from(text.matchAll(/"([^"]+)"/g), (match) => match[1].trim()).filter(Boolean)
}
