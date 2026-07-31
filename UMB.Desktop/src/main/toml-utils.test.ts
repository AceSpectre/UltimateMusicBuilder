import { describe, expect, it } from 'vitest'
import { mkdtempSync, rmSync, writeFileSync } from 'fs'
import { join } from 'path'
import { tmpdir } from 'os'
import { parseGamesBlocks, readTomlIdList, tableArrayBlocks, tableSection, tomlEscape, tomlString } from './toml-utils'

const SAMPLE = `# comment
[series]
id = "my_series"
name = "My Series"
series-playlist = "bgm_mine"

[[games]]
id = "game_one"
name = "Game One"

[[games]]
id = "game_two"

[default-track-data]
game = "game_one"
record-type = "arrange"
`

describe('tableSection', () => {
  it('returns only the named table body', () => {
    const series = tableSection(SAMPLE, 'series')
    expect(series).toContain('id = "my_series"')
    expect(series).not.toContain('game_one')
    expect(tableSection(SAMPLE, 'missing')).toBeNull()
  })
})

describe('tableArrayBlocks / parseGamesBlocks', () => {
  it('scopes each [[games]] block and defaults name to id', () => {
    expect(tableArrayBlocks(SAMPLE, 'games')).toHaveLength(2)
    expect(parseGamesBlocks(SAMPLE)).toEqual([
      { id: 'game_one', name: 'Game One' },
      { id: 'game_two', name: 'game_two' }
    ])
  })
})

describe('tomlString / tomlEscape', () => {
  it('reads quoted values and escapes specials', () => {
    const defaults = tableSection(SAMPLE, 'default-track-data')!
    expect(tomlString(defaults, 'record-type')).toBe('arrange')
    expect(tomlString(defaults, 'absent')).toBe('')
    expect(tomlEscape('say "hi" \\ bye')).toBe('say \\"hi\\" \\\\ bye')
    expect(tomlEscape(null as unknown as string)).toBe('')
  })
})

describe('readTomlIdList', () => {
  it('reads every quoted id, and [] when the file is missing', () => {
    const dir = mkdtempSync(join(tmpdir(), 'umb-toml-'))
    try {
      const path = join(dir, 'song_order.toml')
      writeFileSync(path, 'song_order = [\n  "ui_bgm_a", # mod\n  "ui_bgm_b"\n]\n', 'utf8')
      expect(readTomlIdList(path)).toEqual(['ui_bgm_a', 'ui_bgm_b'])
      expect(readTomlIdList(join(dir, 'missing.toml'))).toEqual([])
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })
})
