import { test, expect, type ElectronApplication } from '@playwright/test'
import { readFileSync } from 'fs'
import { join } from 'path'
import { createWorkspace, seedTestDataMod, launchApp, firstWindow, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication
let modDir: string

test.beforeAll(async () => {
  ws = createWorkspace()
  modDir = seedTestDataMod(ws, 'test-mod')
  app = await launchApp(ws)
})
test.afterAll(async () => {
  await app?.close()
  ws?.cleanup()
})

test('loadManagePlaylists lists existing game playlists and the mod song pool', async () => {
  const page = await firstWindow(app)
  const data = await page.evaluate((mp) => window.electron.umb.loadManagePlaylists(mp), modDir)
  expect(data.playlists.find((p) => p.id === 'bgmmario')?.name).toBe('Mario')
  expect(data.playlists.find((p) => p.id === 'bgmsmashmenu')).toBeTruthy()
  expect(data.songs.length).toBeGreaterThan(0)
})

test('saveManagePlaylists assigns a mod song to a playlist and writes series.toml', async () => {
  const page = await firstWindow(app)
  const data = await page.evaluate((mp) => window.electron.umb.loadManagePlaylists(mp), modDir)
  const song = data.songs[0]

  const result = await page.evaluate(
    ([mp, s]) =>
      window.electron.umb.saveManagePlaylists(mp as string, [
        { playlistId: 'bgmsmashmenu', seriesId: (s as { seriesId: string }).seriesId, filename: (s as { filename: string }).filename, incidence: 30 }
      ]),
    [modDir, song] as const
  )

  const menu = result.playlists.find((p) => p.id === 'bgmsmashmenu')!
  expect(menu.assignments.some((a) => a.filename === song.filename && a.incidence === 30)).toBe(true)

  const toml = readFileSync(join(modDir, song.seriesId, 'series.toml'), 'utf8')
  expect(toml).toContain('[[playlists]]')
  expect(toml).toContain('id = "bgmsmashmenu"')
  expect(toml).toContain('incidence = 30')
})

test('UI: add a song to a playlist, set incidence, and Save persists it', async () => {
  const page = await firstWindow(app)
  await page.getByText('Manage Playlists').first().click()
  await page.getByRole('button', { name: 'Reload' }).click()

  await page.getByPlaceholder('Search playlists...').fill('Smash Menu')
  await page.getByText('Smash Menu').first().click()

  await page.getByRole('button', { name: 'Add song' }).click()
  const modal = page.locator('div.fixed.z-50')
  await modal.getByRole('button').first().click()
  await page.getByRole('button', { name: 'Cancel' }).click()

  await page.getByRole('spinbutton').first().fill('45')
  await page.getByRole('button', { name: 'Save Changes' }).click()
  await expect(page.getByRole('button', { name: 'Saved' })).toBeVisible({ timeout: 5000 })

  const data = await page.evaluate((mp) => window.electron.umb.loadManagePlaylists(mp), modDir)
  const menu = data.playlists.find((p) => p.id === 'bgmsmashmenu')!
  expect(menu.assignments.length).toBeGreaterThanOrEqual(2)
  expect(menu.assignments.some((a) => a.incidence === 45)).toBe(true)
})
