import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { join } from 'path'
import { listMods, listModSeries } from './mods'
import { makeWorkspace, makeDir, writeSeries, type Workspace } from './test-utils'

let ws: Workspace

beforeEach(() => {
  ws = makeWorkspace()
})

afterEach(() => {
  ws.cleanup()
})

const CSV = 'filename,title,game\nsong.flac,Song,Game\n'

describe('listMods', () => {
  it('returns directories under Mods/MusicMods', () => {
    makeDir(ws, 'persona')
    makeDir(ws, 'mario')
    const mods = listMods(ws.root).map((m) => m.name).sort()
    expect(mods).toEqual(['mario', 'persona'])
  })

  it('returns [] when Mods/MusicMods does not exist', () => {
    expect(listMods(ws.root)).toEqual([])
  })
})

describe('listModSeries', () => {
  it('returns only directories containing tracks.csv', () => {
    writeSeries(ws, 'persona', 'persona', { tracksCsv: CSV })
    makeDir(ws, 'persona', 'empty-dir')
    const modPath = join(ws.root, 'Mods', 'MusicMods', 'persona')

    const series = listModSeries(ws.root, modPath).map((s) => s.name)
    expect(series).toEqual(['persona'])
  })

  it('skips dotfiles and songs-to-validate', () => {
    writeSeries(ws, 'persona', 'persona', { tracksCsv: CSV })
    writeSeries(ws, 'persona', 'songs-to-validate', { tracksCsv: CSV })
    writeSeries(ws, 'persona', '.hidden', { tracksCsv: CSV })
    const modPath = join(ws.root, 'Mods', 'MusicMods', 'persona')

    const series = listModSeries(ws.root, modPath).map((s) => s.name)
    expect(series).toEqual(['persona'])
  })

  it('returns [] for a path outside Mods/MusicMods (traversal guard)', () => {
    const outside = join(ws.root, 'elsewhere')
    expect(listModSeries(ws.root, outside)).toEqual([])
  })
})
