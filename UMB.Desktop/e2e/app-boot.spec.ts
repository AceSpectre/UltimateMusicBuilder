import { test, expect, type ElectronApplication } from '@playwright/test'
import { createWorkspace, seedTestDataMod, launchApp, firstWindow, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication

test.beforeAll(async () => {
  ws = createWorkspace()
  seedTestDataMod(ws, 'test-mod')
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

test('listMods returns the seeded TestData mod', async () => {
  const page = await firstWindow(app)
  const mods = await page.evaluate(() => window.electron.umb.listMods())
  expect(mods.map((m: { name: string }) => m.name)).toContain('test-mod')
})

test('app bar displays brand text', async () => {
  const page = await firstWindow(app)
  await expect(page.getByText('Ultimate Music Builder')).toBeVisible()
})

test('sidebar renders action labels', async () => {
  const page = await firstWindow(app)
  await expect(page.getByRole('navigation').getByText('Build', { exact: true })).toBeVisible()
  await expect(page.getByText('Manage Songs')).toBeVisible()
})
