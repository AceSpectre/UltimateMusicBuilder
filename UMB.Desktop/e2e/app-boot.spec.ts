import { test, expect, type ElectronApplication } from '@playwright/test'
import { existsSync } from 'fs'
import { createWorkspace, seedTestDataMod, launchApp, firstWindow, closeApp, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication

test.beforeAll(async () => {
  ws = createWorkspace()
  seedTestDataMod(ws, 'test-mod')
  app = await launchApp(ws)
})

test.afterAll(async () => {
  await closeApp(app)
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

test('closeApp cleans up an app already terminated by kill', async () => {
  const terminated = await launchApp(ws)
  const profile = await terminated.evaluate(({ app }) => app.getPath('userData'))
  expect(existsSync(profile)).toBe(true)
  const child = terminated.process()
  const exited = new Promise<void>(resolveExit => child.once('exit', () => resolveExit()))
  child.kill('SIGKILL')
  await exited
  await closeApp(terminated)
  expect(existsSync(profile)).toBe(false)
})
