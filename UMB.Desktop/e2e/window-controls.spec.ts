import { test, expect, type ElectronApplication } from '@playwright/test'
import { createWorkspace, seedTestDataMod, launchApp, firstWindow, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication

/** The window is frameless (`frame: false`), so these IPC handlers ARE the title bar. */

test.beforeAll(async () => {
  ws = createWorkspace()
  seedTestDataMod(ws, 'test-mod')
  app = await launchApp(ws)
})

test.afterAll(async () => {
  await app?.close()
  ws?.cleanup()
})

/** Undo a minimize so later tests still interact with a visible window. */
async function restoreWindow(): Promise<void> {
  await app.evaluate(({ BrowserWindow }) => {
    const win = BrowserWindow.getAllWindows()[0]
    if (win?.isMinimized()) win.restore()
  })
}

test('windowMinimize acknowledges and actually minimizes the window', async () => {
  const page = await firstWindow(app)

  const result = await page.evaluate(() => window.electron.umb.windowMinimize())
  expect(result).toEqual({ ok: true, action: 'minimize' })

  const minimized = await app.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0]?.isMinimized()
  )
  expect(minimized).toBe(true)

  await restoreWindow()
})

test('windowFullscreen toggles the flag and reports the new state', async () => {
  const page = await firstWindow(app)

  const on = await page.evaluate(() => window.electron.umb.windowFullscreen())
  expect(on.ok).toBe(true)
  expect(on.action).toBe('fullscreen')
  expect(typeof on.fullScreen).toBe('boolean')

  const off = await page.evaluate(() => window.electron.umb.windowFullscreen())
  expect(off.ok).toBe(true)
  // Two calls must land on opposite states — that is the whole contract of a toggle.
  expect(off.fullScreen).toBe(!on.fullScreen)

  // Leave the window non-fullscreen for the remaining tests.
  if (off.fullScreen) await page.evaluate(() => window.electron.umb.windowFullscreen())
})

test('app bar minimize button is wired to the IPC handler', async () => {
  const page = await firstWindow(app)

  await page.getByLabel('Minimize window').click()

  await expect
    .poll(() => app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]?.isMinimized()))
    .toBe(true)

  await restoreWindow()
})

test('app bar fullscreen button is wired to the IPC handler', async () => {
  const page = await firstWindow(app)

  const before = await app.evaluate(({ BrowserWindow }) =>
    BrowserWindow.getAllWindows()[0]?.isFullScreen()
  )
  await page.getByLabel('Toggle fullscreen').click()

  await expect
    .poll(() => app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]?.isFullScreen()))
    .toBe(!before)

  await page.getByLabel('Toggle fullscreen').click()
  await expect
    .poll(() => app.evaluate(({ BrowserWindow }) => BrowserWindow.getAllWindows()[0]?.isFullScreen()))
    .toBe(before)
})

// Must be last in the file: closing the window quits the app via `window-all-closed`.
test('windowClose acknowledges and closes the window', async () => {
  const page = await firstWindow(app)

  const result = await page.evaluate(() => window.electron.umb.windowClose()).catch(() => null)
  // The reply can be lost to the teardown race, but the window must be gone either way.
  if (result) expect(result).toEqual({ ok: true, action: 'close' })

  await expect
    .poll(() => app.windows().length, { timeout: 10_000 })
    .toBe(0)
})
