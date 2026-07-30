import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { existsSync, readFileSync } from 'fs'
import { join } from 'path'
import { analyzeMerge, validateOutputName, executeMerge } from './merge'
import { makeWorkspace, makeDir, writeSeries, writeFile, type Workspace } from './test-utils'

let ws: Workspace
const onLine = (): void => {}

beforeEach(() => {
  ws = makeWorkspace()
})

afterEach(() => {
  ws.cleanup()
})

const MINIMAL_TOML = (id: string, name: string): string =>
  `[series]\nid = "${id}"\nname = "${name}"\n`

function mod(name: string): string {
  return makeDir(ws, name)
}

/** Series folder with a parseable series.toml + tracks.csv + a dummy audio file. */
function series(
  modName: string,
  seriesName: string,
  toml: string,
  csv: string,
  audio: string[] = []
): string {
  const dir = writeSeries(ws, modName, seriesName, { tracksCsv: csv, seriesToml: toml })
  for (const a of audio) writeFile(dir, a, a.toUpperCase())
  return dir
}

describe('analyzeMerge', () => {
  it('groups series across mods and flags conflicts', () => {
    const a = mod('modA')
    const b = mod('modB')
    series('modA', 'persona', MINIMAL_TOML('persona', 'Persona'), 'filename\nx.flac\n')
    series('modA', 'mario', MINIMAL_TOML('mario', 'Mario'), 'filename\ny.flac\n')
    series('modB', 'persona', MINIMAL_TOML('persona', 'Persona'), 'filename\nz.flac\n')
    series('modB', 'zelda', MINIMAL_TOML('zelda', 'Zelda'), 'filename\nw.flac\n')

    const result = analyzeMerge(ws.root, [a, b])

    expect(result.modNames).toEqual(['modA', 'modB'])
    expect(result.totalSeries).toBe(3)
    expect(result.series.map((s) => s.name)).toEqual(['mario', 'persona', 'zelda'])
    expect(result.conflicts).toEqual([{ seriesName: 'persona', mods: ['modA', 'modB'] }])
  })

  it('skips dotfiles and directories without series.toml', () => {
    const a = mod('modA')
    series('modA', 'real', MINIMAL_TOML('real', 'Real'), 'filename\nx.flac\n')
    series('modA', '.hidden', MINIMAL_TOML('h', 'H'), 'filename\nh.flac\n')
    makeDir(ws, 'modA', 'notoml')

    const result = analyzeMerge(ws.root, [a])
    expect(result.series.map((s) => s.name)).toEqual(['real'])
  })

  it('throws for a path outside Mods/MusicMods', () => {
    expect(() => analyzeMerge(ws.root, [join(ws.root, 'outside')])).toThrow(/Invalid mod path/)
  })

  it('throws when a mod path does not exist', () => {
    expect(() => analyzeMerge(ws.root, [join(ws.root, 'Mods', 'MusicMods', 'ghost')])).toThrow(
      /Mod not found/
    )
  })
})

describe('validateOutputName', () => {
  it('rejects empty / whitespace names', () => {
    expect(validateOutputName(ws.root, '')).toBe('Name cannot be empty.')
    expect(validateOutputName(ws.root, '   ')).toBe('Name cannot be empty.')
  })

  it('rejects names with filesystem-illegal characters', () => {
    expect(validateOutputName(ws.root, 'bad/name')).toBe('Name contains invalid characters.')
    expect(validateOutputName(ws.root, 'a:b')).toBe('Name contains invalid characters.')
  })

  it('rejects names that already exist', () => {
    makeDir(ws, 'taken')
    expect(validateOutputName(ws.root, 'taken')).toBe('A mod with that name already exists.')
  })

  it('returns null for a valid, unused name', () => {
    expect(validateOutputName(ws.root, 'fresh')).toBeNull()
  })
})

describe('executeMerge', () => {
  it('copies a single-source series verbatim', async () => {
    const a = mod('modA')
    series(
      'modA',
      'persona',
      MINIMAL_TOML('persona', 'Persona'),
      'filename,title\na.flac,A\nb.flac,B\n',
      ['a.flac', 'b.flac']
    )

    const result = await executeMerge(ws.root, [a], 'merged', null, onLine)

    expect(result.totalSeries).toBe(1)
    expect(result.totalTracks).toBe(2)
    expect(result.conflictsResolved).toBe(0)

    const outSeries = join(result.outputPath, 'persona')
    expect(existsSync(join(outSeries, 'series.toml'))).toBe(true)
    expect(existsSync(join(outSeries, 'tracks.csv'))).toBe(true)
    expect(existsSync(join(outSeries, 'a.flac'))).toBe(true)
    expect(existsSync(join(outSeries, 'b.flac'))).toBe(true)
  })

  it('merges a conflicting series: dedupes tracks, unions games/playlists, honours priority', async () => {
    const a = mod('modA')
    const b = mod('modB')

    const tomlA =
      '[series]\nid = "persona"\nname = "Persona A"\n\n' +
      '[[games]]\nid = "p3"\nname = "Persona 3"\n\n' +
      '[[playlists]]\nid = "bgmjack"\nincidence = 100\nsongs = "*"\n'
    const tomlB =
      '[series]\nid = "persona"\nname = "Persona B"\n\n' +
      '[[games]]\nid = "p5"\nname = "Persona 5"\n\n' +
      '[[playlists]]\nid = "bgmjack"\nincidence = 100\nsongs = "*"\n\n' +
      '[[playlists]]\nid = "bgmextra"\nincidence = 50\nsongs = "*"\n'

    series('modA', 'persona', tomlA, 'filename,title\na.flac,Track A\n', ['a.flac'])
    series(
      'modB',
      'persona',
      tomlB,
      'filename,title\na.flac,Track A2\nb.flac,Track B\n',
      ['a.flac', 'b.flac']
    )

    const result = await executeMerge(ws.root, [a, b], 'merged', a, onLine)

    expect(result.conflictsResolved).toBe(1)
    expect(result.totalSeries).toBe(1)
    expect(result.totalTracks).toBe(2)

    const outSeries = join(result.outputPath, 'persona')
    const mergedToml = readFileSync(join(outSeries, 'series.toml'), 'utf8')
    expect(mergedToml).toContain('id = "persona"')
    expect(mergedToml).toContain('name = "Persona A"')
    expect(mergedToml).toContain('id = "p3"')
    expect(mergedToml).toContain('id = "p5"')
    expect(mergedToml).toContain('id = "bgmjack"')
    expect(mergedToml).toContain('id = "bgmextra"')

    const mergedCsv = readFileSync(join(outSeries, 'tracks.csv'), 'utf8')
    expect(mergedCsv).toContain('a.flac')
    expect(mergedCsv).toContain('b.flac')
    expect(mergedCsv.split(/\r?\n/)[0]).toContain('order')
    const aCount = (mergedCsv.match(/a\.flac/g) ?? []).length
    expect(aCount).toBe(1)
  })

  it('merges series-order.toml across mods, deduping ids in priority order', async () => {
    const a = mod('modA')
    const b = mod('modB')
    series('modA', 'persona', MINIMAL_TOML('persona', 'Persona'), 'filename\na.flac\n')
    series('modB', 'mario', MINIMAL_TOML('mario', 'Mario'), 'filename\nb.flac\n')
    writeFile(a, 'series-order.toml', 'order = [\n  "x",\n  "y"\n]\n')
    writeFile(b, 'series-order.toml', 'order = [\n  "y",\n  "z"\n]\n')

    const result = await executeMerge(ws.root, [a, b], 'merged', a, onLine)
    const order = readFileSync(join(result.outputPath, 'series-order.toml'), 'utf8')
    const ids = [...order.matchAll(/^\s+"([^"]+)",/gm)].map((m) => m[1])
    expect(ids).toEqual(['x', 'y', 'z'])
  })

  it('throws on an invalid output name', async () => {
    const a = mod('modA')
    series('modA', 'persona', MINIMAL_TOML('persona', 'Persona'), 'filename\na.flac\n')
    await expect(executeMerge(ws.root, [a], 'bad/name', null, onLine)).rejects.toThrow(
      'Name contains invalid characters.'
    )
  })

  it('throws when no series folders are found', async () => {
    const a = mod('emptyA')
    const b = mod('emptyB')
    await expect(executeMerge(ws.root, [a, b], 'merged', null, onLine)).rejects.toThrow(
      'No series folders found in the selected mods.'
    )
  })
})
