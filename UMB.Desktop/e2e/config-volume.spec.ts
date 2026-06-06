import { test, expect, type ElectronApplication } from '@playwright/test'
import { _electron as electron } from '@playwright/test'
import { readFileSync } from 'fs'
import { resolve, join, dirname } from 'path'
import { fileURLToPath } from 'url'
import { firstWindow, repoRoot, copyConfiguredSeries } from './e2e-utils'

const __dirname = dirname(fileURLToPath(import.meta.url))
let app: ElectronApplication
let series: { dir: string; cleanup(): void }

test.describe.configure({ timeout: 240_000 }) // real LUFS + daemon bootstrap

test.beforeAll(async () => {
  series = copyConfiguredSeries('dev')
  const mainPath = resolve(__dirname, '..', 'dist', 'main', 'index.js')
  app = await electron.launch({
    args: [mainPath],
    env: { ...process.env, UMB_WORKSPACE: repoRoot(), NODE_ENV: 'test' }
  })
})
test.afterAll(async () => { await app?.close(); series?.cleanup() })

test('analyze returns per-track LUFS + auto-gain matching the CLI gain formula', async () => {
  const page = await firstWindow(app)
  const data = await page.evaluate((sp) => window.electron.umb.loadVolumeConfig(sp, true), series.dir)

  expect(data.ffmpegAvailable).toBe(true)
  expect(data.items.length).toBe(13)
  // Pin the analysis config so the formula loop verifies real values, not just internal consistency.
  expect(data.targetLufs).toBe(-14)
  expect(data.maxMultiplier).toBe(4)

  const target = data.targetLufs   // -14 from appsettings
  const max = data.maxMultiplier   // 4
  for (const item of data.items) {
    expect(item.hasMeasurement).toBe(true)
    const raw = Math.pow(10, (target - item.measuredLufs) / 20)
    const expected = Math.min(raw, max)
    if (!item.wasClamped) {
      expect(Math.abs(item.autoGain - expected)).toBeLessThan(0.02)
    } else {
      expect(item.autoGain).toBeCloseTo(max, 2)
    }
  }
})

test('save writes the per-track override into tracks.csv volume column', async () => {
  const page = await firstWindow(app)
  await page.evaluate(
    (sp) => window.electron.umb.saveVolumeConfig(sp, [{ originalIndex: 0, volume: 0.5 }]),
    series.dir
  )
  const csv = readFileSync(join(series.dir, 'tracks.csv'), 'utf8').split(/\r?\n/).filter((l) => l.trim())
  // Look up the volume column by header name rather than a magic index.
  const header = csv[0].split(',')
  const volIdx = header.indexOf('volume')
  expect(volIdx).toBeGreaterThanOrEqual(0)
  expect(csv[1].split(',')[volIdx]).toBe('0.5')
  // Override must be surgical: a non-targeted row keeps its original volume.
  expect(csv[2].split(',')[volIdx]).toBe('1')
})

test('UI smoke: Config Volume view opens', async () => {
  const page = await firstWindow(app)
  await page.getByText('Config Volume').first().click()
  await expect(page.getByText('Config Volume').or(page.getByText('dev')).first()).toBeVisible({ timeout: 8000 })
})
