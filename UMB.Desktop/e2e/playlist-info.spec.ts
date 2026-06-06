import { test, expect, type ElectronApplication } from '@playwright/test'
import { _electron as electron } from '@playwright/test'
import { firstWindow, repoRoot } from './e2e-utils'
import { resolve, dirname } from 'path'
import { fileURLToPath } from 'url'

const __dirname = dirname(fileURLToPath(import.meta.url))
let app: ElectronApplication

test.beforeAll(async () => {
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

  expect(data.playlists.length).toBeGreaterThan(0)
  expect(data.stages.length).toBeGreaterThan(0)

  // bgmjack → "Persona" (PLAYLIST_NAMES) with vanilla songs.
  const persona = data.playlists.find((p) => p.id === 'bgmjack')
  expect(persona?.name).toBe('Persona')
  expect((persona?.songCount ?? 0)).toBeGreaterThan(0)

  // A known stage resolves its display name + has songs.
  const battlefield = data.stages.find((s) => s.uiStageId === 'ui_stage_battle_field')
  expect(battlefield?.name).toBe('Battlefield')
  expect((battlefield?.songs.length ?? 0)).toBeGreaterThan(0)
})

test('UI smoke: Playlist Info view opens', async () => {
  const page = await firstWindow(app)
  await page.getByText('Playlist Info').first().click()
  await expect(page.getByText('Persona').or(page.getByText('Battlefield')).first()).toBeVisible({ timeout: 8000 })
})
