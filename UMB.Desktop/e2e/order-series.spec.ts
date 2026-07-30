import { test, expect, type ElectronApplication } from '@playwright/test'
import { mkdirSync, writeFileSync, readFileSync, existsSync } from 'fs'
import { join } from 'path'
import { createWorkspace, seedTestDataMod, launchApp, firstWindow, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication
let modDir: string

test.beforeAll(async () => {
  ws = createWorkspace()
  modDir = seedTestDataMod(ws, 'test-mod')
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
  expect(data.hasSeriesOrder).toBe(true)
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
  await page.getByText('Gamma').first().click()

  await page.getByLabel('Name', { exact: true }).fill('Gamma DOM')

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

test('createSeries writes a new series folder (series.toml + header-only tracks.csv)', async () => {
  const page = await firstWindow(app)
  const result = await page.evaluate(
    (mp) =>
      window.electron.umb.createSeries(mp, {
        seriesId: 'ipc_series',
        name: 'IPC Series',
        seriesPlaylist: 'bgm_ipc_series',
        games: [{ id: 'ipc_game', name: 'IPC Game' }]
      }),
    modDir
  )
  expect(result.items.map((i) => i.seriesId)).toContain('ipc_series')

  const toml = readFileSync(join(modDir, 'ipc_series', 'series.toml'), 'utf8')
  expect(toml).toContain('id = "ipc_series"')
  expect(toml).toContain('name = "IPC Series"')
  expect(toml).toContain('series-playlist = "bgm_ipc_series"')
  expect(toml).toContain('id = "ipc_game"')
  expect(toml).toContain('game = "ipc_game"')

  const csv = readFileSync(join(modDir, 'ipc_series', 'tracks.csv'), 'utf8')
  expect(csv.trim()).toBe('filename,game,title,author,copyright,record_type,special_category,volume,info1,in_soundtest')
})

test('UI: New Series modal creates a series (playlist auto-filled, one game required)', async () => {
  const page = await firstWindow(app)
  await page.getByText('Manage Series').first().click()
  await page.getByRole('button', { name: 'Reload series' }).click()

  await page.getByRole('button', { name: 'New Series' }).click()
  await page.getByPlaceholder('my_series', { exact: true }).fill('ui_series')
  await page.getByPlaceholder('My Series', { exact: true }).fill('UI Series')

  await expect(page.getByRole('button', { name: 'Create' })).toBeDisabled()
  await page.getByPlaceholder('my_game').fill('ui_game')
  await page.getByPlaceholder('My Game').fill('UI Game')
  await page.getByRole('button', { name: 'Add game' }).click()

  await page.getByRole('button', { name: 'Create' }).click()

  await expect(page.getByText('UI Series').first()).toBeVisible({ timeout: 5000 })

  const toml = readFileSync(join(modDir, 'ui_series', 'series.toml'), 'utf8')
  expect(toml).toContain('id = "ui_series"')
  expect(toml).toContain('name = "UI Series"')
  expect(toml).toContain('series-playlist = "bgm_ui_series"')
  expect(toml).toContain('id = "ui_game"')
  expect(toml).toContain('game = "ui_game"')

  const csv = readFileSync(join(modDir, 'ui_series', 'tracks.csv'), 'utf8')
  expect(csv.trim()).toBe('filename,game,title,author,copyright,record_type,special_category,volume,info1,in_soundtest')
})

// 1x1 transparent PNG.
const PNG_B64 =
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=='
const pngFile = () => ({ name: 'icon.png', mimeType: 'image/png', buffer: Buffer.from(PNG_B64, 'base64') })

test('setSeriesIcon writes icon.png; loadSeriesOrder returns the data URL', async () => {
  const page = await firstWindow(app)
  const dataUrl = 'data:image/png;base64,' + PNG_B64
  const returned = await page.evaluate(
    ([mp, id, url]) => window.electron.umb.setSeriesIcon(mp as string, id as string, url as string),
    [modDir, 'gamma', dataUrl] as const
  )
  expect(returned).toBe(dataUrl)
  expect(existsSync(join(modDir, 'gamma', 'icon.png'))).toBe(true)

  const data = await page.evaluate((mp) => window.electron.umb.loadSeriesOrder(mp), modDir)
  expect(data.items.find((i) => i.seriesId === 'gamma')?.iconDataUrl).toBe(dataUrl)
})

test('UI: New Series modal can attach a PNG icon', async () => {
  const page = await firstWindow(app)
  await page.getByText('Manage Series').first().click()
  await page.getByRole('button', { name: 'Reload series' }).click()

  await page.getByRole('button', { name: 'New Series' }).click()
  await page.getByPlaceholder('my_series', { exact: true }).fill('icon_series')
  await page.getByPlaceholder('My Series', { exact: true }).fill('Icon Series')
  await page.locator('label:has-text("Upload icon") input[type="file"]').setInputFiles(pngFile())
  await page.getByPlaceholder('my_game').fill('icon_game')
  await page.getByPlaceholder('My Game').fill('Icon Game')
  await page.getByRole('button', { name: 'Add game' }).click()
  await page.getByRole('button', { name: 'Create' }).click()

  await expect(page.getByText('Icon Series').first()).toBeVisible({ timeout: 5000 })
  const iconPath = join(modDir, 'icon_series', 'icon.png')
  expect(existsSync(iconPath)).toBe(true)
  expect(readFileSync(iconPath).toString('base64')).toBe(PNG_B64)
})

test('UI: Change icon in the settings panel writes the chosen PNG', async () => {
  const page = await firstWindow(app)
  await page.getByText('Manage Series').first().click()
  await page.getByRole('button', { name: 'Reload series' }).click()
  await page.getByText('Somewhat Good: Karts').first().click()

  await page.locator('label:has-text("Change icon") input[type="file"]').setInputFiles(pngFile())

  const iconPath = join(modDir, 'dev', 'icon.png')
  await expect.poll(() => (existsSync(iconPath) ? readFileSync(iconPath).toString('base64') : null)).toBe(PNG_B64)
})
