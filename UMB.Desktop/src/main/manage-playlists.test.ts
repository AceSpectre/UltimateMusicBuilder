import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { readFileSync } from 'fs'
import { join } from 'path'
import { loadManagePlaylists, saveManagePlaylists } from './manage-playlists'
import { makeWorkspace, makeDir, writeSeries, type Workspace } from './test-utils'

let ws: Workspace

beforeEach(() => {
  ws = makeWorkspace()
})

afterEach(() => {
  ws.cleanup()
})

const MARIO_CSV = 'filename,game,title\na.flac,g,Song A\nb.flac,g,Song B\n'
const ZELDA_CSV = 'filename,game,title\nc.flac,g,Song C\n'

const MARIO_TOML =
  '[series]\nid = "mario"\nname = "Mario"\nexisting-series = true\nseries-playlist = "bgmmario"\n\n' +
  '[[games]]\nid = "g"\nname = "G"\n\n' +
  '[[playlists]]\nid = "bgmsmashmenu"\nincidence = 10\nsongs = ["a"]\n\n' +
  '[default-track-data]\ngame = "g"\n'

const ZELDA_TOML = '[series]\nid = "zelda"\nname = "Zelda"\n\n[[games]]\nid = "gz"\nname = "GZ"\n'

/** Lays down a two-series mod (mario + zelda) and returns the mod path. */
function seedMod(mario = MARIO_TOML): string {
  const modPath = makeDir(ws, 'mymod')
  writeSeries(ws, 'mymod', 'mario', { tracksCsv: MARIO_CSV, seriesToml: mario })
  writeSeries(ws, 'mymod', 'zelda', { tracksCsv: ZELDA_CSV, seriesToml: ZELDA_TOML })
  return modPath
}

describe('loadManagePlaylists', () => {
  it('lists existing game playlists with friendly names', () => {
    const modPath = seedMod()
    const data = loadManagePlaylists(ws.root, modPath)
    const mario = data.playlists.find((p) => p.id === 'bgmmario')
    expect(mario?.name).toBe('Mario')
    expect(data.playlists.find((p) => p.id === 'bgmsmashmenu')?.name).toBe('Smash Menu')
  })

  it('lists every mod song as an assignable pool entry', () => {
    const modPath = seedMod()
    const data = loadManagePlaylists(ws.root, modPath)
    expect(data.songs.map((s) => s.filename).sort()).toEqual(['a.flac', 'b.flac', 'c.flac'])
    expect(data.songs.find((s) => s.filename === 'a.flac')).toMatchObject({
      seriesId: 'mario',
      seriesName: 'Mario',
      title: 'Song A'
    })
  })

  it('parses a [[playlists]] block into a per-song assignment with incidence', () => {
    const modPath = seedMod()
    const data = loadManagePlaylists(ws.root, modPath)
    const menu = data.playlists.find((p) => p.id === 'bgmsmashmenu')!
    expect(menu.assignedCount).toBe(1)
    expect(menu.assignments).toEqual([
      { seriesId: 'mario', seriesName: 'Mario', filename: 'a.flac', title: 'Song A', incidence: 10 }
    ])
  })

  it('treats songs = "*" as every song in that series', () => {
    const wildcard = MARIO_TOML.replace('songs = ["a"]', 'songs = "*"')
    const modPath = seedMod(wildcard)
    const data = loadManagePlaylists(ws.root, modPath)
    const menu = data.playlists.find((p) => p.id === 'bgmsmashmenu')!
    expect(menu.assignments.map((a) => a.filename).sort()).toEqual(['a.flac', 'b.flac'])
  })
})

describe('saveManagePlaylists', () => {
  it('writes assignments and round-trips through a reload', () => {
    const modPath = seedMod()
    const result = saveManagePlaylists(ws.root, modPath, [
      { playlistId: 'bgmsmashmenu', seriesId: 'mario', filename: 'a.flac', incidence: 10 },
      { playlistId: 'bgmzelda', seriesId: 'zelda', filename: 'c.flac', incidence: 80 }
    ])

    const menu = result.playlists.find((p) => p.id === 'bgmsmashmenu')!
    expect(menu.assignments.map((a) => a.filename)).toEqual(['a.flac'])
    const zelda = result.playlists.find((p) => p.id === 'bgmzelda')!
    expect(zelda.assignments).toEqual([
      { seriesId: 'zelda', seriesName: 'Zelda', filename: 'c.flac', title: 'Song C', incidence: 80 }
    ])
  })

  it('groups songs by incidence into separate same-id blocks (per-song incidence)', () => {
    const modPath = seedMod()
    saveManagePlaylists(ws.root, modPath, [
      { playlistId: 'bgmsmashmenu', seriesId: 'mario', filename: 'a.flac', incidence: 10 },
      { playlistId: 'bgmsmashmenu', seriesId: 'mario', filename: 'b.flac', incidence: 50 }
    ])

    const toml = readFileSync(join(modPath, 'mario', 'series.toml'), 'utf8')
    expect((toml.match(/\[\[playlists\]\]/g) ?? []).length).toBe(2)
    expect(toml).toContain('incidence = 10')
    expect(toml).toContain('incidence = 50')

    const reloaded = loadManagePlaylists(ws.root, modPath)
    const menu = reloaded.playlists.find((p) => p.id === 'bgmsmashmenu')!
    expect(menu.assignments).toEqual([
      { seriesId: 'mario', seriesName: 'Mario', filename: 'a.flac', title: 'Song A', incidence: 10 },
      { seriesId: 'mario', seriesName: 'Mario', filename: 'b.flac', title: 'Song B', incidence: 50 }
    ])
  })

  it('removes a series’ [[playlists]] blocks when it has no assignments', () => {
    const modPath = seedMod()
    saveManagePlaylists(ws.root, modPath, [])

    const toml = readFileSync(join(modPath, 'mario', 'series.toml'), 'utf8')
    expect(toml).not.toContain('[[playlists]]')
    expect(loadManagePlaylists(ws.root, modPath).playlists.every((p) => p.assignedCount === 0)).toBe(true)
  })

  it('preserves [series], [[games]] and [default-track-data]', () => {
    const modPath = seedMod()
    saveManagePlaylists(ws.root, modPath, [
      { playlistId: 'bgmsmashmenu', seriesId: 'mario', filename: 'a.flac', incidence: 25 }
    ])

    const toml = readFileSync(join(modPath, 'mario', 'series.toml'), 'utf8')
    expect(toml).toContain('id = "mario"')
    expect(toml).toContain('series-playlist = "bgmmario"')
    expect(toml).toContain('[[games]]')
    expect(toml).toContain('[default-track-data]')
    expect(toml).toContain('incidence = 25')
  })
})

describe('path traversal guard', () => {
  it('throws when the mod path escapes Mods/MusicMods', () => {
    const outside = join(ws.root, 'outside')
    expect(() => loadManagePlaylists(ws.root, outside)).toThrow('Invalid mod path.')
    expect(() => saveManagePlaylists(ws.root, outside, [])).toThrow('Invalid mod path.')
  })
})
