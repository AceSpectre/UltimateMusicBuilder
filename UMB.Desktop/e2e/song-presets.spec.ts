import { test, expect, type ElectronApplication } from '@playwright/test'
import { readFileSync } from 'fs'
import { join } from 'path'
import { createWorkspace, seedMod, launchApp, firstWindow, closeApp, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication
let seriesPath: string

test.beforeAll(async () => {
  ws = createWorkspace()
  seriesPath = seedMod(ws, 'preset-mod', 'demo', {
    tracksCsv: 'filename,game,title,author,copyright,record_type,volume,info1,in_soundtest\nsong.nus3audio,g2,Song,Old author,,original,0.75,,True\n',
    seriesToml: '[series]\nid = "demo"\nname = "Demo"\n\n[[games]]\nid = "g1"\nname = "One"\n\n[[games]]\nid = "g2"\nname = "Two"\n\n[default-track-data]\ngame = "g1"\nauthor = "Series author"\nrecord-type = "original"\nvolume = 0.8\n'
  })
  app = await launchApp(ws)
})
test.afterAll(async () => { await closeApp(app); ws?.cleanup() })

test('edits multiple game presets, applies one to a song, and restores series fallback', async () => {
  const page = await firstWindow(app)
  await page.getByText('Manage Songs').first().click()
  await page.getByRole('button', { name: 'demo' }).first().click()
  await page.getByRole('button', { name: 'Song Presets', exact: true }).click()
  const scope = page.getByLabel('Edit defaults for')
  const fallback = page.getByLabel('Use series defaults for this game')

  for (const [game, author] of [['g1', 'One author'], ['g2', 'Two author']]) {
    await scope.selectOption(game)
    await fallback.uncheck()
    await page.getByLabel('Default author', { exact: true }).fill(author)
    await page.getByLabel('Volume multiplier').fill('1.4')
  }
  await page.getByRole('button', { name: 'Save Presets', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Song Presets' })).toHaveCount(0)
  expect(readFileSync(join(seriesPath, 'series.toml'), 'utf8')).toContain('Two author')

  await page.getByLabel('Title', { exact: true }).first().click()
  await page.getByRole('button', { name: 'Use Default Values' }).click()
  await expect(page.getByLabel('Author', { exact: true }).first()).toHaveValue('Two author')
  await page.getByRole('button', { name: 'Save Changes' }).click()
  await expect(page.getByRole('button', { name: 'Saved', exact: true })).toBeVisible()
  const applied = await page.evaluate((path) => window.electron.umb.loadTrackOrder(path), seriesPath)
  expect(applied.items[0].fields?.volume).toBeCloseTo(1.4)
  expect(applied.items[0].fields?.game).toBe('g2')

  await page.getByRole('button', { name: 'Song Presets', exact: true }).click()
  await scope.selectOption('g2')
  await fallback.check()
  await page.getByRole('button', { name: 'Save Presets', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Song Presets' })).toHaveCount(0)
  await page.getByLabel('Title', { exact: true }).first().click()
  await page.getByRole('button', { name: 'Use Default Values' }).click()
  await expect(page.getByLabel('Author', { exact: true }).first()).toHaveValue('Series author')
  await expect(page.getByLabel('Game', { exact: true }).first()).toHaveValue('g2')

  await page.getByLabel('Preset to apply').selectOption('series')
  await page.getByRole('button', { name: 'Use Default Values' }).click()
  await expect(page.getByLabel('Game', { exact: true }).first()).toHaveValue('g1')
})
