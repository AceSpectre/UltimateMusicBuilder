import { test, expect, type ElectronApplication } from '@playwright/test'
import { readFileSync } from 'fs'
import { join } from 'path'
import { launchApp, firstWindow, closeApp, repoRoot, copyConfiguredSeries, hasGameResources, hasTool } from './e2e-utils'

let app: ElectronApplication
let series: { dir: string; cleanup(): void }

test.describe.configure({ timeout: 240_000 }) // real LUFS + daemon bootstrap

test.beforeAll(async () => {
  series = copyConfiguredSeries('dev')
  app = await launchApp(repoRoot())
})
test.afterAll(async () => { await closeApp(app); series?.cleanup() })

test('analyze returns per-track LUFS + auto-gain matching the CLI gain formula', async () => {
  test.skip(
    !hasGameResources() || !hasTool('ffmpeg') || !hasTool('dotnet'),
    'requires local game resources + ffmpeg + dotnet daemon'
  )
  const page = await firstWindow(app)
  const data = await page.evaluate((sp) => window.electron.umb.loadVolumeConfig(sp, true), series.dir)

  expect(data.ffmpegAvailable).toBe(true)
  expect(data.items.length).toBe(13)
  expect(data.targetLufs).toBe(-14)
  expect(data.maxGainDb).toBe(12)

  for (const item of data.items) {
    expect(item.hasMeasurement).toBe(true)
    const expected = Math.min(data.targetLufs - item.measuredLufs, data.maxGainDb)
    expect(item.autoGainDb).toBeCloseTo(expected, 1)
  }
})

test('save writes the per-track dB boost into the tracks.csv volume_db column', async () => {
  // daemon-routed: dotnet only
  test.skip(!hasTool('dotnet'), 'requires dotnet for the CLI daemon')
  const page = await firstWindow(app)
  await page.evaluate(
    (sp) => window.electron.umb.saveVolumeConfig(sp, [{ originalIndex: 0, db: -6 }]),
    series.dir
  )
  const csv = readFileSync(join(series.dir, 'tracks.csv'), 'utf8').split(/\r?\n/).filter((l) => l.trim())
  const header = csv[0].split(',')
  const volIdx = header.indexOf('volume_db')
  expect(volIdx).toBeGreaterThanOrEqual(0)
  expect(csv[1].split(',')[volIdx]).toBe('-6')
  expect(csv[2].split(',')[volIdx]).toBe('0')
})
