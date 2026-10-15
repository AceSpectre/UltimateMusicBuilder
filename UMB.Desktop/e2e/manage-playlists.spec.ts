import { test, expect, type ElectronApplication } from '@playwright/test'
import { existsSync, readFileSync, writeFileSync } from 'fs'
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
  expect(data.playlists.find((p) => p.id === 'bgm_dev')?.series.map((s) => s.id)).toEqual(['dev'])
  expect(data.playlists.some((p) => p.id.startsWith('bgm_gametitle_'))).toBe(false)
  expect(data.stages.find((s) => s.id === 'ui_stage_battle_field')?.playlistId).toBe('bgmsmashbtl')
  expect(data.songs.some((s) => s.isMod)).toBe(true)
  expect(data.legacyBlocks).toBe(true)
})

test('UI: legacy series.toml playlists ask before migrating', async () => {
  const page = await firstWindow(app)
  const seriesToml = join(modDir, 'dev', 'series.toml')
  writeFileSync(seriesToml, readFileSync(seriesToml, 'utf8') + '\n[[playlists]]\nid = "bgm_dev_mix"\nincidence = 100\n')

  await page.getByText('Manage Playlists').first().click()
  await page.getByRole('button', { name: 'Reload' }).click()
  await expect(page.getByText('Legacy playlist data found')).toBeVisible({ timeout: 10000 })
  await page.getByRole('button', { name: 'Not now' }).click()
  await expect(page.getByRole('button', { name: 'Save Changes' })).toBeDisabled()

  await page.getByRole('button', { name: 'Migrate' }).click()
  await page.locator('div.fixed.z-50').getByRole('button', { name: 'Migrate' }).click()
  await expect(page.getByText('Legacy playlist data found')).toBeHidden({ timeout: 10000 })

  const series = readFileSync(seriesToml, 'utf8')
  expect(series).not.toContain('[[playlists]]')
  expect(series).not.toContain('series-playlist')
  const toml = readFileSync(join(modDir, 'playlists.toml'), 'utf8')
  expect(toml).toContain('id = "bgm_dev"\nname = "Somewhat Good: Karts"\nseries = ["dev"]\n\n')
  expect(toml).toContain('id = "bgmmario"\nseries = ["mario"]\n')
  expect(toml).toMatch(/id = "bgm_dev_mix"\n(name = .*\n)?series = \["dev"\]/)
})

test('UI: create a playlist, add a song, give it to a stage and save to playlists.toml', async () => {
  const page = await firstWindow(app)
  await page.getByRole('button', { name: 'Reload' }).click()

  await page.getByRole('button', { name: 'New Playlist' }).click()
  await page.getByLabel('Name').fill('Ee Mix')
  await expect(page.getByLabel('Playlist id')).toHaveValue('bgmeemix')
  await page.getByRole('button', { name: 'Create' }).click()

  await page.getByRole('button', { name: 'Add songs' }).click()
  await page.locator('div.fixed.z-50').getByRole('button').first().click()
  await page.getByRole('button', { name: 'Done' }).click()

  await page.getByRole('tab', { name: 'Stages' }).click()
  await page.getByPlaceholder('Search stages…').fill('ui_stage_battle_field')
  await page.getByRole('row', { name: /^Battlefield/ }).getByLabel('Playlist').selectOption('bgmeemix')

  await page.getByRole('button', { name: 'Save Changes' }).click()
  await expect(page.getByRole('button', { name: 'Saved' })).toBeVisible({ timeout: 10000 })

  const toml = readFileSync(join(modDir, 'playlists.toml'), 'utf8')
  expect(toml).toContain('id = "bgmeemix"')
  expect(toml).toContain('name = "Ee Mix"')
  expect(toml).toContain('id = "bgm_dev"\nname = "Somewhat Good: Karts"\nseries = ["dev"]\n\n')
  expect(toml).toMatch(/\[\[stages\]\]\nid = "ui_stage_battle_field"\nplaylist = "bgmeemix"/)

  const data = await page.evaluate((mp) => window.electron.umb.loadManagePlaylists(mp), modDir)
  expect(data.playlists.find((p) => p.id === 'bgmeemix')?.tracks.length).toBe(1)
  expect(data.stages.find((s) => s.id === 'ui_stage_battle_field')?.playlistId).toBe('bgmeemix')
})

test('UI: locked vanilla songs in Manage Songs still show their values', async () => {
  const page = await firstWindow(app)
  await page.getByText('Manage Songs').first().click()
  await page.getByRole('button', { name: 'mario' }).first().click()
  await expect(page.getByRole('button', { name: 'Unlock Vanilla' })).toBeVisible({ timeout: 10000 })

  const data = await page.evaluate((sp) => window.electron.umb.loadTrackOrder(sp), join(modDir, 'mario'))
  const vanilla = data.items.find((i) => i.isVanilla && i.fields)!
  const gameName = data.games.find((g) => g.id === vanilla.fields!.game)!.name
  expect(data.items.filter((i) => !i.isVanilla).every((i) => i.fields?.game !== vanilla.fields!.game)).toBe(true)

  await expect(page.getByRole('listitem').filter({ hasText: vanilla.fields!.title }).getByText(gameName, { exact: true }).first()).toBeVisible()
})

test('saving with nothing changed from the game removes playlists.toml', async () => {
  const page = await firstWindow(app)
  await page.evaluate((mp) => window.electron.umb.saveManagePlaylists(mp, [], []), modDir)

  expect(existsSync(join(modDir, 'playlists.toml'))).toBe(false)
})
