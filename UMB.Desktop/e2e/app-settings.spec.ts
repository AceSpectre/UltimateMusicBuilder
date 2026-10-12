import { test, expect, type ElectronApplication } from '@playwright/test'
import { mkdirSync, writeFileSync, readFileSync, existsSync } from 'fs'
import { join } from 'path'
import { createWorkspace, seedTestDataMod, launchApp, firstWindow, closeApp, type E2EWorkspace } from './e2e-utils'

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
    GlobalVolumeDb: 2.25,
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
  await closeApp(app)
  ws?.cleanup()
})

test.beforeEach(() => {
  writeSettings(BASE_SETTINGS)
})

test('settings modal loads the current global dB boost, saves the edit, and closes', async () => {
  const page = await firstWindow(app)

  await page.getByTitle('Settings').click()

  const input = page.locator('#global-volume')
  await expect(input).toBeVisible()
  await expect(input).toHaveValue('2.25')

  await input.fill('0.6')
  await page.getByRole('button', { name: 'Save' }).click()

  // closes ~600ms after save
  await expect(input).toBeHidden({ timeout: 8000 })
  expect(readSettings().Sma5hMusic.GlobalVolumeDb).toBe(0.6)
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
  expect(readSettings().Sma5hMusic.GlobalVolumeDb).toBe(2.25)
})
