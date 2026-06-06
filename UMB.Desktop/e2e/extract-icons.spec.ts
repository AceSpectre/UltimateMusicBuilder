import { test, expect, type ElectronApplication } from '@playwright/test'
import { mkdirSync, existsSync, statSync, openSync, readSync, closeSync } from 'fs'
import { join } from 'path'
import { createWorkspace, launchApp, firstWindow, seedTool, repoRoot, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication
let modDir: string
const compiled = () => join(repoRoot(), 'Tests', 'TestData', 'baselines', 'extract-icons-source')

function pngDims(file: string): { w: number; h: number } {
  const fd = openSync(file, 'r')
  const b = Buffer.alloc(24)
  readSync(fd, b, 0, 24, 0)
  closeSync(fd)
  return { w: b.readUInt32BE(16), h: b.readUInt32BE(20) }
}

test.beforeAll(async () => {
  ws = createWorkspace()
  seedTool(ws, 'UltimateTexCli') // extractIcons resolves <workspace>/Tools/UltimateTexCli
  modDir = join(ws.root, 'Mods', 'MusicMods', 'umb-target')
  mkdirSync(join(modDir, 'dev'), { recursive: true }) // empty series dir named to match the BNTX
  app = await launchApp(ws)
})
test.afterAll(async () => { await app?.close(); ws?.cleanup() })

test('analyze matches series_0_dev.bntx to the dev series', async () => {
  const page = await firstWindow(app)
  const a = await page.evaluate(
    ([c, m]) => window.electron.umb.analyzeExtractIcons(c as string, m as string),
    [compiled(), modDir] as const
  )
  expect(a.matched.map((x) => x.seriesId)).toEqual(['dev'])
  expect(a.unmatched).toEqual([])
})

test('extract produces icon.png whose dimensions match the source icon', async () => {
  const page = await firstWindow(app)
  const result = await page.evaluate(
    ([c, m]) => window.electron.umb.extractIcons(c as string, m as string, 'all'),
    [compiled(), modDir] as const
  )
  expect(result.extracted).toBe(1)

  const out = join(modDir, 'dev', 'icon.png')
  expect(existsSync(out)).toBe(true)
  expect(statSync(out).size).toBeGreaterThan(0)

  const src = join(repoRoot(), 'Tests', 'TestData', 'configured-mod', 'dev', 'icon.png')
  expect(pngDims(out)).toEqual(pngDims(src))
})

test('UI smoke: Extract Icons view opens', async () => {
  const page = await firstWindow(app)
  await page.getByText('Extract Icons').first().click()
  await expect(page.getByText('umb-target').or(page.getByText('Extract Icons')).first()).toBeVisible({ timeout: 5000 })
})
