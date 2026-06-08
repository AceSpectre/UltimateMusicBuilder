import { test, expect, type ElectronApplication } from '@playwright/test'
import { mkdirSync, writeFileSync, readFileSync } from 'fs'
import { join } from 'path'
import { createWorkspace, seedTestDataMod, launchApp, firstWindow, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication
let modDir: string

test.beforeAll(async () => {
  ws = createWorkspace()
  modDir = seedTestDataMod(ws, 'test-mod')
  // Add a second custom series so series ordering has two entries.
  const gamma = join(modDir, 'gamma')
  mkdirSync(gamma, { recursive: true })
  writeFileSync(join(gamma, 'series.toml'), '[series]\nid = "gamma"\nname = "Gamma"\n', 'utf8')
  writeFileSync(join(gamma, 'tracks.csv'), 'filename,title,game\ng.flac,G,Gamma\n', 'utf8')
  app = await launchApp(ws)
})
test.afterAll(async () => { await app?.close(); ws?.cleanup() })

test('loadSeriesOrder lists custom series; dev pre-ordered by series-order.toml', async () => {
  const page = await firstWindow(app)
  const data = await page.evaluate((mp) => window.electron.umb.loadSeriesOrder(mp), modDir)
  const ids = data.items.map((i) => i.seriesId).sort()
  expect(ids).toEqual(['dev', 'gamma'])
  expect(data.hasSeriesOrder).toBe(true) // configured-mod ships series-order.toml = ["dev"]
  // series-order.toml = ["dev"] must be honoured end-to-end: dev before the unlisted gamma.
  expect(data.items.map((i) => i.seriesId)).toEqual(['dev', 'gamma'])
})

test('saveSeriesOrder writes gamma before dev', async () => {
  const page = await firstWindow(app)
  const loaded = await page.evaluate((mp) => window.electron.umb.loadSeriesOrder(mp), modDir)
  const gamma = loaded.items.find((i) => i.seriesId === 'gamma')!
  const dev = loaded.items.find((i) => i.seriesId === 'dev')!
  expect(gamma, 'gamma series missing from loadSeriesOrder').toBeDefined()
  expect(dev, 'dev series missing from loadSeriesOrder').toBeDefined()

  const items = [gamma, dev].map((s) => ({ id: s.id, fields: s.fields }))
  const result = await page.evaluate(
    ([mp, payload]) => window.electron.umb.saveSeriesOrder(mp as string, payload as never),
    [modDir, items] as const
  )
  expect(result.items.map((i) => i.seriesId)).toEqual(['gamma', 'dev'])

  const toml = readFileSync(join(modDir, 'series-order.toml'), 'utf8')
  const order = [...toml.matchAll(/^\s+"([^"]+)",/gm)].map((m) => m[1])
  expect(order).toEqual(['gamma', 'dev'])
})

test('editing a [series] field persists to series.toml', async () => {
  const page = await firstWindow(app)
  const loaded = await page.evaluate((mp) => window.electron.umb.loadSeriesOrder(mp), modDir)
  const items = loaded.items.map((i) => ({
    id: i.id,
    fields: i.seriesId === 'gamma' ? { ...i.fields, name: 'Gamma Edited', playlistIncidence: 42 } : i.fields
  }))
  const result = await page.evaluate(
    ([mp, payload]) => window.electron.umb.saveSeriesOrder(mp as string, payload as never),
    [modDir, items] as const
  )
  expect(result.items.find((i) => i.seriesId === 'gamma')?.fields.name).toBe('Gamma Edited')

  const toml = readFileSync(join(modDir, 'gamma', 'series.toml'), 'utf8')
  expect(toml).toContain('name = "Gamma Edited"')
  expect(toml).toContain('playlist-incidence = 42')
})

test('UI smoke: Manage Series view opens', async () => {
  const page = await firstWindow(app)
  await page.getByText('Manage Series').first().click()
  await expect(page.getByText('test-mod').or(page.getByText('Gamma')).first()).toBeVisible({ timeout: 5000 })
})

test('UI: editing the name + adding a game via the panel persists to series.toml', async () => {
  const page = await firstWindow(app)
  await page.getByText('Manage Series').first().click()
  await page.getByRole('button', { name: 'Reload series' }).click()
  await page.getByText('Gamma').first().click() // select the card

  // Settings tab: rename the series.
  await page.getByLabel('Name', { exact: true }).fill('Gamma DOM')

  // Games tab: add a game through the modal.
  await page.getByRole('button', { name: 'Games', exact: true }).click()
  await page.getByRole('button', { name: 'Add game', exact: true }).click()
  await page.getByPlaceholder('mario_kart_8').fill('dom_game')
  await page.getByPlaceholder('Mario Kart 8').fill('DOM Game')
  await page.getByRole('button', { name: 'Add', exact: true }).click()

  await page.getByRole('button', { name: 'Save Changes' }).click()
  await expect(page.getByRole('button', { name: 'Saved' })).toBeVisible({ timeout: 5000 })

  const toml = readFileSync(join(modDir, 'gamma', 'series.toml'), 'utf8')
  expect(toml).toContain('name = "Gamma DOM"')
  expect(toml).toContain('id = "dom_game"')
  expect(toml).toContain('name = "DOM Game"')
})
