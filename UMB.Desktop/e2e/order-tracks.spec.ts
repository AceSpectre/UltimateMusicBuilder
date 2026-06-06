import { test, expect, type ElectronApplication } from '@playwright/test'
import { readFileSync, writeFileSync } from 'fs'
import { join } from 'path'
import { createWorkspace, seedTestDataMod, launchApp, firstWindow, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication
let modDir: string

test.beforeAll(async () => {
  ws = createWorkspace()
  modDir = seedTestDataMod(ws, 'test-mod')
  app = await launchApp(ws)
})
test.afterAll(async () => { await app?.close(); ws?.cleanup() })

const devPath = () => join(modDir, 'dev')
const marioPath = () => join(modDir, 'mario')

test('loads all 13 custom-series tracks, not an existing series', async () => {
  const page = await firstWindow(app)
  const data = await page.evaluate((sp) => window.electron.umb.loadTrackOrder(sp), devPath())
  expect(data.items).toHaveLength(13)
  expect(data.isExistingSeries).toBe(false)
  expect(data.hasSongOrder).toBe(false)
  expect(data.items[0].title).toBe('KARTS!')
})

test('reversing the custom series rewrites tracks.csv with an order column', async () => {
  const page = await firstWindow(app)
  const loaded = await page.evaluate((sp) => window.electron.umb.loadTrackOrder(sp), devPath())
  const reversed = loaded.items.map((i) => i.id).reverse()

  const saved = await page.evaluate(
    ([sp, ids]) => window.electron.umb.saveTrackOrder(sp as string, ids as string[]),
    [devPath(), reversed] as const
  )
  expect(saved.items[0].title).toBe('Time Trials')
  expect(saved.items[12].title).toBe('KARTS!')

  const csv = readFileSync(join(devPath(), 'tracks.csv'), 'utf8')
  expect(csv.split(/\r?\n/)[0]).toContain('order')
})

test('existing series writes song_order.toml with derived bgmIds matching the CLI', async () => {
  const page = await firstWindow(app)
  const loaded = await page.evaluate((sp) => window.electron.umb.loadTrackOrder(sp), marioPath())
  expect(loaded.isExistingSeries).toBe(true)
  expect(loaded.items).toHaveLength(6)

  await page.evaluate(
    ([sp, ids]) => window.electron.umb.saveTrackOrder(sp as string, ids as string[]),
    [marioPath(), loaded.items.map((i) => i.id)] as const
  )

  const toml = readFileSync(join(marioPath(), 'song_order.toml'), 'utf8')
  expect(toml).toContain('song_order = [')
  // Tone-id derivation parity with C# (BaselineGenerator.WriteMarioSongOrderToml).
  expect(toml).toContain('ui_bgm_flowerhead___somewhat_good__lofi___01_summer')
  expect(toml).toContain('ui_bgm_flowerhead___somewhat_good__lofi___03_brain_empty')
})

test('pre-existing song_order.toml is loaded with vanilla entries locked', async () => {
  // Mirrors OrderingTests.TrackOrder_ExistingSeriesRespectsSongOrderToml: interleaved vanilla ps01.
  const page = await firstWindow(app)
  const songOrder =
    'song_order = [\n' +
    '  "ui_bgm_flowerhead___somewhat_good__lofi___03_brain_empty",\n' +
    '  "ui_bgm_ps01",\n' +
    '  "ui_bgm_flowerhead___somewhat_good__lofi___01_summer",\n' +
    ']\n'
  writeFileSync(join(marioPath(), 'song_order.toml'), songOrder, 'utf8')

  const data = await page.evaluate((sp) => window.electron.umb.loadTrackOrder(sp), marioPath())
  expect(data.hasSongOrder).toBe(true)
  expect(data.items[0].bgmId).toBe('ui_bgm_flowerhead___somewhat_good__lofi___03_brain_empty')
  const vanilla = data.items.find((i) => i.bgmId === 'ui_bgm_ps01')
  expect(vanilla?.isLocked).toBe(true)
})

test('UI smoke: Order Tracks shows the series list', async () => {
  const page = await firstWindow(app)
  await page.getByText('Order Tracks').first().click()
  await expect(page.getByRole('heading', { name: 'test-mod' }).or(page.getByText('dev')).first()).toBeVisible({ timeout: 5000 })
})
