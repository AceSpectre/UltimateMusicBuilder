import { test, expect, type ElectronApplication } from '@playwright/test'
import { _electron as electron } from '@playwright/test'
import { readdirSync } from 'fs'
import { resolve, dirname } from 'path'
import { fileURLToPath } from 'url'
import { firstWindow, repoRoot, copyConfiguredSeries, hasTool } from './e2e-utils'

const __dirname = dirname(fileURLToPath(import.meta.url))
let app: ElectronApplication
let series: { dir: string; cleanup(): void }

// decodeTrackPreview via the CLI daemon; .flac fixtures need ffmpeg, so decode tests gate on it.

const DAEMON_MISSING = !hasTool('dotnet')
const FFMPEG_MISSING = !hasTool('ffmpeg')

test.describe.configure({ timeout: 240_000 }) // dotnet run + daemon bootstrap

test.beforeAll(async () => {
  series = copyConfiguredSeries('dev')
  const mainPath = resolve(__dirname, '..', 'dist', 'main', 'index.js')
  app = await electron.launch({
    args: [mainPath],
    // repoRoot: daemon spawns dotnet run --project <workspace>/UMB.CLI
    env: { ...process.env, UMB_WORKSPACE: repoRoot(), NODE_ENV: 'test' }
  })
})

test.afterAll(async () => {
  await app?.close()
  series?.cleanup()
})

/** Parses the RIFF header of a base64 wav data URL. */
function wavHeader(dataUrl: string): {
  channels: number
  sampleRate: number
  bitsPerSample: number
  seconds: number
} {
  const buf = Buffer.from(dataUrl.slice('data:audio/wav;base64,'.length), 'base64')
  expect(buf.subarray(0, 4).toString('ascii')).toBe('RIFF')
  expect(buf.subarray(8, 12).toString('ascii')).toBe('WAVE')
  const channels = buf.readUInt16LE(22)
  const sampleRate = buf.readUInt32LE(24)
  const bitsPerSample = buf.readUInt16LE(34)
  return {
    channels,
    sampleRate,
    bitsPerSample,
    seconds: (buf.length - 44) / (sampleRate * channels * (bitsPerSample / 8))
  }
}

function firstFlac(): string {
  const flac = readdirSync(series.dir).find((f) => f.toLowerCase().endsWith('.flac'))
  if (!flac) throw new Error('no .flac in the copied dev series')
  return flac
}

test('decodeTrackPreview returns a playable WAV data URL for a source track', async () => {
  test.skip(DAEMON_MISSING, 'requires dotnet for the CLI daemon')
  test.skip(FFMPEG_MISSING, 'requires ffmpeg to decode the .flac fixtures')
  const page = await firstWindow(app)

  const dataUrl = await page.evaluate(
    ([sp, f]) => window.electron.umb.decodeTrackPreview(sp, f),
    [series.dir, firstFlac()] as const
  )

  expect(dataUrl).not.toBeNull()
  expect(dataUrl!.startsWith('data:audio/wav;base64,')).toBe(true)

  const header = wavHeader(dataUrl!)
  expect(header.sampleRate).toBeGreaterThan(0)
  expect(header.channels).toBeGreaterThanOrEqual(1)
  expect(header.bitsPerSample).toBeGreaterThanOrEqual(8)
  expect(header.seconds).toBeGreaterThan(5)
})

test('decodeTrackPreview returns null when the track is not in the series', async () => {
  test.skip(DAEMON_MISSING, 'requires dotnet for the CLI daemon')
  const page = await firstWindow(app)

  const dataUrl = await page.evaluate(
    (sp) => window.electron.umb.decodeTrackPreview(sp, 'does-not-exist.flac'),
    series.dir
  )
  expect(dataUrl).toBeNull()
})

test('decodeTrackPreview returns null for a series path that does not exist', async () => {
  test.skip(DAEMON_MISSING, 'requires dotnet for the CLI daemon')
  const page = await firstWindow(app)

  const dataUrl = await page.evaluate(
    (sp) => window.electron.umb.decodeTrackPreview(sp, 'anything.flac'),
    series.dir + '-missing'
  )
  expect(dataUrl).toBeNull()
})

test('decodeTrackPreview serialises concurrent requests through the daemon', async () => {
  test.skip(DAEMON_MISSING, 'requires dotnet for the CLI daemon')
  test.skip(FFMPEG_MISSING, 'requires ffmpeg to decode the .flac fixtures')
  const page = await firstWindow(app)
  const flac = firstFlac()

  const [a, b] = await page.evaluate(
    ([sp, f]) =>
      Promise.all([
        window.electron.umb.decodeTrackPreview(sp, f),
        window.electron.umb.decodeTrackPreview(sp, f)
      ]),
    [series.dir, flac] as const
  )

  expect(a).not.toBeNull()
  expect(b).not.toBeNull()
  expect(a).toBe(b)
})
