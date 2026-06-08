import { test, expect, type ElectronApplication } from '@playwright/test'
import { _electron as electron } from '@playwright/test'
import { firstWindow, repoRoot, hasGameResources } from './e2e-utils'
import { resolve, dirname } from 'path'
import { fileURLToPath } from 'url'

const __dirname = dirname(fileURLToPath(import.meta.url))
let app: ElectronApplication

test.beforeAll(async () => {
  test.skip(!hasGameResources(), 'requires local game resources (vanilla PRC/MSBT)')
  const mainPath = resolve(__dirname, '..', 'dist', 'main', 'index.js')
  app = await electron.launch({
    args: [mainPath],
    env: { ...process.env, UMB_WORKSPACE: repoRoot(), NODE_ENV: 'test' }
  })
})
test.afterAll(async () => { await app?.close() })

test('getPlaylistInfo parses vanilla playlists and stages', async () => {
  const page = await firstWindow(app)
  const data = await page.evaluate(() => window.electron.umb.getPlaylistInfo())

  // Lower bounds (well under the real vanilla counts) catch a partial parse, not just empty.
  expect(data.playlists.length).toBeGreaterThanOrEqual(30)
  expect(data.stages.length).toBeGreaterThanOrEqual(100)

  // bgmjack → "Persona" (PLAYLIST_NAMES) with vanilla songs.
  const persona = data.playlists.find((p) => p.id === 'bgmjack')
  expect(persona, 'bgmjack playlist not found').toBeDefined()
  expect(persona!.name).toBe('Persona')
  expect(persona!.songCount).toBeGreaterThan(0)

  // A known stage resolves its display name + has songs.
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
