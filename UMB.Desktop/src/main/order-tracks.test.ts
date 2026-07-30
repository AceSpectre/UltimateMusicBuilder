import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { existsSync, readFileSync } from 'fs'
import { join, resolve } from 'path'
import { loadTrackOrderData, saveTrackOrderData } from './order-tracks'
import { makeWorkspace, writeSeries, type Workspace } from './test-utils'

let ws: Workspace

const ord = (...ids: string[]) => ids.map((id) => ({ id, fields: null }))

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
    saveTrackOrderData(ws.root, seriesPath, ord('mod:1', 'mod:0'))

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
    saveTrackOrderData(ws.root, seriesPath, ord('mod:1'))

    const reloaded = loadTrackOrderData(ws.root, seriesPath)
    expect(reloaded.items.map((i) => i.title)).toEqual([
      'Reach Out to the Truth',
      'Mass Destruction'
    ])
  })

  it('preserves commas in titles via quoting on round-trip', () => {
    const csv = 'filename,title,game\nsong.flac,"Hello, World",Game\n'
    const seriesPath = writeSeries(ws, 'm', 's', { tracksCsv: csv })
    saveTrackOrderData(ws.root, seriesPath, ord('mod:0'))

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

    saveTrackOrderData(ws.root, seriesPath, ord(
      'mod:0',
      'vanilla:0:ui_bgm_vanilla_one',
      'mod:1'
    ))

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
    saveTrackOrderData(ws.root, seriesPath, ord('mod:0', 'mod:1'))
    expect(() => readFileSync(join(seriesPath, 'song_order.toml'), 'utf8')).toThrow()
  })
})

describe('track fields', () => {
  const FULL_CSV =
    'filename,game,title,author,copyright,record_type,special_category,volume,info1,in_soundtest\n' +
    'a.nus3audio,g1,Song A,Auth,Copy,original,sf_situationlink,1,a_pinch.nus3audio,True\n' +
    'a_pinch.nus3audio,g1,Song A Pinch,,,original,,1,,True\n' +
    'b.nus3audio,g2,Song B,,,arrange,,1,,False\n'

  const SERIES_TOML =
    '[series]\nid = "s"\nexisting-series = true\n\n' +
    '[[games]]\nid = "g1"\nname = "Game One"\n\n' +
    '[[games]]\nid = "g2"\nname = "Game Two"\n'

  const payloadFrom = (data: ReturnType<typeof loadTrackOrderData>) =>
    data.items.map((i) => ({ id: i.id, fields: i.fields }))

  it('parses [[games]] blocks from series.toml', () => {
    const seriesPath = writeSeries(ws, 'm', 's', { tracksCsv: FULL_CSV, seriesToml: SERIES_TOML })
    const data = loadTrackOrderData(ws.root, seriesPath)
    expect(data.games).toEqual([
      { id: 'g1', name: 'Game One' },
      { id: 'g2', name: 'Game Two' }
    ])
  })

  it('returns no games when series.toml is absent', () => {
    const seriesPath = writeSeries(ws, 'm', 's', { tracksCsv: FULL_CSV })
    expect(loadTrackOrderData(ws.root, seriesPath).games).toEqual([])
  })

  it('parses [default-track-data] (record-type dash mapped to record_type)', () => {
    const seriesToml =
      SERIES_TOML +
      '\n[default-track-data]\ngame = "g2"\nauthor = "Nobuo"\ncopyright = "Square"\nrecord-type = "arrange"\nvolume = 1.0\n'
    const seriesPath = writeSeries(ws, 'm', 's', { tracksCsv: FULL_CSV, seriesToml })
    expect(loadTrackOrderData(ws.root, seriesPath).defaultTrackData).toEqual({
      game: 'g2',
      author: 'Nobuo',
      copyright: 'Square',
      record_type: 'arrange'
    })
  })

  it('returns null defaultTrackData when the table is absent', () => {
    const seriesPath = writeSeries(ws, 'm', 's', { tracksCsv: FULL_CSV, seriesToml: SERIES_TOML })
    expect(loadTrackOrderData(ws.root, seriesPath).defaultTrackData).toBeNull()
  })

  it('exposes editable fields with sensible defaults', () => {
    const seriesPath = writeSeries(ws, 'm', 's', { tracksCsv: FULL_CSV })
    const data = loadTrackOrderData(ws.root, seriesPath)
    expect(data.items[0].filename).toBe('a.nus3audio')
    expect(data.items[0].fields).toEqual({
      title: 'Song A',
      game: 'g1',
      author: 'Auth',
      copyright: 'Copy',
      record_type: 'original',
      special_category: 'sf_situationlink',
      info1: 'a_pinch.nus3audio',
      in_soundtest: 'True'
    })
  })

  it('marks rows referenced by another row info1 as pinch targets', () => {
    const seriesPath = writeSeries(ws, 'm', 's', { tracksCsv: FULL_CSV })
    const data = loadTrackOrderData(ws.root, seriesPath)
    expect(data.items.find((i) => i.filename === 'a_pinch.nus3audio')!.isPinchTarget).toBe(true)
    expect(data.items.find((i) => i.filename === 'a.nus3audio')!.isPinchTarget).toBe(false)
  })

  it('round-trips edited fields to tracks.csv', () => {
    const seriesPath = writeSeries(ws, 'm', 's', { tracksCsv: FULL_CSV })
    const items = payloadFrom(loadTrackOrderData(ws.root, seriesPath))
    const b = items.find((i) => i.id === 'mod:2')!
    b.fields!.title = 'Renamed B'
    b.fields!.game = 'g1'
    b.fields!.record_type = 'new_arrange'
    b.fields!.in_soundtest = 'True'
    saveTrackOrderData(ws.root, seriesPath, items)

    const reloaded = loadTrackOrderData(ws.root, seriesPath)
    const rb = reloaded.items.find((i) => i.filename === 'b.nus3audio')!
    expect(rb.fields).toMatchObject({
      title: 'Renamed B',
      game: 'g1',
      record_type: 'new_arrange',
      in_soundtest: 'True'
    })
    expect(readFileSync(join(seriesPath, 'tracks.csv'), 'utf8')).toContain('Renamed B')
  })

  it('writes the pinch coupling (info1 + special_category) and clears both together', () => {
    const seriesPath = writeSeries(ws, 'm', 's', { tracksCsv: FULL_CSV })

    let items = payloadFrom(loadTrackOrderData(ws.root, seriesPath))
    const set = items.find((i) => i.id === 'mod:2')!
    set.fields!.info1 = 'a_pinch.nus3audio'
    set.fields!.special_category = 'sf_situationlink'
    saveTrackOrderData(ws.root, seriesPath, items)

    let b = loadTrackOrderData(ws.root, seriesPath).items.find((i) => i.filename === 'b.nus3audio')!
    expect(b.fields).toMatchObject({ info1: 'a_pinch.nus3audio', special_category: 'sf_situationlink' })

    items = payloadFrom(loadTrackOrderData(ws.root, seriesPath))
    const clear = items.find((i) => i.id === 'mod:2')!
    clear.fields!.info1 = ''
    clear.fields!.special_category = ''
    saveTrackOrderData(ws.root, seriesPath, items)

    b = loadTrackOrderData(ws.root, seriesPath).items.find((i) => i.filename === 'b.nus3audio')!
    expect(b.fields).toMatchObject({ info1: '', special_category: '' })
  })

  it('preserves a non-pinch special_category when other fields are edited', () => {
    const csv =
      'filename,game,title,special_category,in_soundtest\n' +
      'x.flac,g1,X,mario_3dland_scenelink,True\n'
    const seriesPath = writeSeries(ws, 'm', 's', { tracksCsv: csv })
    const items = payloadFrom(loadTrackOrderData(ws.root, seriesPath))
    items[0].fields!.title = 'X edited'
    saveTrackOrderData(ws.root, seriesPath, items)

    const reloaded = loadTrackOrderData(ws.root, seriesPath)
    expect(reloaded.items[0].fields!.special_category).toBe('mario_3dland_scenelink')
    expect(reloaded.items[0].fields!.title).toBe('X edited')
  })

  it('persists a vanilla pinch reference (info_*) without marking a target', () => {
    const seriesPath = writeSeries(ws, 'm', 's', { tracksCsv: FULL_CSV })
    const items = payloadFrom(loadTrackOrderData(ws.root, seriesPath))
    const b = items.find((i) => i.id === 'mod:2')!
    b.fields!.info1 = 'info_bgm_some_vanilla_song'
    b.fields!.special_category = 'sf_situationlink'
    saveTrackOrderData(ws.root, seriesPath, items)

    const reloaded = loadTrackOrderData(ws.root, seriesPath)
    const row = reloaded.items.find((i) => i.filename === 'b.nus3audio')!
    expect(row.fields).toMatchObject({ info1: 'info_bgm_some_vanilla_song', special_category: 'sf_situationlink' })
    expect(reloaded.items.some((i) => i.isPinchTarget)).toBe(true)
    expect(row.isPinchTarget).toBe(false)
  })
})

// Integration: needs the real Resources/Game dump + the matt-dan mariokart mod. Auto-skips otherwise.
const REPO_ROOT = resolve(__dirname, '..', '..', '..')
const MARIOKART = join(REPO_ROOT, 'Mods', 'MusicMods', 'matt-dan-18-05-26', 'mariokart')
const HAS_REAL_MARIOKART =
  existsSync(join(REPO_ROOT, 'Resources', 'Game', 'ui', 'param', 'database', 'ui_bgm_db.prc')) &&
  existsSync(join(MARIOKART, 'series.toml'))

describe.runIf(HAS_REAL_MARIOKART)('vanilla catalog wired into a real existing series', () => {
  it('merges vanilla mariokart games and exposes vanilla songs', () => {
    const data = loadTrackOrderData(REPO_ROOT, MARIOKART)
    expect(data.games.length).toBeGreaterThan(2)
    expect(data.vanillaSongs.length).toBeGreaterThan(0)
    expect(data.vanillaSongs.every((s) => s.infoId.startsWith('info_'))).toBe(true)
  })
})
