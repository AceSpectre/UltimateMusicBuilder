import { test, expect, type ElectronApplication } from '@playwright/test'
import { createWorkspace, seedMod, launchApp, firstWindow, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication

test.beforeAll(async () => {
  ws = createWorkspace()
  seedMod(ws, 'test-mod', 'test-series', {
    tracksCsv: 'filename,title,game\nsong.flac,Song,Game\n'
  })
  app = await launchApp(ws)
})

test.afterAll(async () => {
  await app?.close()
  ws?.cleanup()
})

test('no raw i18n keys leak in app bar or sidebar', async () => {
  const page = await firstWindow(app)
  await page.waitForTimeout(1000)

  const bodyText = await page.locator('body').innerText()

  const i18nKeyPattern = /(?:^|\s)(app\.|appBar\.|nav\.|commandPalette\.|bottomPanel\.|orderTracks\.)\w+/g
  const leaked = bodyText.match(i18nKeyPattern)?.map((s) => s.trim()) ?? []

  expect(leaked, `Leaked i18n keys found in UI: ${leaked.join(', ')}`).toEqual([])
})

test('sidebar labels match locale strings', async () => {
  const page = await firstWindow(app)

  // see src/renderer/src/lib/actions.ts
  const expectedLabels = [
    'Build',
    'Nus3 Convert', 'Config Volume',
    'Manage Series', 'Manage Songs',
    'Merge', 'Extract Icons',
    'Playlist Info'
  ]

  const sidebar = page.getByRole('navigation')
  for (const label of expectedLabels) {
    await expect(
      sidebar.getByText(label, { exact: true }),
      `Expected sidebar label "${label}" to be visible`
    ).toBeVisible()
  }
})

test('order tracks view shows localised text when selected', async () => {
  const page = await firstWindow(app)

  await page.getByText('Manage Songs').first().click()
  await expect(page.getByRole('heading', { name: 'Series', exact: true })).toBeVisible()
})
