import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { existsSync } from 'fs'
import { join } from 'path'

// nus3-convert imports cli.ts, which imports electron's `app`. Stub it so the
// module loads in a plain Node (vitest) context. vi.mock is hoisted above the
// imports below, so the stub is in place before nus3-convert loads.
vi.mock('electron', () => ({ app: { isPackaged: false } }))

import {
  listNus3Sources,
  loadConversions,
  rejectNus3Track,
  getTrackDuration,
  analyzeLoopPoints,
  extractWaveformPeaks
} from './nus3-convert'
import { makeWorkspace, makeDir, writeFile, type Workspace } from './test-utils'

let ws: Workspace

beforeEach(() => {
  ws = makeWorkspace()
})

afterEach(() => {
  ws.cleanup()
})

describe('listNus3Sources', () => {
  it('lists only audio files with prettified names', () => {
    const dir = makeDir(ws, 'mod', 'series')
    writeFile(dir, 'mass-destruction.flac', 'a')
    writeFile(dir, 'song_two.mp3', 'b')
    writeFile(dir, 'clip.wav', 'c')
    writeFile(dir, 'track.ogg', 'd')
    writeFile(dir, 'notes.txt', 'ignore')
    makeDir(ws, 'mod', 'series', 'subdir')

    const tracks = listNus3Sources(ws.root, dir).sort((a, b) => a.id.localeCompare(b.id))

    expect(tracks.map((t) => t.id)).toEqual([
      'clip.wav',
      'mass-destruction.flac',
      'song_two.mp3',
      'track.ogg'
    ])
    const md = tracks.find((t) => t.id === 'mass-destruction.flac')!
    expect(md.name).toBe('Mass Destruction')
    expect(md.src).toBe('mass-destruction.flac')
    expect(md.duration).toBe('—')
    expect(md.durationSeconds).toBe(0)
    expect(md.converted).toBe(false)
  })

  it('hides a source once a sibling .nus3audio exists in the series folder', () => {
    const dir = makeDir(ws, 'mod', 'series')
    writeFile(dir, 'done.flac', 'a')
    writeFile(dir, 'done.nus3audio', 'x')
    writeFile(dir, 'pending.flac', 'b')

    const ids = listNus3Sources(ws.root, dir).map((t) => t.id)
    expect(ids).toEqual(['pending.flac'])
  })

  it('marks converted=true when the .nus3audio is staged in songs-to-validate', () => {
    const dir = makeDir(ws, 'mod', 'series')
    writeFile(dir, 'staged.flac', 'a')
    writeFile(join(dir, 'songs-to-validate'), 'staged.nus3audio', 'x')

    const track = listNus3Sources(ws.root, dir).find((t) => t.id === 'staged.flac')!
    expect(track.converted).toBe(true)
  })

  it('returns [] for a missing series folder', () => {
    expect(listNus3Sources(ws.root, join(ws.root, 'nope'))).toEqual([])
  })
})

describe('loadConversions', () => {
  it('returns {} when no .conversions.json exists', () => {
    const dir = makeDir(ws, 'mod', 'series')
    expect(loadConversions(dir)).toEqual({})
  })

  it('parses persisted conversion metadata', () => {
    const dir = makeDir(ws, 'mod', 'series')
    writeFile(
      join(dir, 'songs-to-validate'),
      '.conversions.json',
      JSON.stringify({ 'a.flac': { mode: 'loop' } })
    )
    expect(loadConversions(dir)).toEqual({ 'a.flac': { mode: 'loop' } })
  })
})

describe('cached reads (short-circuit before any external tool)', () => {
  // When the source file is absent the cache guard can't stat it, so a seeded
  // entry is used unconditionally — exercising the cache-hit path without
  // ffprobe / ffmpeg / pymusiclooper.
  function seedCache(seriesDir: string, filename: string, entry: object): void {
    writeFile(
      join(seriesDir, 'songs-to-validate'),
      '.analysis-cache.json',
      JSON.stringify({ [filename]: entry })
    )
  }

  it('getTrackDuration returns the cached duration', async () => {
    const dir = makeDir(ws, 'mod', 'series')
    seedCache(dir, 'a.flac', { mtimeMs: 0, size: 0, duration: '1:30', durationSeconds: 90 })
    expect(await getTrackDuration(join(dir, 'a.flac'))).toBe(90)
  })

  it('analyzeLoopPoints returns cached candidates', async () => {
    const dir = makeDir(ws, 'mod', 'series')
    const candidate = { rank: 1, score: 99, loopStart: 1, loopEnd: 2, seam: 'smooth' }
    seedCache(dir, 'song-name.flac', {
      mtimeMs: 0,
      size: 0,
      duration: '0:30',
      durationSeconds: 30,
      candidates: [candidate]
    })

    const res = await analyzeLoopPoints(ws.root, join(dir, 'song-name.flac'))
    expect(res.candidates).toEqual([candidate])
    expect(res.track.name).toBe('Song Name')
    expect(res.track.duration).toBe('0:30')
    expect(res.track.converted).toBe(false)
  })

  it('extractWaveformPeaks returns cached peaks', async () => {
    const dir = makeDir(ws, 'mod', 'series')
    seedCache(dir, 'a.flac', {
      mtimeMs: 0,
      size: 0,
      duration: '0:30',
      durationSeconds: 30,
      peaks: [0.1, 0.2, 0.3]
    })
    expect(await extractWaveformPeaks(join(dir, 'a.flac'))).toEqual([0.1, 0.2, 0.3])
  })
})

describe('rejectNus3Track', () => {
  it('deletes the staged .nus3audio and forgets its decision', () => {
    const dir = makeDir(ws, 'mod', 'series')
    const validate = join(dir, 'songs-to-validate')
    writeFile(validate, 'w.nus3audio', 'x')
    writeFile(validate, '.conversions.json', JSON.stringify({ 'w.flac': { mode: 'loop' } }))

    rejectNus3Track(dir, 'w.flac')

    expect(existsSync(join(validate, 'w.nus3audio'))).toBe(false)
    expect(loadConversions(dir)).toEqual({})
  })
})
