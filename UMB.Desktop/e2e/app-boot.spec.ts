import { test, expect, type ElectronApplication } from '@playwright/test'
import { createWorkspace, seedMod, launchApp, firstWindow, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication

const TRACKS_CSV =
  'filename,title,game\n' +
  'Mass Destruction.flac,Mass Destruction,Persona 3\n' +
  'reach-out.flac,Reach Out to the Truth,Persona 4\n'

test.beforeAll(async () => {
  ws = createWorkspace()
  seedMod(ws, 'persona', 'persona', {
    tracksCsv: TRACKS_CSV,
    seriesToml: 'existing-series = true\n'
  })
  seedMod(ws, 'mario', 'mushroom-kingdom', {
    tracksCsv: 'filename,title,game\nground.flac,Ground Theme,Super Mario Bros.\n'
  })
  app = await launchApp(ws)
})

test.afterAll(async () => {
  await app?.close()
  ws?.cleanup()
})

test('window opens and has a title', async () => {
  const page = await firstWindow(app)
  const title = await page.title()
  expect(title).toBeTruthy()
})

test('debug ping returns ok with correct workspace', async () => {
  const page = await firstWindow(app)

  const result = await page.evaluate(() => window.electron.umb.debugPing())
  expect(result.ok).toBe(true)
  expect(result.workspace).toBeTruthy()
})

test('listMods returns seeded mod directories', async () => {
  const page = await firstWindow(app)

  const mods = await page.evaluate(() => window.electron.umb.listMods())
  const names = mods.map((m: { name: string }) => m.name).sort()
  expect(names).toEqual(['mario', 'persona'])
})

test('app bar displays brand text', async () => {
  const page = await firstWindow(app)
  await expect(page.getByText('Ultimate Music Builder')).toBeVisible()
})

test('sidebar renders action labels', async () => {
  const page = await firstWindow(app)
  await expect(page.getByText('Build', { exact: true })).toBeVisible()
  await expect(page.getByText('Order Tracks')).toBeVisible()
})
