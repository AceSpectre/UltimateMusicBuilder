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
  try {
    const b = Buffer.alloc(24)
    const read = readSync(fd, b, 0, 24, 0)
    if (read < 24) throw new Error(`PNG too short (${read} bytes): ${file}`)
    return { w: b.readUInt32BE(16), h: b.readUInt32BE(20) }
  } finally {
    closeSync(fd)
  }
}

test.beforeAll(async () => {
  ws = createWorkspace()
  seedTool(ws, 'UltimateTexCli')
  modDir = join(ws.root, 'Mods', 'MusicMods', 'umb-target')
  mkdirSync(join(modDir, 'dev'), { recursive: true })
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

test('a mod without a matching series extracts nothing', async () => {
  const noMatch = join(ws.root, 'Mods', 'MusicMods', 'no-match')
  mkdirSync(join(noMatch, 'gamma'), { recursive: true })
  const page = await firstWindow(app)

  const a = await page.evaluate(
    ([c, m]) => window.electron.umb.analyzeExtractIcons(c as string, m as string),
    [compiled(), noMatch] as const
  )
  expect(a.matched).toEqual([])
  expect(a.unmatched).toEqual(['dev'])

  const result = await page.evaluate(
    ([c, m]) => window.electron.umb.extractIcons(c as string, m as string, 'all'),
    [compiled(), noMatch] as const
  )
  expect(result.extracted).toBe(0)
  expect(existsSync(join(noMatch, 'gamma', 'icon.png'))).toBe(false)
})
