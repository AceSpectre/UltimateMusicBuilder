import { test, expect, type ElectronApplication } from '@playwright/test'
import { _electron as electron } from '@playwright/test'
import { existsSync, statSync } from 'fs'
import { resolve, join, dirname } from 'path'
import { fileURLToPath } from 'url'
import { firstWindow, repoRoot, copyConfiguredSeries, hasTool } from './e2e-utils'

const __dirname = dirname(fileURLToPath(import.meta.url))
const FLAC = 'flowerhead - Somewhat Good- Karts - 13 Time Trials.flac'
const NUS3 = 'flowerhead - Somewhat Good- Karts - 13 Time Trials.nus3audio'
const BASELINE_SIZE = 855544

// Conversion shells out to ffmpeg + pymusiclooper + the dotnet CLI; skip the encode
// tests (and the ones that read their output) when those aren't on PATH, e.g. on CI.
const ENCODE_DEPS_MISSING =
  !hasTool('ffmpeg') || !hasTool('pymusiclooper') || !hasTool('dotnet')

let app: ElectronApplication
let series: { dir: string; cleanup(): void }

test.describe.configure({ timeout: 240_000 }) // pymusiclooper + VGAudio + daemon

test.beforeAll(async () => {
  series = copyConfiguredSeries('dev')
  const mainPath = resolve(__dirname, '..', 'dist', 'main', 'index.js')
  app = await electron.launch({
    args: [mainPath],
    env: { ...process.env, UMB_WORKSPACE: repoRoot(), NODE_ENV: 'test' }
  })
})
test.afterAll(async () => { await app?.close(); series?.cleanup() })

test('analyze + convert produces a non-empty nus3audio and persists the decision', async () => {
  test.skip(ENCODE_DEPS_MISSING, 'requires ffmpeg + pymusiclooper + dotnet')
  const page = await firstWindow(app)

  const analysis = await page.evaluate(
    ([sp, f]) => window.electron.umb.analyzeLoopPoints(sp as string, f as string),
    [series.dir, FLAC] as const
  )
  const decision = {
    trackId: FLAC,
    mode: (analysis.candidates.length > 0 ? 'loop' : 'end-to-end') as 'loop' | 'end-to-end',
    candidate: analysis.candidates[0],
    status: 'pending' as const
  }

  const ok = await page.evaluate(
    ([sp, d]) => window.electron.umb.convertNus3Track(sp as string, d as never),
    [series.dir, decision] as const
  )
  expect(ok).toBe(true)

  const produced = join(series.dir, 'songs-to-validate', NUS3)
  expect(existsSync(produced)).toBe(true)
  expect(statSync(produced).size).toBeGreaterThan(0)

  const conversions = await page.evaluate((sp) => window.electron.umb.loadNus3Conversions(sp), series.dir)
  expect(conversions[FLAC]).toBeTruthy()
})

test('produced nus3audio size matches the CLI baseline manifest (within tolerance)', async () => {
  test.skip(ENCODE_DEPS_MISSING, 'requires ffmpeg + pymusiclooper + dotnet')
  const produced = join(series.dir, 'songs-to-validate', NUS3)
  const size = statSync(produced).size
  // Audio payload is identical (same FLAC, same VGAudio encode); only loop-marker
  // fields differ, so size should land within ~2% of the CLI baseline.
  expect(Math.abs(size - BASELINE_SIZE)).toBeLessThan(BASELINE_SIZE * 0.02)
})

test('accept moves the validated nus3audio into the series folder', async () => {
  test.skip(ENCODE_DEPS_MISSING, 'requires ffmpeg + pymusiclooper + dotnet')
  const page = await firstWindow(app)
  // acceptNus3Files returns the CLI exit code (0 = success, -1 = error)
  const exitCode = await page.evaluate((sp) => window.electron.umb.acceptNus3Files(sp, false), series.dir)
  expect(exitCode).toBe(0) // CLI exited successfully
  expect(existsSync(join(series.dir, NUS3))).toBe(true)
})

test('UI smoke: Nus3 Convert view opens', async () => {
  const page = await firstWindow(app)
  await page.getByText('Nus3 Convert').first().click()
  await expect(page.getByText('Nus3 Convert').or(page.getByText('dev')).first()).toBeVisible({ timeout: 8000 })
})
