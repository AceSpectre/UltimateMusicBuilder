import { test, expect, type ElectronApplication } from '@playwright/test'
import { mkdirSync, writeFileSync, existsSync, readFileSync } from 'fs'
import { join } from 'path'
import { createWorkspace, seedTestDataMod, launchApp, firstWindow, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication
let modA: string
let modB: string

test.beforeAll(async () => {
  ws = createWorkspace()
  modA = seedTestDataMod(ws, 'mod-a') // has dev, mario, series-order.toml=["dev"]
  // mod-b: a conflicting "dev" series + a unique "custom" series + series-order=["custom","dev"]
  modB = join(ws.root, 'Mods', 'MusicMods', 'mod-b')
  const bDev = join(modB, 'dev')
  mkdirSync(bDev, { recursive: true })
  writeFileSync(join(bDev, 'series.toml'),
    '[series]\nid = "dev"\nname = "Dev B"\n\n[[games]]\nid = "dev_b"\nname = "Dev B"\n', 'utf8')
  writeFileSync(join(bDev, 'tracks.csv'),
    'filename,game,title,author,copyright,record_type,special_category,volume,info1,in_soundtest\n' +
    'b-only.flac,dev_b,B Only,,,original,,1,,True\n', 'utf8')
  writeFileSync(join(bDev, 'b-only.flac'), 'B')
  const bCustom = join(modB, 'custom')
  mkdirSync(bCustom, { recursive: true })
  writeFileSync(join(bCustom, 'series.toml'), '[series]\nid = "custom"\nname = "Custom"\n', 'utf8')
  writeFileSync(join(bCustom, 'tracks.csv'), 'filename,title,game\nc.flac,C,Custom\n', 'utf8')
  writeFileSync(join(modB, 'series-order.toml'), 'order = [\n    "custom",\n    "dev",\n]\n', 'utf8')
  app = await launchApp(ws)
})
test.afterAll(async () => { await app?.close(); ws?.cleanup() })

test('analyzeMerge flags the dev conflict and lists all series', async () => {
  const page = await firstWindow(app)
  const analysis = await page.evaluate(
    (mods) => window.electron.umb.analyzeMerge(mods),
    [modA, modB]
  )
  expect(analysis.series.map((s) => s.name).sort()).toEqual(['custom', 'dev', 'mario'])
  expect(analysis.conflicts.map((c) => c.seriesName)).toContain('dev')
})

test('executeMerge produces a merged mod with all series and resolved conflict', async () => {
  const page = await firstWindow(app)
  const result = await page.evaluate(
    ([mods, name, priority]) =>
      window.electron.umb.executeMerge(mods as string[], name as string, priority as string),
    [[modA, modB], 'merged', modA] as const
  )
  expect(result.totalSeries).toBe(3)
  expect(result.conflictsResolved).toBe(1)

  const out = result.outputPath
  expect(existsSync(join(out, 'dev', 'tracks.csv'))).toBe(true)
  expect(existsSync(join(out, 'mario', 'tracks.csv'))).toBe(true)
  expect(existsSync(join(out, 'custom', 'series.toml'))).toBe(true)

  // Conflict resolution: merged dev must union both mods' tracks, with mod-a metadata winning.
  const mergedDevCsv = readFileSync(join(out, 'dev', 'tracks.csv'), 'utf8')
  expect(mergedDevCsv).toContain('KARTS!')      // a mod-a track
  expect(mergedDevCsv).toContain('b-only.flac') // mod-b's unique track
  const mergedDevToml = readFileSync(join(out, 'dev', 'series.toml'), 'utf8')
  expect(mergedDevToml).toContain('Somewhat Good: Karts') // priority mod-a name, not "Dev B"

  // series-order merged, priority A first, deduped → dev, custom
  const order = [...readFileSync(join(out, 'series-order.toml'), 'utf8').matchAll(/^\s+"([^"]+)",/gm)].map((m) => m[1])
  expect(order).toEqual(['dev', 'custom'])
})

test('validateMergeName rejects an existing name', async () => {
  const page = await firstWindow(app)
  const err = await page.evaluate(() => window.electron.umb.validateMergeName('mod-a'))
  expect(err).toBe('A mod with that name already exists.')
})

test('UI smoke: Merge view opens', async () => {
  const page = await firstWindow(app)
  await page.getByText('Merge', { exact: true }).first().click()
  await expect(page.getByText('mod-a').or(page.getByText('mod-b')).first()).toBeVisible({ timeout: 5000 })
})
