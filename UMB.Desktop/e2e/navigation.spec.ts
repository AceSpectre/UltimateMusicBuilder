import { test, expect, type ElectronApplication, type Locator, type Page } from '@playwright/test'
import { createWorkspace, seedTestDataMod, launchApp, firstWindow, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication

// App shell: nav, palette, bottom panel, theme. Runtime Debug pane is the assertion surface.

// nav order — src/renderer/src/lib/actions.ts
const NAV_ITEMS: Array<{ id: string; label: string }> = [
  { id: 'build', label: 'Build' },
  { id: 'nus3-convert', label: 'Nus3 Convert' },
  { id: 'config-volume', label: 'Config Volume' },
  { id: 'order-series', label: 'Manage Series' },
  { id: 'order-tracks', label: 'Manage Songs' },
  { id: 'manage-playlists', label: 'Manage Playlists' },
  { id: 'merge', label: 'Merge' },
  { id: 'extract-icons', label: 'Extract Icons' },
  { id: 'playlist-info', label: 'Playlist Info' }
]

test.beforeAll(async () => {
  ws = createWorkspace()
  seedTestDataMod(ws, 'test-mod')
  app = await launchApp(ws)
})

test.afterAll(async () => {
  await app?.close()
  ws?.cleanup()
})

/** Ensures the bottom panel is expanded so the Runtime Debug pane is readable. */
async function openBottomPanel(page: Page): Promise<void> {
  const show = page.getByRole('button', { name: 'Show panel' })
  if (await show.isVisible()) await show.click()
  await expect(page.getByText('Runtime Debug')).toBeVisible()
}

async function clickNav(page: Page, label: string): Promise<void> {
  await page.getByRole('navigation').getByText(label, { exact: true }).click()
}

/** The palette overlay, scoped so sidebar labels don't collide. */
function paletteOf(page: Page): Locator {
  return page
    .locator('div.fixed.inset-0.z-50')
    .filter({ has: page.getByPlaceholder('Search actions...') })
}

/** Opens the palette and returns [overlay, search input] with focus already inside. */
async function openPalette(page: Page): Promise<[Locator, Locator]> {
  await page.keyboard.press('Control+k')
  const search = page.getByPlaceholder('Search actions...')
  await expect(search).toBeVisible()
  // wait for the 50ms autofocus so Escape reaches the palette
  await expect(search).toBeFocused()
  return [paletteOf(page), search]
}

test('bottom panel reports a healthy bridge, the workspace, and the loaded mods', async () => {
  const page = await firstWindow(app)
  await openBottomPanel(page)

  await expect(page.getByText('bridge: ok')).toBeVisible({ timeout: 8000 })
  await expect(page.getByText('mods: loaded:1')).toBeVisible({ timeout: 8000 })
  await expect(page.getByText('last error: none')).toBeVisible()
})

test('sidebar navigates to every action view', async () => {
  const page = await firstWindow(app)
  await openBottomPanel(page)

  for (const item of NAV_ITEMS) {
    await clickNav(page, item.label)
    await expect(page.getByText(`active tab: ${item.id}`)).toBeVisible({ timeout: 8000 })
  }

  expect((await page.evaluate(() => window.electron.umb.debugPing())).ok).toBe(true)
})

test('sidebar counts each action click', async () => {
  const page = await firstWindow(app)
  await openBottomPanel(page)

  const clicksBefore = await page
    .getByText(/^action clicks: \d+$/)
    .textContent()
    .then((t) => parseInt(t!.replace('action clicks:', '').trim(), 10))

  await clickNav(page, 'Merge')
  await clickNav(page, 'Build')

  await expect(page.getByText(`action clicks: ${clicksBefore + 2}`)).toBeVisible()
})

test('command palette opens on Ctrl+K and closes on Escape', async () => {
  const page = await firstWindow(app)

  const [, search] = await openPalette(page)
  await search.press('Escape')
  await expect(search).toBeHidden()
})

test('command palette closes when the backdrop is clicked', async () => {
  const page = await firstWindow(app)

  const [palette, search] = await openPalette(page)
  await palette.click({ position: { x: 5, y: 5 } })
  await expect(search).toBeHidden()
})

test('command palette filters actions by the typed query', async () => {
  const page = await firstWindow(app)

  const [palette, search] = await openPalette(page)
  await search.fill('manage')

  await expect(palette.getByRole('button', { name: 'Manage Series' })).toBeVisible()
  await expect(palette.getByRole('button', { name: 'Manage Songs' })).toBeVisible()
  await expect(palette.getByRole('button', { name: 'Manage Playlists' })).toBeVisible()
  await expect(palette.getByRole('button', { name: 'Build', exact: true })).toHaveCount(0)
  await expect(palette.getByText('Mods', { exact: true })).toHaveCount(0)

  await search.press('Escape')
  await expect(search).toBeHidden()
})

test('command palette shows the empty state for a query that matches nothing', async () => {
  const page = await firstWindow(app)

  const [palette, search] = await openPalette(page)
  await search.fill('zzzznotanaction')
  await expect(palette.getByText('No results found.')).toBeVisible()

  await search.press('Escape')
  await expect(search).toBeHidden()
})

test('command palette selecting an action switches the view and closes', async () => {
  const page = await firstWindow(app)
  await openBottomPanel(page)
  await clickNav(page, 'Build')

  const [palette, search] = await openPalette(page)
  await search.fill('extract')
  await palette.getByRole('button', { name: 'Extract Icons' }).click()

  await expect(search).toBeHidden()
  await expect(page.getByText('active tab: extract-icons')).toBeVisible({ timeout: 8000 })
})

test('command palette lists mods and selecting one sets the active mod', async () => {
  const page = await firstWindow(app)
  await openBottomPanel(page)

  const [palette, search] = await openPalette(page)
  await expect(palette.getByText('Mods', { exact: true })).toBeVisible()
  await palette.getByRole('button', { name: 'test-mod', exact: true }).click()

  await expect(search).toBeHidden()
  await expect(page.getByText('mod selections: 1 / test-mod')).toBeVisible()
})

test('bottom panel collapses and re-expands on Ctrl+J', async () => {
  const page = await firstWindow(app)
  await openBottomPanel(page)

  await page.keyboard.press('Control+j')
  await expect(page.getByRole('button', { name: 'Show panel' })).toBeVisible()
  await expect(page.getByText('Runtime Debug')).toBeHidden()

  await page.keyboard.press('Control+j')
  await expect(page.getByText('Runtime Debug')).toBeVisible()
})

test('bottom panel Ping re-runs the bridge check', async () => {
  const page = await firstWindow(app)
  await openBottomPanel(page)

  await page.getByRole('button', { name: 'Ping' }).click()
  await expect(page.getByText('bridge: ok')).toBeVisible({ timeout: 8000 })
})

test('console pane renders its empty state and every level filter', async () => {
  const page = await firstWindow(app)
  await openBottomPanel(page)

  await expect(page.getByText('0 log lines')).toBeVisible()
  for (const filter of ['Info', 'Warn', 'Error', 'All']) {
    await page.getByRole('button', { name: filter, exact: true }).click()
    await expect(page.getByText('No log entries yet.')).toBeVisible()
  }
})

test('theme toggle flips the dark class and persists the choice', async () => {
  const page = await firstWindow(app)

  const isDark = (): Promise<boolean> =>
    page.evaluate(() => document.documentElement.classList.contains('dark'))
  const stored = (): Promise<string | null> =>
    page.evaluate(() => localStorage.getItem('umb-theme'))

  const before = await isDark()
  await page.getByTitle(before ? 'Switch to light mode' : 'Switch to dark mode').click()

  expect(await isDark()).toBe(!before)
  expect(await stored()).toBe(before ? 'light' : 'dark')

  await page.getByTitle(!before ? 'Switch to light mode' : 'Switch to dark mode').click()
  expect(await isDark()).toBe(before)
})

test('mod picker in the app bar lists and selects mods', async () => {
  const page = await firstWindow(app)
  await openBottomPanel(page)

  await page.getByText('Mods/MusicMods/', { exact: true }).click()
  await expect(page.getByText('Select Mod')).toBeVisible()

  await page.getByRole('button', { name: /test-mod/ }).first().click()
  await expect(page.getByText('Select Mod')).toBeHidden()
  await expect(page.getByText(/mod selections: \d+ \/ test-mod/)).toBeVisible()
})
