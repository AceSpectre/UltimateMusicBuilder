import { test, expect, type ElectronApplication } from '@playwright/test'
import { existsSync, readFileSync } from 'fs'
import { join } from 'path'
import {
  createWorkspace,
  seedTestDataMod,
  launchApp,
  firstWindow,
  closeApp,
  hasGameResources,
  linkResources,
  type E2EWorkspace
} from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication
let modDir: string

test.beforeAll(async () => {
  test.skip(!hasGameResources(), 'requires local game resources (vanilla PRC/MSBT)')
  ws = createWorkspace()
  linkResources(ws)
  modDir = seedTestDataMod(ws, 'test-mod')
  app = await launchApp(ws)
})
test.afterAll(async () => {
  await closeApp(app)
  ws?.cleanup()
})

test('loadManagePlaylists shows game, series and stage playlists as the build makes them', async () => {
  const page = await firstWindow(app)
  const data = await page.evaluate((mp) => window.electron.umb.loadManagePlaylists(mp), modDir)

  const mario = data.playlists.find((p) => p.id === 'bgmmario')!
  expect(mario.kind).toBe('vanilla')
  expect(mario.tracks.length).toBeGreaterThan(0)
  expect(data.playlists.find((p) => p.id === 'bgm_gametitle_dev')?.kind).toBe('series')
  expect(data.stages.find((s) => s.id === 'ui_stage_battle_field')?.playlistId).toBe('bgmsmashbtl')
  expect(data.songs.some((s) => s.isMod)).toBe(true)
})

test('UI: create a playlist, add a song, give it to a stage and save to playlists.toml', async () => {
  const page = await firstWindow(app)
  await page.getByText('Manage Playlists').first().click()
  await page.getByRole('button', { name: 'Reload' }).click()

  await page.getByRole('button', { name: 'New Playlist' }).click()
  await page.getByLabel('Name').fill('Ee Mix')
  await expect(page.getByLabel('Playlist id')).toHaveValue('bgmeemix')
  await page.getByRole('button', { name: 'Create' }).click()

  await page.getByRole('button', { name: 'Add songs' }).click()
  await page.locator('div.fixed.z-50').getByRole('button').first().click()
  await page.getByRole('button', { name: 'Done' }).click()

  await page.getByRole('tab', { name: 'Stages' }).click()
  await page.getByPlaceholder('Search stages...').fill('ui_stage_battle_field')
  await page.getByRole('row', { name: /^Battlefield/ }).getByLabel('Playlist').selectOption('bgmeemix')

  await page.getByRole('button', { name: 'Save Changes' }).click()
  await expect(page.getByRole('button', { name: 'Saved' })).toBeVisible({ timeout: 10000 })

  const toml = readFileSync(join(modDir, 'playlists.toml'), 'utf8')
  expect(toml).toContain('id = "bgmeemix"')
  expect(toml).toContain('name = "Ee Mix"')
  expect(toml).toMatch(/\[\[stages\]\]\nid = "ui_stage_battle_field"\nplaylist = "bgmeemix"/)

  const data = await page.evaluate((mp) => window.electron.umb.loadManagePlaylists(mp), modDir)
  expect(data.playlists.find((p) => p.id === 'bgmeemix')?.tracks.length).toBe(1)
  expect(data.stages.find((s) => s.id === 'ui_stage_battle_field')?.playlistId).toBe('bgmeemix')
})

test('saving with nothing changed from the game removes playlists.toml', async () => {
  const page = await firstWindow(app)
  await page.evaluate((mp) => window.electron.umb.saveManagePlaylists(mp, [], []), modDir)

  expect(existsSync(join(modDir, 'playlists.toml'))).toBe(false)
})
