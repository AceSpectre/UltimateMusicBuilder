import { test, expect, type ElectronApplication } from '@playwright/test'
import { launchApp, firstWindow, closeApp, repoRoot, hasGameResources } from './e2e-utils'

let app: ElectronApplication

test.beforeAll(async () => {
  test.skip(!hasGameResources(), 'requires local game resources (vanilla PRC/MSBT)')
  app = await launchApp(repoRoot())
})
test.afterAll(async () => { await closeApp(app) })

test('getPlaylistInfo parses vanilla playlists and stages', async () => {
  const page = await firstWindow(app)
  const data = await page.evaluate(() => window.electron.umb.getPlaylistInfo())

  expect(data.playlists.length).toBeGreaterThanOrEqual(30)
  expect(data.stages.length).toBeGreaterThanOrEqual(100)

  const persona = data.playlists.find((p) => p.id === 'bgmjack')
  expect(persona, 'bgmjack playlist not found').toBeDefined()
  expect(persona!.name).toBe('Persona')
  expect(persona!.songCount).toBeGreaterThan(0)

  const battlefield = data.stages.find((s) => s.uiStageId === 'ui_stage_battle_field')
  expect(battlefield, 'ui_stage_battle_field not found').toBeDefined()
  expect(battlefield!.name).toBe('Battlefield')
  expect(battlefield!.songs.length).toBeGreaterThan(0)
})

test('UI smoke: Playlist Info view opens', async () => {
  const page = await firstWindow(app)
  await page.getByText('Playlist Info').first().click()
  await expect(page.getByText('Persona').or(page.getByText('Battlefield')).first()).toBeVisible({ timeout: 8000 })
})
