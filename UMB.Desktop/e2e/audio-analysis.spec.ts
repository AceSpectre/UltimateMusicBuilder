import { test, expect, type ElectronApplication } from '@playwright/test'
import { existsSync, readFileSync, rmSync } from 'fs'
import { join } from 'path'
import {
  createWorkspace, launchApp, firstWindow, copyConfiguredSeries, hasTool, type E2EWorkspace
} from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication
let series: { dir: string; cleanup(): void }

// ffprobe/ffmpeg-backed helpers; gated on ffmpeg.

const FFMPEG_MISSING = !hasTool('ffmpeg')

const TRACK = 'flowerhead - Somewhat Good- Karts - 01 KARTS!.flac'
const TRACK_SECONDS = 60.458

test.describe.configure({ timeout: 120_000 })

test.beforeAll(async () => {
  ws = createWorkspace()
  series = copyConfiguredSeries('dev')
  app = await launchApp(ws)
})

test.afterAll(async () => {
  await app?.close()
  series?.cleanup()
  ws?.cleanup()
})

function cachePath(): string {
  return join(series.dir, 'songs-to-validate', '.analysis-cache.json')
}

function readCache(): Record<string, any> {
  return JSON.parse(readFileSync(cachePath(), 'utf-8'))
}

/** Drops the analysis cache so the next call takes the real ffprobe/ffmpeg path. */
function clearCache(): void {
  rmSync(cachePath(), { force: true })
}

test('getTrackDuration probes the real duration and caches it', async () => {
  test.skip(FFMPEG_MISSING, 'requires ffmpeg/ffprobe')
  const page = await firstWindow(app)
  clearCache()

  const seconds = await page.evaluate(
    ([sp, f]) => window.electron.umb.getTrackDuration(sp, f),
    [series.dir, TRACK] as const
  )
  expect(seconds).toBeCloseTo(TRACK_SECONDS, 1)

  expect(existsSync(cachePath())).toBe(true)
  const entry = readCache()[TRACK]
  expect(entry.durationSeconds).toBeCloseTo(TRACK_SECONDS, 1)
  expect(entry.duration).toBe('1:00')
})

test('getTrackDuration serves a second call from the cache', async () => {
  test.skip(FFMPEG_MISSING, 'requires ffmpeg/ffprobe')
  const page = await firstWindow(app)

  const first = await page.evaluate(
    ([sp, f]) => window.electron.umb.getTrackDuration(sp, f),
    [series.dir, TRACK] as const
  )
  const second = await page.evaluate(
    ([sp, f]) => window.electron.umb.getTrackDuration(sp, f),
    [series.dir, TRACK] as const
  )
  expect(second).toBe(first)
})

test('getTrackDuration returns 0 for a file that does not exist', async () => {
  test.skip(FFMPEG_MISSING, 'requires ffmpeg/ffprobe')
  const page = await firstWindow(app)

  const seconds = await page.evaluate(
    (sp) => window.electron.umb.getTrackDuration(sp, 'nope.flac'),
    series.dir
  )
  expect(seconds).toBe(0)
})

test('extractWaveform returns one normalised peak per bar and caches them', async () => {
  test.skip(FFMPEG_MISSING, 'requires ffmpeg/ffprobe')
  const page = await firstWindow(app)
  clearCache()

  const peaks = await page.evaluate(
    ([sp, f]) => window.electron.umb.extractWaveform(sp, f),
    [series.dir, TRACK] as const
  )

  expect(peaks.length).toBe(140)
  for (const p of peaks) {
    expect(p).toBeGreaterThanOrEqual(0)
    expect(p).toBeLessThanOrEqual(1)
  }
  expect(Math.max(...peaks)).toBeCloseTo(1, 5)
  expect(peaks.filter((p) => p > 0).length).toBeGreaterThan(10)

  expect(readCache()[TRACK].peaks.length).toBe(140)
})

test('extractWaveform honours a custom bar count', async () => {
  test.skip(FFMPEG_MISSING, 'requires ffmpeg/ffprobe')
  const page = await firstWindow(app)
  clearCache()

  const peaks = await page.evaluate(
    ([sp, f]) => window.electron.umb.extractWaveform(sp, f, 32),
    [series.dir, TRACK] as const
  )
  expect(peaks.length).toBe(32)
})

test('extractWaveform serves a second call from the cache', async () => {
  test.skip(FFMPEG_MISSING, 'requires ffmpeg/ffprobe')
  const page = await firstWindow(app)
  clearCache()

  const first = await page.evaluate(
    ([sp, f]) => window.electron.umb.extractWaveform(sp, f, 64),
    [series.dir, TRACK] as const
  )
  const second = await page.evaluate(
    ([sp, f]) => window.electron.umb.extractWaveform(sp, f, 140),
    [series.dir, TRACK] as const
  )
  expect(second).toEqual(first)
})

test('extractWaveform returns empty for a file that does not exist', async () => {
  test.skip(FFMPEG_MISSING, 'requires ffmpeg/ffprobe')
  const page = await firstWindow(app)

  const peaks = await page.evaluate(
    (sp) => window.electron.umb.extractWaveform(sp, 'nope.flac'),
    series.dir
  )
  expect(peaks).toEqual([])
})

/** Seconds of 16-bit PCM in a base64 wav data URL, from its RIFF header. */
function wavSeconds(dataUrl: string): number {
  expect(dataUrl.startsWith('data:audio/wav;base64,')).toBe(true)
  const buf = Buffer.from(dataUrl.slice('data:audio/wav;base64,'.length), 'base64')
  expect(buf.subarray(0, 4).toString('ascii')).toBe('RIFF')
  expect(buf.subarray(8, 12).toString('ascii')).toBe('WAVE')
  const channels = buf.readUInt16LE(22)
  const sampleRate = buf.readUInt32LE(24)
  const bitsPerSample = buf.readUInt16LE(34)
  const dataBytes = buf.length - 44
  return dataBytes / (sampleRate * channels * (bitsPerSample / 8))
}

test('generateLoopPreview stitches the loop tail onto the loop head', async () => {
  test.skip(FFMPEG_MISSING, 'requires ffmpeg')
  const page = await firstWindow(app)

  const dataUrl = await page.evaluate(
    ([sp, f]) => window.electron.umb.generateLoopPreview(sp, f, 10, 40, 8),
    [series.dir, TRACK] as const
  )

  expect(dataUrl).not.toBeNull()
  expect(wavSeconds(dataUrl!)).toBeCloseTo(8, 1)
})

test('generateLoopPreview falls back to the tail-to-head transition when loop points are unset', async () => {
  test.skip(FFMPEG_MISSING, 'requires ffmpeg')
  const page = await firstWindow(app)

  const dataUrl = await page.evaluate(
    ([sp, f]) => window.electron.umb.generateLoopPreview(sp, f, 0, 0, 6),
    [series.dir, TRACK] as const
  )

  expect(dataUrl).not.toBeNull()
  expect(wavSeconds(dataUrl!)).toBeCloseTo(6, 1)
})

test('generateLoopPreview clamps a loop tail that runs past the start of the track', async () => {
  test.skip(FFMPEG_MISSING, 'requires ffmpeg')
  const page = await firstWindow(app)

  // tail clamps at 0 → 3s tail + 5s head = 8s
  const dataUrl = await page.evaluate(
    ([sp, f]) => window.electron.umb.generateLoopPreview(sp, f, 1, 3, 10),
    [series.dir, TRACK] as const
  )

  expect(dataUrl).not.toBeNull()
  expect(wavSeconds(dataUrl!)).toBeCloseTo(8, 1)
})

test('generateLoopPreview returns null for a file that does not exist', async () => {
  test.skip(FFMPEG_MISSING, 'requires ffmpeg')
  const page = await firstWindow(app)

  const dataUrl = await page.evaluate(
    (sp) => window.electron.umb.generateLoopPreview(sp, 'nope.flac', 1, 5, 4),
    series.dir
  )
  expect(dataUrl).toBeNull()
})
