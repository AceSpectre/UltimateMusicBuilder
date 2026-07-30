import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { readFileSync } from 'fs'
import { join } from 'path'
import { createSeries, loadSeriesOrderData, saveSeriesOrderData, setSeriesIcon } from './order-series'
import { makeWorkspace, makeDir, writeSeries, writeFile, type Workspace } from './test-utils'

let ws: Workspace

beforeEach(() => {
  ws = makeWorkspace()
})

afterEach(() => {
  ws.cleanup()
})

const DUMMY_CSV = 'filename,title,game\nx.flac,X,Game\n'

const ord = (...ids: string[]) => ids.map((id) => ({ id, fields: null }))

/** Lays down a custom-series folder (tracks.csv + series.toml) under a mod. */
function writeCustomSeries(mod: string, series: string, toml: string): string {
  return writeSeries(ws, mod, series, { tracksCsv: DUMMY_CSV, seriesToml: toml })
}

describe('loadSeriesOrderData', () => {
  it('lists custom series sorted alphabetically by name when no order file', () => {
    const modPath = makeDir(ws, 'mymod')
    writeCustomSeries('mymod', 'first', 'id = "bravo"\nname = "Bravo"\n')
    writeCustomSeries('mymod', 'second', 'id = "alpha"\nname = "Alpha"\n')

    const data = loadSeriesOrderData(ws.root, modPath)

    expect(data.modName).toBe('mymod')
    expect(data.hasSeriesOrder).toBe(false)
    expect(data.items).toHaveLength(2)
    expect(data.items[0]).toMatchObject({
      id: 'series:0',
      name: 'Alpha',
      seriesId: 'alpha',
      iconDataUrl: null,
      originalIndex: 0
    })
    expect(data.items[1].seriesId).toBe('bravo')
  })

  it('excludes existing-series, "etc", dotfiles, and dirs without series.toml', () => {
    const modPath = makeDir(ws, 'mymod')
    writeCustomSeries('mymod', 'good', 'id = "good"\nname = "Good"\n')
    writeCustomSeries('mymod', 'existing', 'id = "ff"\nname = "FF"\nexisting-series = true\n')
    writeCustomSeries('mymod', 'etcdir', 'id = "etc"\nname = "Etc"\n')
    writeCustomSeries('mymod', '.hidden', 'id = "h"\nname = "H"\n')
    makeDir(ws, 'mymod', 'notoml')

    const data = loadSeriesOrderData(ws.root, modPath)
    expect(data.items.map((i) => i.seriesId)).toEqual(['good'])
  })

  it('honours series-order.toml, appending unlisted series by name', () => {
    const modPath = makeDir(ws, 'mymod')
    writeCustomSeries('mymod', 'da', 'id = "a"\nname = "A"\n')
    writeCustomSeries('mymod', 'db', 'id = "b"\nname = "B"\n')
    writeCustomSeries('mymod', 'dc', 'id = "c"\nname = "C"\n')
    writeFile(modPath, 'series-order.toml', 'order = [\n  "c",\n  "a"\n]\n')

    const data = loadSeriesOrderData(ws.root, modPath)
    expect(data.hasSeriesOrder).toBe(true)
    expect(data.items.map((i) => i.seriesId)).toEqual(['c', 'a', 'b'])
  })

  it('embeds icon.png as a base64 data URL', () => {
    const modPath = makeDir(ws, 'mymod')
    const seriesDir = writeCustomSeries('mymod', 'p', 'id = "p"\nname = "P"\n')
    const png = Buffer.from([0x89, 0x50, 0x4e, 0x47])
    writeFile(seriesDir, 'icon.png', png)

    const data = loadSeriesOrderData(ws.root, modPath)
    expect(data.items[0].iconDataUrl).toBe(
      'data:image/png;base64,' + png.toString('base64')
    )
  })
})

describe('path traversal guard', () => {
  it('throws when the mod path escapes Mods/MusicMods', () => {
    const outside = join(ws.root, 'outside')
    expect(() => loadSeriesOrderData(ws.root, outside)).toThrow('Invalid mod path.')
    expect(() => saveSeriesOrderData(ws.root, outside, [])).toThrow('Invalid mod path.')
    expect(() =>
      createSeries(ws.root, outside, { seriesId: 'x', name: 'X', seriesPlaylist: '', games: [{ id: 'g', name: 'G' }] })
    ).toThrow('Invalid mod path.')
  })
})

describe('createSeries', () => {
  const oneGame = (over: Partial<Parameters<typeof createSeries>[2]> = {}) => ({
    seriesId: 'my_series',
    name: 'My Series',
    seriesPlaylist: 'bgm_my_series',
    games: [{ id: 'first_game', name: 'First Game' }],
    ...over
  })

  it('creates the folder with series.toml + header-only tracks.csv and returns it in the reload', () => {
    const modPath = makeDir(ws, 'mymod')
    const result = createSeries(ws.root, modPath, oneGame())

    expect(result.items.map((i) => i.seriesId)).toContain('my_series')

    const toml = readFileSync(join(modPath, 'my_series', 'series.toml'), 'utf8')
    expect(toml).toContain('id = "my_series"')
    expect(toml).toContain('name = "My Series"')
    expect(toml).toContain('playlist-incidence = 100')
    expect(toml).toContain('series-playlist = "bgm_my_series"')
    expect(toml).toContain('[[games]]')
    expect(toml).toContain('id = "first_game"')

    const csv = readFileSync(join(modPath, 'my_series', 'tracks.csv'), 'utf8')
    expect(csv.trim()).toBe('filename,game,title,author,copyright,record_type,special_category,volume,info1,in_soundtest')
  })

  it('makes the first game the default game in [default-track-data]', () => {
    const modPath = makeDir(ws, 'mymod')
    createSeries(
      ws.root,
      modPath,
      oneGame({ games: [{ id: 'first_game', name: 'First' }, { id: 'second_game', name: 'Second' }] })
    )

    const data = loadSeriesOrderData(ws.root, modPath)
    const item = data.items.find((i) => i.seriesId === 'my_series')!
    expect(item.fields.defaultGame).toBe('first_game')
    expect(item.fields.games).toEqual([
      { id: 'first_game', name: 'First' },
      { id: 'second_game', name: 'Second' }
    ])
    expect(item.fields.defaultRecordType).toBe('original')
    expect(item.fields.defaultVolume).toBe(1)
  })

  it('omits series-playlist when blank', () => {
    const modPath = makeDir(ws, 'mymod')
    createSeries(ws.root, modPath, oneGame({ seriesPlaylist: '' }))
    const toml = readFileSync(join(modPath, 'my_series', 'series.toml'), 'utf8')
    expect(toml).not.toContain('series-playlist')
  })

  it('rejects a duplicate series id (folder already exists)', () => {
    const modPath = makeDir(ws, 'mymod')
    writeCustomSeries('mymod', 'my_series', 'id = "my_series"\nname = "Existing"\n')
    expect(() => createSeries(ws.root, modPath, oneGame())).toThrow('already exists')
  })

  it('rejects an invalid series id', () => {
    const modPath = makeDir(ws, 'mymod')
    expect(() => createSeries(ws.root, modPath, oneGame({ seriesId: 'Bad Id!' }))).toThrow()
    expect(() => createSeries(ws.root, modPath, oneGame({ seriesId: '' }))).toThrow()
  })

  it('rejects the reserved "etc" id', () => {
    const modPath = makeDir(ws, 'mymod')
    expect(() => createSeries(ws.root, modPath, oneGame({ seriesId: 'etc' }))).toThrow('reserved')
  })

  it('rejects a blank name', () => {
    const modPath = makeDir(ws, 'mymod')
    expect(() => createSeries(ws.root, modPath, oneGame({ name: '  ' }))).toThrow()
  })

  it('requires at least one game', () => {
    const modPath = makeDir(ws, 'mymod')
    expect(() => createSeries(ws.root, modPath, oneGame({ games: [] }))).toThrow('game')
  })

  it('writes icon.png when an icon data URL is supplied', () => {
    const modPath = makeDir(ws, 'mymod')
    createSeries(ws.root, modPath, oneGame({ iconDataUrl: PNG_DATA_URL }))

    const data = loadSeriesOrderData(ws.root, modPath)
    expect(data.items.find((i) => i.seriesId === 'my_series')!.iconDataUrl).toBe(PNG_DATA_URL)
  })

  it('rejects a non-PNG icon data URL', () => {
    const modPath = makeDir(ws, 'mymod')
    expect(() =>
      createSeries(ws.root, modPath, oneGame({ iconDataUrl: 'data:image/jpeg;base64,AAAA' }))
    ).toThrow('PNG')
  })
})

// 1x1 transparent PNG.
const PNG_DATA_URL =
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=='

describe('setSeriesIcon', () => {
  it('writes icon.png to an existing series and returns the data URL', () => {
    const modPath = makeDir(ws, 'mymod')
    writeCustomSeries('mymod', 'da', 'id = "a"\nname = "A"\n')

    const returned = setSeriesIcon(ws.root, modPath, 'a', PNG_DATA_URL)
    expect(returned).toBe(PNG_DATA_URL)
    expect(loadSeriesOrderData(ws.root, modPath).items[0].iconDataUrl).toBe(PNG_DATA_URL)
  })

  it('throws for an unknown series id', () => {
    const modPath = makeDir(ws, 'mymod')
    writeCustomSeries('mymod', 'da', 'id = "a"\nname = "A"\n')
    expect(() => setSeriesIcon(ws.root, modPath, 'nope', PNG_DATA_URL)).toThrow('not found')
  })

  it('rejects a non-PNG icon', () => {
    const modPath = makeDir(ws, 'mymod')
    writeCustomSeries('mymod', 'da', 'id = "a"\nname = "A"\n')
    expect(() => setSeriesIcon(ws.root, modPath, 'a', 'data:text/plain;base64,AAAA')).toThrow('PNG')
  })

  it('rejects a mod path outside Mods/MusicMods', () => {
    const outside = join(ws.root, 'outside')
    expect(() => setSeriesIcon(ws.root, outside, 'a', PNG_DATA_URL)).toThrow('Invalid mod path.')
  })
})

describe('saveSeriesOrderData', () => {
  it('writes series-order.toml in the requested order and appends omitted series', () => {
    const modPath = makeDir(ws, 'mymod')
    writeCustomSeries('mymod', 'da', 'id = "a"\nname = "A"\n')
    writeCustomSeries('mymod', 'db', 'id = "b"\nname = "B"\n')
    writeCustomSeries('mymod', 'dc', 'id = "c"\nname = "C"\n')

    const result = saveSeriesOrderData(ws.root, modPath, ord('series:2', 'series:0'))

    expect(result.hasSeriesOrder).toBe(true)
    expect(result.items.map((i) => i.seriesId)).toEqual(['c', 'a', 'b'])

    const written = readFileSync(join(modPath, 'series-order.toml'), 'utf8')
    expect(written).toContain('# Custom series display order')
    expect(written).toContain('order = [')
    expect(written).toContain('    "c",')
    expect(written).toContain('    "a",')
    expect(written).toContain('    "b",')
  })
})

describe('series.toml [series] fields', () => {
  const RICH_TOML =
    '[series]\nid = "a"\nname = "A"\nexisting-series = false\nplaylist-incidence = 100\nseries-playlist = "bgmmario"\n\n' +
    '[[games]]\nid = "g1"\nname = "Game One"\n\n[default-track-data]\ngame = "g1"\n'

  it('parses the [series] + [default-track-data] fields into each item', () => {
    const modPath = makeDir(ws, 'mymod')
    writeCustomSeries('mymod', 'da', RICH_TOML)
    const data = loadSeriesOrderData(ws.root, modPath)
    expect(data.items[0].fields).toEqual({
      name: 'A',
      seriesPlaylist: 'bgmmario',
      playlistIncidence: 100,
      games: [{ id: 'g1', name: 'Game One' }],
      defaultGame: 'g1',
      defaultAuthor: '',
      defaultCopyright: '',
      defaultRecordType: 'original',
      defaultVolume: 1
    })
  })

  it('edits a game name and adds a new game, preserving id and other tables', () => {
    const modPath = makeDir(ws, 'mymod')
    writeCustomSeries('mymod', 'da', RICH_TOML)
    const data = loadSeriesOrderData(ws.root, modPath)
    const items = data.items.map((i) => ({
      id: i.id,
      fields: {
        ...i.fields,
        games: [{ id: 'g1', name: 'Game One Renamed' }, { id: 'g2', name: 'Game Two' }]
      }
    }))
    saveSeriesOrderData(ws.root, modPath, items)

    const toml = readFileSync(join(modPath, 'da', 'series.toml'), 'utf8')
    expect(toml).toContain('name = "Game One Renamed"')
    expect(toml).toContain('id = "g2"')
    expect(toml).toContain('name = "Game Two"')
    expect(toml).toContain('id = "a"')
    expect(toml).toContain('[default-track-data]')
    expect((toml.match(/\[\[games\]\]/g) ?? []).length).toBe(2)

    const reloaded = loadSeriesOrderData(ws.root, modPath)
    expect(reloaded.items[0].fields.games).toEqual([
      { id: 'g1', name: 'Game One Renamed' },
      { id: 'g2', name: 'Game Two' }
    ])
  })

  it('creates [[games]] (after [series], before [default-track-data]) when the series had none', () => {
    const modPath = makeDir(ws, 'mymod')
    writeCustomSeries('mymod', 'da', '[series]\nid = "a"\nname = "A"\n\n[default-track-data]\ngame = "x"\n')
    const data = loadSeriesOrderData(ws.root, modPath)
    expect(data.items[0].fields.games).toEqual([])

    const items = data.items.map((i) => ({ id: i.id, fields: { ...i.fields, games: [{ id: 'g1', name: 'G1' }] } }))
    saveSeriesOrderData(ws.root, modPath, items)

    const toml = readFileSync(join(modPath, 'da', 'series.toml'), 'utf8')
    expect(toml).toContain('[[games]]')
    expect(toml).toContain('id = "g1"')
    expect(toml.indexOf('[[games]]')).toBeLessThan(toml.indexOf('[default-track-data]'))
  })

  it('removes all [[games]] blocks when games are cleared', () => {
    const modPath = makeDir(ws, 'mymod')
    writeCustomSeries('mymod', 'da', RICH_TOML)
    const data = loadSeriesOrderData(ws.root, modPath)
    const items = data.items.map((i) => ({ id: i.id, fields: { ...i.fields, games: [] } }))
    saveSeriesOrderData(ws.root, modPath, items)

    const toml = readFileSync(join(modPath, 'da', 'series.toml'), 'utf8')
    expect(toml).not.toContain('[[games]]')
    expect(toml).toContain('[series]')
    expect(toml).toContain('[default-track-data]')
  })

  it('writes edited [series] fields back, preserving id and other tables', () => {
    const modPath = makeDir(ws, 'mymod')
    writeCustomSeries('mymod', 'da', RICH_TOML)
    const data = loadSeriesOrderData(ws.root, modPath)
    const items = data.items.map((i) => ({
      id: i.id,
      fields: { ...i.fields, name: 'Renamed', seriesPlaylist: 'bgmother', playlistIncidence: 50 }
    }))
    saveSeriesOrderData(ws.root, modPath, items)

    const toml = readFileSync(join(modPath, 'da', 'series.toml'), 'utf8')
    expect(toml).toContain('name = "Renamed"')
    expect(toml).toContain('series-playlist = "bgmother"')
    expect(toml).toContain('playlist-incidence = 50')
    expect(toml).toContain('id = "a"')
    expect(toml).toContain('[[games]]')
    expect(toml).toContain('[default-track-data]')

    const reloaded = loadSeriesOrderData(ws.root, modPath)
    expect(reloaded.items[0].fields).toMatchObject({ name: 'Renamed', seriesPlaylist: 'bgmother', playlistIncidence: 50 })
  })

  it('drops series-playlist when cleared', () => {
    const modPath = makeDir(ws, 'mymod')
    writeCustomSeries('mymod', 'da', RICH_TOML)
    const data = loadSeriesOrderData(ws.root, modPath)
    const items = data.items.map((i) => ({ id: i.id, fields: { ...i.fields, seriesPlaylist: '' } }))
    saveSeriesOrderData(ws.root, modPath, items)

    const toml = readFileSync(join(modPath, 'da', 'series.toml'), 'utf8')
    expect(toml).not.toContain('series-playlist')
  })

  it('writes edited [default-track-data] values back', () => {
    const modPath = makeDir(ws, 'mymod')
    writeCustomSeries('mymod', 'da', RICH_TOML)
    const data = loadSeriesOrderData(ws.root, modPath)
    const items = data.items.map((i) => ({
      id: i.id,
      fields: { ...i.fields, defaultGame: 'g1', defaultAuthor: 'Nobuo', defaultRecordType: 'arrange', defaultVolume: 0.8 }
    }))
    saveSeriesOrderData(ws.root, modPath, items)

    const toml = readFileSync(join(modPath, 'da', 'series.toml'), 'utf8')
    expect(toml).toContain('author = "Nobuo"')
    expect(toml).toContain('record-type = "arrange"')
    expect(toml).toContain('volume = 0.8')

    expect(loadSeriesOrderData(ws.root, modPath).items[0].fields).toMatchObject({
      defaultAuthor: 'Nobuo',
      defaultRecordType: 'arrange',
      defaultVolume: 0.8
    })
  })

  it('creates [default-track-data] when absent and a default is set', () => {
    const modPath = makeDir(ws, 'mymod')
    writeCustomSeries('mymod', 'da', '[series]\nid = "a"\nname = "A"\n')
    const data = loadSeriesOrderData(ws.root, modPath)
    const items = data.items.map((i) => ({ id: i.id, fields: { ...i.fields, defaultAuthor: 'Composer' } }))
    saveSeriesOrderData(ws.root, modPath, items)

    const toml = readFileSync(join(modPath, 'da', 'series.toml'), 'utf8')
    expect(toml).toContain('[default-track-data]')
    expect(toml).toContain('author = "Composer"')
  })

  it('leaves series.toml without [default-track-data] when no defaults are set', () => {
    const modPath = makeDir(ws, 'mymod')
    writeCustomSeries('mymod', 'da', '[series]\nid = "a"\nname = "A"\n')
    const data = loadSeriesOrderData(ws.root, modPath)
    const items = data.items.map((i) => ({ id: i.id, fields: { ...i.fields, name: 'A2' } }))
    saveSeriesOrderData(ws.root, modPath, items)

    const toml = readFileSync(join(modPath, 'da', 'series.toml'), 'utf8')
    expect(toml).not.toContain('[default-track-data]')
  })
})
