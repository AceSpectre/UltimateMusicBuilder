import { test, expect, type ElectronApplication } from '@playwright/test'
import { readFileSync, writeFileSync } from 'fs'
import { join } from 'path'
import { createWorkspace, seedTestDataMod, launchApp, firstWindow, closeApp, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication
let modDir: string

test.beforeAll(async () => {
  ws = createWorkspace()
  modDir = seedTestDataMod(ws, 'test-mod')
  app = await launchApp(ws)
})
test.afterAll(async () => { await closeApp(app); ws?.cleanup() })

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
  const reversed = loaded.items.map((i) => ({ id: i.id, fields: i.fields })).reverse()

  const saved = await page.evaluate(
    ([sp, items]) => window.electron.umb.saveTrackOrder(sp as string, items as never),
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
    ([sp, items]) => window.electron.umb.saveTrackOrder(sp as string, items as never),
    [marioPath(), loaded.items.map((i) => ({ id: i.id, fields: i.fields }))] as const
  )

  const toml = readFileSync(join(marioPath(), 'song_order.toml'), 'utf8')
  expect(toml).toContain('song_order = [')
  expect(toml).toContain('ui_bgm_flowerhead___somewhat_good__lofi___01_summer')
  expect(toml).toContain('ui_bgm_flowerhead___somewhat_good__lofi___03_brain_empty')
})

test('pre-existing song_order.toml is loaded with vanilla entries locked', async () => {
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

test('editing a track field via saveTrackOrder persists to tracks.csv', async () => {
  const page = await firstWindow(app)
  const loaded = await page.evaluate((sp) => window.electron.umb.loadTrackOrder(sp), devPath())
  const items = loaded.items.map((i) => ({ id: i.id, fields: i.fields }))
  items[0].fields!.title = 'Edited Title E2E'

  const saved = await page.evaluate(
    ([sp, payload]) => window.electron.umb.saveTrackOrder(sp as string, payload as never),
    [devPath(), items] as const
  )
  expect(saved.items.some((i) => i.fields?.title === 'Edited Title E2E')).toBe(true)

  const csv = readFileSync(join(devPath(), 'tracks.csv'), 'utf8')
  expect(csv).toContain('Edited Title E2E')
})

test('UI: editing a title in the table and clicking Save persists to tracks.csv', async () => {
  const page = await firstWindow(app)
  await page.getByText('Manage Songs').first().click()
  await page.getByRole('button', { name: 'dev' }).first().click()

  const titleInput = page.getByLabel('Title').first()
  await titleInput.waitFor({ state: 'visible', timeout: 5000 })
  await titleInput.fill('DOM Edited Title')

  await page.getByRole('button', { name: 'Save Changes' }).click()
  await expect(page.getByRole('button', { name: 'Saved' })).toBeVisible({ timeout: 5000 })

  const csv = readFileSync(join(devPath(), 'tracks.csv'), 'utf8')
  expect(csv).toContain('DOM Edited Title')
})

test('UI: Use Default Values enables only after selecting a custom song', async () => {
  const page = await firstWindow(app)
  await page.getByText('Manage Songs').first().click()
  await page.getByRole('button', { name: 'mario' }).first().click()
  const useDefaults = page.getByRole('button', { name: 'Use Default Values' })
  await expect(useDefaults).toBeDisabled()

  await page.getByLabel('Title').first().click()
  await expect(useDefaults).toBeEnabled()
})
