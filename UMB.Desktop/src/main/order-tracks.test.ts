import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { readFileSync } from 'fs'
import { join } from 'path'
import { loadTrackOrderData, saveTrackOrderData } from './order-tracks'
import { makeWorkspace, writeSeries, type Workspace } from './test-utils'

let ws: Workspace

beforeEach(() => {
  ws = makeWorkspace()
})

afterEach(() => {
  ws.cleanup()
})

const BASIC_CSV =
  'filename,title,game\n' +
  'Mass Destruction.flac,Mass Destruction,Persona 3\n' +
  'reach-out.flac,Reach Out to the Truth,Persona 4\n'

describe('loadTrackOrderData', () => {
  it('maps CSV rows to items with derived bgmId and stable ids', () => {
    const seriesPath = writeSeries(ws, 'persona', 'persona', { tracksCsv: BASIC_CSV })
    const data = loadTrackOrderData(ws.root, seriesPath)

    expect(data.items).toHaveLength(2)
    expect(data.items[0]).toMatchObject({
      id: 'mod:0',
      title: 'Mass Destruction',
      subtitle: 'Persona 3 - Mass Destruction.flac',
      bgmId: 'ui_bgm_mass_destruction',
      isLocked: false,
      originalIndex: 0
    })
    expect(data.items[1].bgmId).toBe('ui_bgm_reach_out')
  })

  it('falls back title -> filename -> Track N', () => {
    const csv = 'filename,title,game\nonly-file.flac,,\n,,\n'
    const seriesPath = writeSeries(ws, 'm', 's', { tracksCsv: csv })
    const data = loadTrackOrderData(ws.root, seriesPath)

    expect(data.items[0].title).toBe('only-file.flac')
    expect(data.items[1].title).toBe('Track 2')
  })

  it('omits game from subtitle when absent', () => {
    const csv = 'filename,title,game\nsong.flac,Song,\n'
    const seriesPath = writeSeries(ws, 'm', 's', { tracksCsv: csv })
    const data = loadTrackOrderData(ws.root, seriesPath)
    expect(data.items[0].subtitle).toBe('song.flac')
  })

  it('reads existing-series flag from series.toml', () => {
    const seriesPath = writeSeries(ws, 'm', 's', {
      tracksCsv: BASIC_CSV,
      seriesToml: 'id = "final_fantasy"\nexisting-series = true\n'
    })
    const data = loadTrackOrderData(ws.root, seriesPath)
    expect(data.isExistingSeries).toBe(true)
  })

  it('defaults existing-series to false when series.toml missing', () => {
    const seriesPath = writeSeries(ws, 'm', 's', { tracksCsv: BASIC_CSV })
    const data = loadTrackOrderData(ws.root, seriesPath)
    expect(data.isExistingSeries).toBe(false)
  })

  it('sorts mods by order column, sinking missing/NaN to the end', () => {
    const csv =
      'filename,title,game,order\n' +
      'a.flac,A,,2\n' +
      'b.flac,B,,\n' +
      'c.flac,C,,0\n'
    const seriesPath = writeSeries(ws, 'm', 's', { tracksCsv: csv })
    const data = loadTrackOrderData(ws.root, seriesPath)
    expect(data.items.map((i) => i.title)).toEqual(['C', 'A', 'B'])
  })

  describe('with song_order.toml', () => {
    it('preserves vanilla entries as locked and matches mod items by bgmId', () => {
      const songOrder =
        'song_order = [\n' +
        '  "ui_bgm_vanilla_one",\n' +
        '  "ui_bgm_mass_destruction",\n' +
        '  "ui_bgm_vanilla_two"\n' +
        ']\n'
      const seriesPath = writeSeries(ws, 'persona', 'persona', {
        tracksCsv: BASIC_CSV,
        seriesToml: 'existing-series = true\n',
        songOrderToml: songOrder
      })
      const data = loadTrackOrderData(ws.root, seriesPath)

      expect(data.hasSongOrder).toBe(true)
      expect(data.items.map((i) => i.bgmId)).toEqual([
        'ui_bgm_vanilla_one',
        'ui_bgm_mass_destruction',
        'ui_bgm_vanilla_two',
        'ui_bgm_reach_out'
      ])

      const vanilla = data.items[0]
      expect(vanilla.isLocked).toBe(true)
      expect(vanilla.originalIndex).toBeNull()
      expect(vanilla.title).toBe('Vanilla One')

      const mod = data.items[1]
      expect(mod.isLocked).toBe(false)
      expect(mod.originalIndex).toBe(0)
    })
  })
})

describe('path traversal guard', () => {
  it('throws when series path escapes Mods/MusicMods', () => {
    writeSeries(ws, 'm', 's', { tracksCsv: BASIC_CSV })
    const outside = join(ws.root, 'not-mods')
    expect(() => loadTrackOrderData(ws.root, outside)).toThrow('Invalid series path.')
    expect(() => saveTrackOrderData(ws.root, outside, [])).toThrow('Invalid series path.')
  })
})

describe('saveTrackOrderData', () => {
  it('rewrites tracks.csv with sequential order values and appends order header', () => {
    const seriesPath = writeSeries(ws, 'm', 's', { tracksCsv: BASIC_CSV })
    saveTrackOrderData(ws.root, seriesPath, ['mod:1', 'mod:0'])

    const csv = readFileSync(join(seriesPath, 'tracks.csv'), 'utf8')
    expect(csv.split(/\r?\n/)[0]).toContain('order')

    const reloaded = loadTrackOrderData(ws.root, seriesPath)
    expect(reloaded.items.map((i) => i.title)).toEqual([
      'Reach Out to the Truth',
      'Mass Destruction'
    ])
  })

  it('appends rows omitted from orderedIds to the end', () => {
    const seriesPath = writeSeries(ws, 'm', 's', { tracksCsv: BASIC_CSV })
    saveTrackOrderData(ws.root, seriesPath, ['mod:1'])

    const reloaded = loadTrackOrderData(ws.root, seriesPath)
    expect(reloaded.items.map((i) => i.title)).toEqual([
      'Reach Out to the Truth',
      'Mass Destruction'
    ])
  })

  it('preserves commas in titles via quoting on round-trip', () => {
    const csv = 'filename,title,game\nsong.flac,"Hello, World",Game\n'
    const seriesPath = writeSeries(ws, 'm', 's', { tracksCsv: csv })
    saveTrackOrderData(ws.root, seriesPath, ['mod:0'])

    const reloaded = loadTrackOrderData(ws.root, seriesPath)
    expect(reloaded.items[0].title).toBe('Hello, World')
  })

  it('writes song_order.toml for existing-series with vanilla/mod tags', () => {
    const songOrder =
      'song_order = [\n  "ui_bgm_vanilla_one",\n  "ui_bgm_mass_destruction"\n]\n'
    const seriesPath = writeSeries(ws, 'persona', 'persona', {
      tracksCsv: BASIC_CSV,
      seriesToml: 'existing-series = true\n',
      songOrderToml: songOrder
    })

    saveTrackOrderData(ws.root, seriesPath, [
      'mod:0',
      'vanilla:0:ui_bgm_vanilla_one',
      'mod:1'
    ])

    const written = readFileSync(join(seriesPath, 'song_order.toml'), 'utf8')
    expect(written).toContain('song_order = [')
    expect(written).toContain('"ui_bgm_mass_destruction"')
    expect(written).toContain('# mod')
    expect(written).toContain('"ui_bgm_vanilla_one"')
    expect(written).toContain('# vanilla')
  })

  it('does not write song_order.toml for non-existing series', () => {
    const seriesPath = writeSeries(ws, 'm', 's', {
      tracksCsv: BASIC_CSV,
      seriesToml: 'existing-series = false\n'
    })
    saveTrackOrderData(ws.root, seriesPath, ['mod:0', 'mod:1'])
    expect(() => readFileSync(join(seriesPath, 'song_order.toml'), 'utf8')).toThrow()
  })
})
