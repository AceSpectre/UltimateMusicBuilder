import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { readFileSync } from 'fs'
import { join } from 'path'
import { loadSeriesOrderData, saveSeriesOrderData } from './order-series'
import { makeWorkspace, makeDir, writeSeries, writeFile, type Workspace } from './test-utils'

let ws: Workspace

beforeEach(() => {
  ws = makeWorkspace()
})

afterEach(() => {
  ws.cleanup()
})

const DUMMY_CSV = 'filename,title,game\nx.flac,X,Game\n'

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
  })
})

describe('saveSeriesOrderData', () => {
  it('writes series-order.toml in the requested order and appends omitted series', () => {
    const modPath = makeDir(ws, 'mymod')
    writeCustomSeries('mymod', 'da', 'id = "a"\nname = "A"\n')
    writeCustomSeries('mymod', 'db', 'id = "b"\nname = "B"\n')
    writeCustomSeries('mymod', 'dc', 'id = "c"\nname = "C"\n')

    // Default load sorts by name: series:0=a, series:1=b, series:2=c.
    const result = saveSeriesOrderData(ws.root, modPath, ['series:2', 'series:0'])

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
