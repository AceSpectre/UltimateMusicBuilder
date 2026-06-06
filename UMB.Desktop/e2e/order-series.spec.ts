import { test, expect, type ElectronApplication } from '@playwright/test'
import { mkdirSync, writeFileSync, readFileSync } from 'fs'
import { join } from 'path'
import { createWorkspace, seedTestDataMod, launchApp, firstWindow, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication
let modDir: string

test.beforeAll(async () => {
  ws = createWorkspace()
  modDir = seedTestDataMod(ws, 'test-mod')
  // Add a second custom series so series ordering has two entries.
  const gamma = join(modDir, 'gamma')
  mkdirSync(gamma, { recursive: true })
  writeFileSync(join(gamma, 'series.toml'), '[series]\nid = "gamma"\nname = "Gamma"\n', 'utf8')
  writeFileSync(join(gamma, 'tracks.csv'), 'filename,title,game\ng.flac,G,Gamma\n', 'utf8')
  app = await launchApp(ws)
})
test.afterAll(async () => { await app?.close(); ws?.cleanup() })

test('loadSeriesOrder lists custom series; dev pre-ordered by series-order.toml', async () => {
  const page = await firstWindow(app)
  const data = await page.evaluate((mp) => window.electron.umb.loadSeriesOrder(mp), modDir)
  const ids = data.items.map((i) => i.seriesId).sort()
  expect(ids).toEqual(['dev', 'gamma'])
  expect(data.hasSeriesOrder).toBe(true) // configured-mod ships series-order.toml = ["dev"]
})

test('saveSeriesOrder writes gamma before dev', async () => {
  const page = await firstWindow(app)
  const loaded = await page.evaluate((mp) => window.electron.umb.loadSeriesOrder(mp), modDir)
  const gamma = loaded.items.find((i) => i.seriesId === 'gamma')!
  const dev = loaded.items.find((i) => i.seriesId === 'dev')!

  const result = await page.evaluate(
    ([mp, ids]) => window.electron.umb.saveSeriesOrder(mp as string, ids as string[]),
    [modDir, [gamma.id, dev.id]] as const
  )
  expect(result.items.map((i) => i.seriesId)).toEqual(['gamma', 'dev'])

  const toml = readFileSync(join(modDir, 'series-order.toml'), 'utf8')
  const order = [...toml.matchAll(/^\s+"([^"]+)",/gm)].map((m) => m[1])
  expect(order).toEqual(['gamma', 'dev'])
})

test('UI smoke: Order Series view opens', async () => {
  const page = await firstWindow(app)
  await page.getByText('Order Series').first().click()
  await expect(page.getByText('test-mod').or(page.getByText('Gamma')).first()).toBeVisible({ timeout: 5000 })
})
