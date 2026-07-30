import { describe, expect, it } from 'vitest'
import { existsSync } from 'fs'
import { join, resolve } from 'path'
import { getVanillaBgmTitles, getVanillaGameTitles, getVanillaSongs } from './playlist-info'

// The repo root holds the Resources/Game dump. These tests need it present and auto-skip
// otherwise (e.g. CI), mirroring the e2e hasGameResources gate.
const REPO_ROOT = resolve(__dirname, '..', '..', '..')
const HAS_RESOURCES = existsSync(join(REPO_ROOT, 'Resources', 'Game', 'ui', 'param', 'database', 'ui_bgm_db.prc'))

describe.runIf(HAS_RESOURCES)('vanilla catalog (needs Resources/Game)', () => {
  it('resolves localised vanilla bgm titles', () => {
    const titles = getVanillaBgmTitles(REPO_ROOT)
    expect(titles.size).toBeGreaterThan(100)
    for (const [, name] of titles) {
      expect(name.length).toBeGreaterThan(0)
    }
  })

  it('lists vanilla game titles with bare ids, names and series', () => {
    const games = getVanillaGameTitles(REPO_ROOT)
    expect(games.length).toBeGreaterThan(50)
    for (const game of games) {
      expect(game.id).not.toMatch(/^ui_gametitle_/)
      expect(game.seriesId === '' || game.seriesId.startsWith('ui_series_')).toBe(true)
    }
    expect(games.some((g) => g.seriesId === 'ui_series_mariokart')).toBe(true)
  })

  it('lists vanilla songs with info_* ids grouped by series', () => {
    const songs = getVanillaSongs(REPO_ROOT)
    expect(songs.length).toBeGreaterThan(100)
    for (const song of songs) {
      expect(song.infoId.startsWith('info_')).toBe(true)
      expect(song.bgmId.startsWith('ui_bgm_')).toBe(true)
    }
    expect(songs.some((s) => s.seriesId === 'ui_series_mariokart')).toBe(true)
  })
})
