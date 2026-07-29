import { test, expect, type ElectronApplication } from '@playwright/test'
import { createWorkspace, seedTestDataMod, launchApp, firstWindow, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication

/**
 * selectFolder wraps a native modal, which would hang a headless run, so each
 * test replaces dialog.showOpenDialog in the main process for the duration of
 * the call. The handler under test — argument shape, cancel handling and the
 * first-path unwrap — is still the real one.
 */

/** Makes the next showOpenDialog calls resolve to `result`, and records its args. */
async function stubDialog(
  result: { canceled: boolean; filePaths: string[] }
): Promise<void> {
  await app.evaluate(async ({ dialog }, res) => {
    const g = globalThis as unknown as { __dialogArgs?: unknown[] }
    g.__dialogArgs = []
    // @ts-expect-error deliberately monkey-patching the real dialog module
    dialog.showOpenDialog = async (...args: unknown[]) => {
      g.__dialogArgs!.push(args)
      return res
    }
  }, result)
}

async function dialogCallCount(): Promise<number> {
  return app.evaluate(() => {
    const g = globalThis as unknown as { __dialogArgs?: unknown[] }
    return g.__dialogArgs?.length ?? 0
  })
}

/** The options object the handler passed to showOpenDialog on its last call. */
async function lastDialogOptions(): Promise<Record<string, unknown> | null> {
  return app.evaluate(() => {
    const g = globalThis as unknown as { __dialogArgs?: unknown[][] }
    const args = g.__dialogArgs
    if (!args || args.length === 0) return null
    const last = args[args.length - 1]
    // showOpenDialog(browserWindow, options) — options is the second argument.
    return (last[1] ?? last[0]) as Record<string, unknown>
  })
}

test.beforeAll(async () => {
  ws = createWorkspace()
  seedTestDataMod(ws, 'test-mod')
  app = await launchApp(ws)
})

test.afterAll(async () => {
  await app?.close()
  ws?.cleanup()
})

test('selectFolder returns the chosen directory', async () => {
  const page = await firstWindow(app)
  await stubDialog({ canceled: false, filePaths: [ws.root] })

  const chosen = await page.evaluate(() => window.electron.umb.selectFolder())
  expect(chosen).toBe(ws.root)
  expect(await dialogCallCount()).toBe(1)
})

test('selectFolder asks for a directory, not a file', async () => {
  const page = await firstWindow(app)
  await stubDialog({ canceled: false, filePaths: [ws.root] })

  await page.evaluate(() => window.electron.umb.selectFolder())

  const options = await lastDialogOptions()
  expect(options?.properties).toEqual(['openDirectory'])
})

test('selectFolder returns the first path when the dialog yields several', async () => {
  const page = await firstWindow(app)
  await stubDialog({ canceled: false, filePaths: [ws.root, 'C:/second', 'C:/third'] })

  expect(await page.evaluate(() => window.electron.umb.selectFolder())).toBe(ws.root)
})

test('selectFolder returns null when the user cancels', async () => {
  const page = await firstWindow(app)
  await stubDialog({ canceled: true, filePaths: [] })

  expect(await page.evaluate(() => window.electron.umb.selectFolder())).toBeNull()
})

test('selectFolder returns null when the dialog yields no path', async () => {
  const page = await firstWindow(app)
  // Not cancelled, but empty — must not resolve to undefined and blow up callers.
  await stubDialog({ canceled: false, filePaths: [] })

  expect(await page.evaluate(() => window.electron.umb.selectFolder())).toBeNull()
})

test('Extract Icons Browse fills the compiled mod folder from the dialog', async () => {
  const page = await firstWindow(app)
  await stubDialog({ canceled: false, filePaths: [ws.root] })

  await page.getByRole('navigation').getByText('Extract Icons', { exact: true }).click()
  await expect(page.getByText('No folder selected')).toBeVisible()

  await page.getByRole('button', { name: 'Browse' }).click()

  await expect(page.getByText('No folder selected')).toBeHidden()
  // Scoped to <main>: the workspace path also appears in the Runtime Debug pane.
  await expect(page.getByRole('main').getByText(ws.root, { exact: false })).toBeVisible()
})

test('Extract Icons Browse leaves the folder unchanged when the dialog is cancelled', async () => {
  const page = await firstWindow(app)
  await stubDialog({ canceled: false, filePaths: [ws.root] })

  await page.getByRole('navigation').getByText('Extract Icons', { exact: true }).click()
  await page.getByRole('button', { name: 'Browse' }).click()
  await expect(page.getByRole('main').getByText(ws.root, { exact: false })).toBeVisible()

  await stubDialog({ canceled: true, filePaths: [] })
  await page.getByRole('button', { name: 'Browse' }).click()

  // A cancel must not clear an already-picked folder.
  await expect(page.getByRole('main').getByText(ws.root, { exact: false })).toBeVisible()
  await expect(page.getByText('No folder selected')).toBeHidden()
})
