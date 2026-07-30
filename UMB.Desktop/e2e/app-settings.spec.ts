import { test, expect, type ElectronApplication } from '@playwright/test'
import { mkdirSync, writeFileSync, readFileSync, rmSync, existsSync } from 'fs'
import { join } from 'path'
import { createWorkspace, seedTestDataMod, launchApp, firstWindow, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication

/** Where app-settings.ts resolves appsettings.json when unpackaged. */
function settingsPath(): string {
  return join(ws.root, 'UMB.CLI', 'bin', 'Debug', 'net8.0', 'appsettings.json')
}

function writeSettings(obj: unknown): void {
  const path = settingsPath()
  mkdirSync(join(path, '..'), { recursive: true })
  writeFileSync(path, JSON.stringify(obj, null, 2), 'utf-8')
}

function readSettings(): Record<string, any> {
  return JSON.parse(readFileSync(settingsPath(), 'utf-8'))
}

const BASE_SETTINGS = {
  OutputPath: 'ArcOutput',
  ModPath: 'Mods/MusicMods',
  Sma5hMusic: {
    GlobalVolumeMultiplier: 2.25,
    CachePath: 'Cache'
  }
}

test.beforeAll(async () => {
  ws = createWorkspace()
  seedTestDataMod(ws, 'test-mod')
  writeSettings(BASE_SETTINGS)
  app = await launchApp(ws)
})

test.afterAll(async () => {
  await app?.close()
  ws?.cleanup()
})

test.beforeEach(() => {
  writeSettings(BASE_SETTINGS)
})

test('getAppSettings reads GlobalVolumeMultiplier out of appsettings.json', async () => {
  const page = await firstWindow(app)
  const settings = await page.evaluate(() => window.electron.umb.getAppSettings())
  expect(settings.globalVolumeMultiplier).toBe(2.25)
})

test('getAppSettings falls back to the 1.5 default when appsettings.json is absent', async () => {
  rmSync(settingsPath(), { force: true })
  const page = await firstWindow(app)
  const settings = await page.evaluate(() => window.electron.umb.getAppSettings())
  expect(settings.globalVolumeMultiplier).toBe(1.5)
})

test('getAppSettings falls back to the default when GlobalVolumeMultiplier is missing', async () => {
  writeSettings({ OutputPath: 'ArcOutput', Sma5hMusic: { CachePath: 'Cache' } })
  const page = await firstWindow(app)
  const settings = await page.evaluate(() => window.electron.umb.getAppSettings())
  expect(settings.globalVolumeMultiplier).toBe(1.5)
})

test('saveAppSettings rewrites the multiplier and preserves every other key', async () => {
  const page = await firstWindow(app)
  await page.evaluate(() => window.electron.umb.saveAppSettings({ globalVolumeMultiplier: 0.8 }))

  const raw = readSettings()
  expect(raw.Sma5hMusic.GlobalVolumeMultiplier).toBe(0.8)
  expect(raw.OutputPath).toBe('ArcOutput')
  expect(raw.ModPath).toBe('Mods/MusicMods')
  expect(raw.Sma5hMusic.CachePath).toBe('Cache')

  const reread = await page.evaluate(() => window.electron.umb.getAppSettings())
  expect(reread.globalVolumeMultiplier).toBe(0.8)
})

test('saveAppSettings creates the Sma5hMusic section when it does not exist', async () => {
  writeSettings({ OutputPath: 'ArcOutput' })
  const page = await firstWindow(app)
  await page.evaluate(() => window.electron.umb.saveAppSettings({ globalVolumeMultiplier: 3 }))

  expect(readSettings().Sma5hMusic.GlobalVolumeMultiplier).toBe(3)
})

test('checkArcOutput is false when the output dir is missing, then empty, and true once populated', async () => {
  const page = await firstWindow(app)
  const arcOutput = join(ws.root, 'ArcOutput')

  rmSync(arcOutput, { recursive: true, force: true })
  expect(await page.evaluate(() => window.electron.umb.checkArcOutput())).toBe(false)

  mkdirSync(arcOutput, { recursive: true })
  expect(await page.evaluate(() => window.electron.umb.checkArcOutput())).toBe(false)

  writeFileSync(join(arcOutput, 'ui_bgm_db.prc'), 'stub', 'utf-8')
  expect(await page.evaluate(() => window.electron.umb.checkArcOutput())).toBe(true)

  rmSync(arcOutput, { recursive: true, force: true })
})

test('checkArcOutput honours a custom OutputPath from appsettings.json', async () => {
  writeSettings({ ...BASE_SETTINGS, OutputPath: 'CustomOut' })
  const page = await firstWindow(app)

  const customOut = join(ws.root, 'CustomOut')
  expect(await page.evaluate(() => window.electron.umb.checkArcOutput())).toBe(false)

  mkdirSync(customOut, { recursive: true })
  writeFileSync(join(customOut, 'anything.prc'), 'stub', 'utf-8')
  expect(await page.evaluate(() => window.electron.umb.checkArcOutput())).toBe(true)

  rmSync(customOut, { recursive: true, force: true })
})

test('checkArcOutput is false when appsettings.json is unreadable', async () => {
  writeFileSync(settingsPath(), '{ not json', 'utf-8')
  const page = await firstWindow(app)
  expect(await page.evaluate(() => window.electron.umb.checkArcOutput())).toBe(false)
})

test('settings modal loads the current multiplier, saves the edit, and closes', async () => {
  const page = await firstWindow(app)

  await page.getByTitle('Settings').click()

  const input = page.locator('#global-volume')
  await expect(input).toBeVisible()
  await expect(input).toHaveValue('2.25')

  await input.fill('0.6')
  await page.getByRole('button', { name: 'Save' }).click()

  // closes ~600ms after save
  await expect(input).toBeHidden({ timeout: 8000 })
  expect(readSettings().Sma5hMusic.GlobalVolumeMultiplier).toBe(0.6)
})

test('settings modal cancel discards the edit', async () => {
  const page = await firstWindow(app)

  await page.getByTitle('Settings').click()
  const input = page.locator('#global-volume')
  await expect(input).toBeVisible()

  await input.fill('9.9')
  await page.getByRole('button', { name: 'Cancel' }).click()

  await expect(input).toBeHidden()
  expect(existsSync(settingsPath())).toBe(true)
  expect(readSettings().Sma5hMusic.GlobalVolumeMultiplier).toBe(2.25)
})
