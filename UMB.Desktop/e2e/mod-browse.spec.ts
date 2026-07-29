import { test, expect, type ElectronApplication } from '@playwright/test'
import { mkdirSync, writeFileSync, existsSync, readFileSync, rmSync } from 'fs'
import { basename, join } from 'path'
import { createWorkspace, seedTestDataMod, launchApp, firstWindow, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication
let modPath: string

// Tests/TestData/configured-mod: 'dev' (13 tracks, 13 .flac sources) + 'mario' (6 tracks).
const DEV_TRACKS = 13
const MARIO_TRACKS = 6

test.beforeAll(async () => {
  ws = createWorkspace()
  modPath = seedTestDataMod(ws, 'test-mod')
  app = await launchApp(ws)
})

test.afterAll(async () => {
  await app?.close()
  ws?.cleanup()
})

// ── workspace / version plumbing ──

test('getWorkspace reports the UMB_WORKSPACE root the app was launched with', async () => {
  const page = await firstWindow(app)
  const workspace = await page.evaluate(() => window.electron.umb.getWorkspace())
  expect(workspace.replace(/\\/g, '/')).toContain(basename(ws.root))
})

test('getAppVersion reports "dev" when the app is not packaged', async () => {
  const page = await firstWindow(app)
  expect(await page.evaluate(() => window.electron.umb.getAppVersion())).toBe('dev')
})

// ── mod / series inspection (drives the app-bar picker and every view's series list) ──

test('listModSeries returns each series folder that has a tracks.csv', async () => {
  const page = await firstWindow(app)
  const series = await page.evaluate((mp) => window.electron.umb.listModSeries(mp), modPath)

  expect(series.map((s) => s.name).sort()).toEqual(['dev', 'mario'])
  for (const s of series) {
    expect(existsSync(join(s.path, 'tracks.csv'))).toBe(true)
  }
})

test('listModSeries skips songs-to-validate, dot-folders, and folders without a tracks.csv', async () => {
  mkdirSync(join(modPath, 'songs-to-validate'), { recursive: true })
  mkdirSync(join(modPath, '.hidden'), { recursive: true })
  writeFileSync(join(modPath, '.hidden', 'tracks.csv'), 'filename\n', 'utf-8')
  mkdirSync(join(modPath, 'no-csv-here'), { recursive: true })

  const page = await firstWindow(app)
  const series = await page.evaluate((mp) => window.electron.umb.listModSeries(mp), modPath)

  expect(series.map((s) => s.name).sort()).toEqual(['dev', 'mario'])

  rmSync(join(modPath, 'songs-to-validate'), { recursive: true, force: true })
  rmSync(join(modPath, '.hidden'), { recursive: true, force: true })
  rmSync(join(modPath, 'no-csv-here'), { recursive: true, force: true })
})

test('listModSeries refuses a path outside Mods/MusicMods', async () => {
  const page = await firstWindow(app)
  // Path-traversal guard: the renderer must not be able to enumerate arbitrary dirs.
  const escaped = await page.evaluate((root) => window.electron.umb.listModSeries(root), ws.root)
  expect(escaped).toEqual([])
})

test('getModStats counts series and tracks across the whole mod', async () => {
  const page = await firstWindow(app)
  const stats = await page.evaluate((mp) => window.electron.umb.getModStats(mp), modPath)

  expect(stats.seriesCount).toBe(2)
  expect(stats.trackCount).toBe(DEV_TRACKS + MARIO_TRACKS)
})

test('app bar shows the series and track counts for the active mod', async () => {
  const page = await firstWindow(app)
  // The active mod is auto-selected on load, so its stats must render, not the "—" placeholder.
  await expect(page.getByText('2 series')).toBeVisible({ timeout: 8000 })
  await expect(page.getByText(`${DEV_TRACKS + MARIO_TRACKS} tracks`)).toBeVisible({ timeout: 8000 })
})

// ── nus3 source listing / rejection (portable: no ffmpeg, no pymusiclooper) ──

test('listNus3Sources lists every convertible audio file in the series', async () => {
  const page = await firstWindow(app)
  const sources = await page.evaluate(
    (sp) => window.electron.umb.listNus3Sources(sp),
    join(modPath, 'dev')
  )

  expect(sources.length).toBe(DEV_TRACKS)
  // .txt/.png/.toml siblings must not be offered for conversion.
  for (const s of sources) {
    expect(s.src.toLowerCase()).toMatch(/\.(mp3|wav|flac|ogg)$/)
    expect(s.converted).toBe(false)
  }
})

test('listNus3Sources marks a source converted once its nus3audio sits in songs-to-validate', async () => {
  const seriesPath = join(modPath, 'dev')
  const validateDir = join(seriesPath, 'songs-to-validate')
  const page = await firstWindow(app)

  const before = await page.evaluate((sp) => window.electron.umb.listNus3Sources(sp), seriesPath)
  const target = before[0]

  mkdirSync(validateDir, { recursive: true })
  writeFileSync(join(validateDir, target.src.replace(/\.[^.]+$/, '.nus3audio')), 'stub', 'utf-8')

  const after = await page.evaluate((sp) => window.electron.umb.listNus3Sources(sp), seriesPath)
  expect(after.find((s) => s.src === target.src)?.converted).toBe(true)
  // The others are untouched.
  expect(after.filter((s) => s.converted).length).toBe(1)

  rmSync(validateDir, { recursive: true, force: true })
})

test('listNus3Sources hides a source already accepted into the series folder', async () => {
  const seriesPath = join(modPath, 'dev')
  const page = await firstWindow(app)

  const before = await page.evaluate((sp) => window.electron.umb.listNus3Sources(sp), seriesPath)
  const target = before[0]
  const accepted = join(seriesPath, target.src.replace(/\.[^.]+$/, '.nus3audio'))
  writeFileSync(accepted, 'stub', 'utf-8')

  const after = await page.evaluate((sp) => window.electron.umb.listNus3Sources(sp), seriesPath)
  expect(after.map((s) => s.src)).not.toContain(target.src)
  expect(after.length).toBe(DEV_TRACKS - 1)

  rmSync(accepted, { force: true })
})

test('listNus3Sources returns empty for a series path that does not exist', async () => {
  const page = await firstWindow(app)
  const sources = await page.evaluate(
    (sp) => window.electron.umb.listNus3Sources(sp),
    join(modPath, 'does-not-exist')
  )
  expect(sources).toEqual([])
})

test('loadNus3Conversions returns empty when nothing has been converted', async () => {
  const page = await firstWindow(app)
  const conversions = await page.evaluate(
    (sp) => window.electron.umb.loadNus3Conversions(sp),
    join(modPath, 'mario')
  )
  expect(conversions).toEqual({})
})

test('rejectNus3Track deletes the nus3audio and drops only its conversion entry', async () => {
  const seriesPath = join(modPath, 'dev')
  const validateDir = join(seriesPath, 'songs-to-validate')
  const page = await firstWindow(app)

  const sources = await page.evaluate((sp) => window.electron.umb.listNus3Sources(sp), seriesPath)
  const reject = sources[0]
  const keep = sources[1]
  const nus3Of = (src: string): string => join(validateDir, src.replace(/\.[^.]+$/, '.nus3audio'))

  mkdirSync(validateDir, { recursive: true })
  writeFileSync(nus3Of(reject.src), 'stub', 'utf-8')
  writeFileSync(nus3Of(keep.src), 'stub', 'utf-8')
  writeFileSync(
    join(validateDir, '.conversions.json'),
    JSON.stringify({ [reject.id]: { mode: 'end-to-end' }, [keep.id]: { mode: 'loop' } }),
    'utf-8'
  )

  await page.evaluate(
    ([sp, id]) => window.electron.umb.rejectNus3Track(sp, id),
    [seriesPath, reject.id] as const
  )

  expect(existsSync(nus3Of(reject.src))).toBe(false)
  expect(existsSync(nus3Of(keep.src))).toBe(true)

  const conversions = JSON.parse(readFileSync(join(validateDir, '.conversions.json'), 'utf-8'))
  expect(reject.id in conversions).toBe(false)
  expect(conversions[keep.id]).toEqual({ mode: 'loop' })

  const viaIpc = await page.evaluate((sp) => window.electron.umb.loadNus3Conversions(sp), seriesPath)
  expect(Object.keys(viaIpc)).toEqual([keep.id])

  rmSync(validateDir, { recursive: true, force: true })
})

test('rejectNus3Track is a no-op when the track was never converted', async () => {
  const seriesPath = join(modPath, 'mario')
  const page = await firstWindow(app)

  await page.evaluate(
    ([sp, id]) => window.electron.umb.rejectNus3Track(sp, id),
    [seriesPath, 'never-existed.flac'] as const
  )

  expect(existsSync(join(seriesPath, 'songs-to-validate', '.conversions.json'))).toBe(false)
})

// ── cancel ──

test('cancelAction is safe when no CLI action is running', async () => {
  const page = await firstWindow(app)
  await page.evaluate(() => window.electron.umb.cancelAction())
  // The bridge must still be alive afterwards.
  expect((await page.evaluate(() => window.electron.umb.debugPing())).ok).toBe(true)
})
