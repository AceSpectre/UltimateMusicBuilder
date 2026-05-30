import { test, expect, type ElectronApplication } from '@playwright/test'
import { readFileSync } from 'fs'
import { join } from 'path'
import { createWorkspace, seedMod, launchApp, firstWindow, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication
let seriesPath: string

const TRACKS_CSV =
  'filename,title,game\n' +
  'Mass Destruction.flac,Mass Destruction,Persona 3\n' +
  'reach-out.flac,Reach Out to the Truth,Persona 4\n' +
  'last-surprise.flac,Last Surprise,Persona 5\n'

test.beforeAll(async () => {
  ws = createWorkspace()
  seriesPath = seedMod(ws, 'persona', 'persona', {
    tracksCsv: TRACKS_CSV,
    seriesToml: 'existing-series = true\n'
  })
  app = await launchApp(ws)
})

test.afterAll(async () => {
  await app?.close()
  ws?.cleanup()
})

test('loadTrackOrder returns items from seeded CSV', async () => {
  const page = await firstWindow(app)

  const data = await page.evaluate(
    (sp) => window.electron.umb.loadTrackOrder(sp),
    seriesPath
  )

  expect(data.items).toHaveLength(3)
  expect(data.items[0].title).toBe('Mass Destruction')
  expect(data.items[1].title).toBe('Reach Out to the Truth')
  expect(data.items[2].title).toBe('Last Surprise')
  expect(data.isExistingSeries).toBe(true)
  expect(data.hasSongOrder).toBe(false)
})

test('saveTrackOrder rewrites tracks.csv with new order', async () => {
  const page = await firstWindow(app)

  const saved = await page.evaluate(
    (sp) => window.electron.umb.saveTrackOrder(sp, ['mod:2', 'mod:0', 'mod:1']),
    seriesPath
  )

  expect(saved.items.map((i: { title: string }) => i.title)).toEqual([
    'Last Surprise',
    'Mass Destruction',
    'Reach Out to the Truth'
  ])

  const csv = readFileSync(join(seriesPath, 'tracks.csv'), 'utf8')
  expect(csv).toContain('order')
})

test('saveTrackOrder writes song_order.toml for existing-series', async () => {
  const toml = readFileSync(join(seriesPath, 'song_order.toml'), 'utf8')
  expect(toml).toContain('song_order = [')
  expect(toml).toContain('ui_bgm_last_surprise')
  expect(toml).toContain('ui_bgm_mass_destruction')
})

test('order tracks UI shows series list after clicking Order Tracks', async () => {
  const page = await firstWindow(app)

  await page.getByText('Order Tracks').first().click()
  await expect(page.getByRole('heading', { name: 'persona' })).toBeVisible({ timeout: 5000 })
})
