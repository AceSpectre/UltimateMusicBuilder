import { test, expect, type ElectronApplication } from '@playwright/test'
import { readFileSync } from 'fs'
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

test('UI: a preset saved in Manage Presets is written to series.toml and applied in Manage Songs', async () => {
  const page = await firstWindow(app)
  await page.getByText('Manage Presets').first().click()
  await page.getByRole('button', { name: 'dev' }).first().click()

  await page.getByRole('button', { name: 'Add Preset' }).click()
  await page.getByLabel('Preset', { exact: true }).last().fill('Remix')
  await page.getByLabel('Author').last().fill('Preset Author')
  await page.getByLabel('Record Type').last().selectOption('arrange')
  await page.getByRole('button', { name: 'Save Changes' }).click()
  await expect(page.getByRole('button', { name: 'Saved' })).toBeVisible({ timeout: 5000 })

  const toml = readFileSync(join(modDir, 'dev', 'series.toml'), 'utf8')
  expect(toml).toContain('[[presets]]\nname = "Remix"\nauthor = "Preset Author"\nrecord-type = "arrange"')

  await page.getByText('Manage Songs').first().click()
  await page.getByLabel('Title').first().click()
  await page.getByLabel('Apply Preset').selectOption({ label: 'Remix' })
  await expect(page.getByLabel('Author').first()).toHaveValue('Preset Author')
  await expect(page.getByLabel('Record', { exact: true }).first()).toHaveValue('arrange')
})
