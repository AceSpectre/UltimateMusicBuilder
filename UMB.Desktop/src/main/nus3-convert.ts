import { readdirSync, statSync, readFileSync, writeFileSync, unlinkSync, existsSync, mkdirSync } from 'fs'
import { join, basename, extname, dirname } from 'path'
import { execFile } from 'child_process'
import { promisify } from 'util'
import { tmpdir } from 'os'
import { spawnCliAction, type LogLine } from './cli'

const execFileAsync = promisify(execFile)

const AUDIO_EXTENSIONS = new Set(['.mp3', '.wav', '.flac', '.ogg'])
const VALIDATE_FOLDER = 'songs-to-validate'

export interface Nus3SourceTrack {
  id: string
  name: string
  src: string
  duration: string
  durationSeconds: number
  /** true when a matching .nus3audio already exists in songs-to-validate */
  converted: boolean
}

export interface LoopCandidate {
  rank: number
  score: number
  loopStart: number
  loopEnd: number
  loopLength: number
  loopStartStr: string
  loopEndStr: string
  loopLengthStr: string
  beatAligned: boolean
  bars: number | null
  tempo: number
  key: string
  noteDistance: number
  spectralSim: number
  rmsDelta: number
  seam: 'smooth' | 'good' | 'audible' | 'click'
  note: string
}

export interface Nus3AnalysisResult {
  track: Nus3SourceTrack
  candidates: LoopCandidate[]
}

/** pymusiclooper tuning passed from the convert view's settings panel. */
export interface LoopAnalysisOptions {
  /** --min-loop-duration (seconds). Overrides the duration multiplier when > 0. */
  minLoopDuration?: number
  /** --min-duration-multiplier (0<x<1). pymusiclooper default is 0.35. */
  minDurationMultiplier?: number
  /** --disable-pruning: keep loop points the initial pass would discard. */
  disablePruning?: boolean
  /** Bypass the analysis cache and re-run pymusiclooper. */
  force?: boolean
}

/**
 * Relaxed fallback used automatically when the default pass finds nothing:
 * a short minimum loop and no pruning surfaces short loops (e.g. boss themes)
 * that the default 0.35 duration multiplier rejects.
 */
const RELAXED_OPTIONS: LoopAnalysisOptions = {
  minLoopDuration: 2,
  disablePruning: true
}

export interface Nus3TrackDecision {
  trackId: string
  mode: 'loop' | 'end-to-end'
  candidate?: LoopCandidate
  status: 'accepted' | 'rejected' | 'skipped' | 'pending'
}

/** Persisted per-track conversion metadata, keyed by source filename (track id). */
export interface Nus3ConversionMeta {
  mode: 'loop' | 'end-to-end'
  candidate?: LoopCandidate
}

/** Cache entry for pymusiclooper analysis + waveform peaks, keyed by source filename. */
interface AnalysisCacheEntry {
  mtimeMs: number
  size: number
  duration: string
  durationSeconds: number
  candidates?: LoopCandidate[]
  peaks?: number[]
}

function formatDuration(seconds: number): string {
  const m = Math.floor(seconds / 60)
  const s = Math.floor(seconds % 60)
  return `${m}:${String(s).padStart(2, '0')}`
}

function formatTimestamp(seconds: number): string {
  const m = Math.floor(seconds / 60)
  const s = (seconds % 60).toFixed(1)
  return `${m}:${s.padStart(4, '0')}`
}

function prettifyName(filename: string): string {
  const ext = extname(filename)
  return basename(filename, ext)
    .replace(/[-_]/g, ' ')
    .replace(/\b\w/g, (c) => c.toUpperCase())
}

// ── JSON sidecar helpers (cache + conversion metadata live in songs-to-validate) ──

function validateDirOf(seriesPath: string): string {
  return join(seriesPath, VALIDATE_FOLDER)
}

function readJson<T>(path: string): T | null {
  try {
    if (!existsSync(path)) return null
    return JSON.parse(readFileSync(path, 'utf-8')) as T
  } catch {
    return null
  }
}

function writeJson(path: string, value: unknown): void {
  try {
    const dir = dirname(path)
    if (!existsSync(dir)) mkdirSync(dir, { recursive: true })
    writeFileSync(path, JSON.stringify(value, null, 2), 'utf-8')
  } catch {
    /* best-effort cache; ignore failures */
  }
}

function statSafe(path: string): { mtimeMs: number; size: number } | null {
  try {
    const s = statSync(path)
    return { mtimeMs: s.mtimeMs, size: s.size }
  } catch {
    return null
  }
}

function cachePath(seriesPath: string): string {
  return join(validateDirOf(seriesPath), '.analysis-cache.json')
}

function loadCache(seriesPath: string): Record<string, AnalysisCacheEntry> {
  return readJson<Record<string, AnalysisCacheEntry>>(cachePath(seriesPath)) ?? {}
}

function getCacheEntry(seriesPath: string, filename: string): AnalysisCacheEntry | null {
  const cache = loadCache(seriesPath)
  const entry = cache[filename]
  if (!entry) return null
  const stat = statSafe(join(seriesPath, filename))
  if (!stat) return entry
  // Invalidate if the source file changed since the cache was written.
  if (stat.mtimeMs !== entry.mtimeMs || stat.size !== entry.size) return null
  return entry
}

function updateCacheEntry(seriesPath: string, filename: string, patch: Partial<AnalysisCacheEntry>): void {
  const cache = loadCache(seriesPath)
  const stat = statSafe(join(seriesPath, filename))
  const existing = cache[filename] ?? { mtimeMs: 0, size: 0, duration: '0:00', durationSeconds: 0 }
  cache[filename] = {
    ...existing,
    ...patch,
    mtimeMs: stat?.mtimeMs ?? existing.mtimeMs,
    size: stat?.size ?? existing.size
  }
  writeJson(cachePath(seriesPath), cache)
}

function conversionsPath(seriesPath: string): string {
  return join(validateDirOf(seriesPath), '.conversions.json')
}

export function loadConversions(seriesPath: string): Record<string, Nus3ConversionMeta> {
  return readJson<Record<string, Nus3ConversionMeta>>(conversionsPath(seriesPath)) ?? {}
}

function saveConversionMeta(seriesPath: string, trackId: string, meta: Nus3ConversionMeta): void {
  const all = loadConversions(seriesPath)
  all[trackId] = meta
  writeJson(conversionsPath(seriesPath), all)
}

function removeConversionMeta(seriesPath: string, trackId: string): void {
  const all = loadConversions(seriesPath)
  if (trackId in all) {
    delete all[trackId]
    writeJson(conversionsPath(seriesPath), all)
  }
}

function nus3PathFor(seriesPath: string, trackId: string): string {
  const base = basename(trackId, extname(trackId))
  return join(validateDirOf(seriesPath), base + '.nus3audio')
}

// ── ffprobe ──

async function ffprobe(filePath: string): Promise<{ sampleRate: number; duration: number }> {
  try {
    const { stdout } = await execFileAsync('ffprobe', [
      '-v', 'error',
      '-select_streams', 'a:0',
      '-show_entries', 'stream=sample_rate:stream=duration',
      '-of', 'csv=p=0',
      filePath
    ])
    const parts = stdout.trim().split(',')
    const sampleRate = parseInt(parts[0], 10)
    const duration = parseFloat(parts[1])
    if (!isNaN(sampleRate) && !isNaN(duration)) {
      return { sampleRate, duration }
    }
  } catch { /* fall through */ }
  return { sampleRate: 0, duration: 0 }
}

// ── source listing ──

export function listNus3Sources(_workspace: string, seriesPath: string): Nus3SourceTrack[] {
  try {
    const entries = readdirSync(seriesPath)
    const validateDir = validateDirOf(seriesPath)
    const tracks: Nus3SourceTrack[] = []

    for (const entry of entries) {
      const ext = extname(entry).toLowerCase()
      if (!AUDIO_EXTENSIONS.has(ext)) continue

      const fullPath = join(seriesPath, entry)
      const stat = statSync(fullPath)
      if (!stat.isFile()) continue

      const base = basename(entry, ext)

      // Already accepted into the series folder (sibling .nus3audio) — done, hide it.
      if (existsSync(join(seriesPath, base + '.nus3audio'))) continue

      const converted = existsSync(join(validateDir, base + '.nus3audio'))

      tracks.push({
        id: entry,
        name: prettifyName(entry),
        src: entry,
        duration: '—',
        durationSeconds: 0,
        converted
      })
    }

    return tracks
  } catch {
    return []
  }
}

// ── pymusiclooper analysis (cached) ──

export async function analyzeLoopPoints(
  _workspace: string,
  filePath: string,
  options: LoopAnalysisOptions = {}
): Promise<Nus3AnalysisResult> {
  const seriesPath = dirname(filePath)
  const filename = basename(filePath)

  // Use the cache only for non-forced runs, and only when it holds actual loop
  // points — an empty cached result is retried so the relaxed fallback (or new
  // settings) gets a chance to find loops.
  if (!options.force) {
    const cached = getCacheEntry(seriesPath, filename)
    if (cached && cached.candidates && cached.candidates.length > 0) {
      return {
        track: {
          id: filename,
          name: prettifyName(filename),
          src: filename,
          duration: cached.duration,
          durationSeconds: cached.durationSeconds,
          converted: existsSync(nus3PathFor(seriesPath, filename))
        },
        candidates: cached.candidates
      }
    }
  }

  const probe = await ffprobe(filePath)

  const track: Nus3SourceTrack = {
    id: filename,
    name: prettifyName(filename),
    src: filename,
    duration: probe.duration > 0 ? formatDuration(probe.duration) : '0:00',
    durationSeconds: probe.duration,
    converted: existsSync(nus3PathFor(seriesPath, filename))
  }

  let candidates = await runPymusiclooper(filePath, probe.sampleRate, options)

  // Auto-fallback: when the default pass finds nothing and the caller didn't
  // already supply explicit loop options, retry once with relaxed settings.
  const explicit =
    options.minLoopDuration != null ||
    options.minDurationMultiplier != null ||
    options.disablePruning != null
  if (candidates.length === 0 && !explicit) {
    candidates = await runPymusiclooper(filePath, probe.sampleRate, RELAXED_OPTIONS)
  }

  updateCacheEntry(seriesPath, filename, {
    duration: track.duration,
    durationSeconds: track.durationSeconds,
    candidates
  })

  return { track, candidates }
}

async function runPymusiclooper(
  filePath: string,
  sampleRate: number,
  options: LoopAnalysisOptions = {}
): Promise<LoopCandidate[]> {
  try {
    const args = [
      'export-points',
      '--path', filePath,
      '--alt-export-top', '10',
      '--fmt', 'samples',
      '--export-to', 'stdout'
    ]
    if (options.minLoopDuration != null && options.minLoopDuration > 0) {
      args.push('--min-loop-duration', String(options.minLoopDuration))
    } else if (options.minDurationMultiplier != null) {
      args.push('--min-duration-multiplier', String(options.minDurationMultiplier))
    }
    if (options.disablePruning) {
      args.push('--disable-pruning')
    }

    const { stdout } = await execFileAsync('pymusiclooper', args, { timeout: 120000 })

    const candidates: LoopCandidate[] = []
    const lines = stdout.split('\n').filter((l) => l.trim().length > 0)
    const rate = sampleRate > 0 ? sampleRate : 48000
    console.log(`[nus3] pymusiclooper raw output: ${lines.length} line(s)`)

    for (const line of lines) {
      const parts = line.trim().split(/\s+/)
      if (parts.length < 5) continue

      const startSamples = parseInt(parts[0], 10)
      const endSamples = parseInt(parts[1], 10)
      const noteDistance = parseFloat(parts[2])
      const loudnessDiff = parseFloat(parts[3])
      const score = parseFloat(parts[4])

      if (isNaN(startSamples) || isNaN(endSamples) || isNaN(score)) continue

      const startSec = startSamples / rate
      const endSec = endSamples / rate
      const lengthSec = endSec - startSec

      const rank = candidates.length + 1

      let seam: 'smooth' | 'good' | 'audible' | 'click' = 'smooth'
      if (loudnessDiff > 3.0) seam = 'click'
      else if (loudnessDiff > 2.0) seam = 'audible'
      else if (loudnessDiff > 1.0) seam = 'good'

      const spectralSim = Math.max(0, Math.min(1, 1 - noteDistance))

      candidates.push({
        rank,
        score: Math.round(score * 1000) / 10,
        loopStart: Math.round(startSec * 10) / 10,
        loopEnd: Math.round(endSec * 10) / 10,
        loopLength: Math.round(lengthSec * 10) / 10,
        loopStartStr: formatTimestamp(startSec),
        loopEndStr: formatTimestamp(endSec),
        loopLengthStr: formatTimestamp(lengthSec),
        beatAligned: false,
        bars: null,
        tempo: 0,
        key: '',
        noteDistance: Math.round(noteDistance * 10000) / 10000,
        spectralSim,
        rmsDelta: Math.round(loudnessDiff * 10) / 10,
        seam,
        note: rank === 1 ? 'best match' : ''
      })
    }

    return candidates
  } catch {
    return []
  }
}

const previewDir = join(tmpdir(), 'umb-previews')

// ── duration probe (cached, no pymusiclooper) ──

/** Track duration in seconds from the analysis cache, or a cheap ffprobe. */
export async function getTrackDuration(filePath: string): Promise<number> {
  const seriesPath = dirname(filePath)
  const filename = basename(filePath)

  const cached = getCacheEntry(seriesPath, filename)
  if (cached && cached.durationSeconds > 0) return cached.durationSeconds

  const probe = await ffprobe(filePath)
  if (probe.duration > 0) {
    updateCacheEntry(seriesPath, filename, {
      duration: formatDuration(probe.duration),
      durationSeconds: probe.duration
    })
  }
  return probe.duration
}

// ── waveform peaks (cached) ──

export async function extractWaveformPeaks(filePath: string, bars: number = 140): Promise<number[]> {
  const seriesPath = dirname(filePath)
  const filename = basename(filePath)

  const cached = getCacheEntry(seriesPath, filename)
  if (cached && cached.peaks && cached.peaks.length > 0) {
    return cached.peaks
  }

  try {
    const { stdout } = await execFileAsync('ffprobe', [
      '-v', 'error',
      '-select_streams', 'a:0',
      '-show_entries', 'stream=duration',
      '-of', 'csv=p=0',
      filePath
    ])
    const duration = parseFloat(stdout.trim())
    if (isNaN(duration) || duration <= 0) return []

    const { stdout: rawPcm } = await execFileAsync('ffmpeg', [
      '-i', filePath,
      '-ac', '1',
      '-ar', String(Math.ceil(bars / duration)),
      '-f', 's16le',
      '-acodec', 'pcm_s16le',
      '-'
    ], { encoding: 'buffer' as unknown as string, maxBuffer: 1024 * 1024 })

    const buf = Buffer.from(rawPcm as unknown as Buffer)
    const samples = Math.floor(buf.length / 2)
    const peaks: number[] = []
    let maxAbs = 1

    for (let i = 0; i < samples; i++) {
      const val = Math.abs(buf.readInt16LE(i * 2))
      if (val > maxAbs) maxAbs = val
    }

    for (let i = 0; i < Math.min(samples, bars); i++) {
      peaks.push(Math.abs(buf.readInt16LE(i * 2)) / maxAbs)
    }

    while (peaks.length < bars) peaks.push(0)

    updateCacheEntry(seriesPath, filename, { peaks })
    return peaks
  } catch {
    return []
  }
}

export async function generateLoopPreview(
  filePath: string,
  loopStartSec: number,
  loopEndSec: number,
  previewLength: number
): Promise<string | null> {
  try {
    if (!existsSync(previewDir)) mkdirSync(previewDir, { recursive: true })

    // End-to-end loop (no chosen points): preview the song's tail → head transition.
    if (loopEndSec <= loopStartSec) {
      const probe = await ffprobe(filePath)
      loopStartSec = 0
      loopEndSec = probe.duration > 0 ? probe.duration : 0
      if (loopEndSec <= 0) return null
    }

    const halfLen = previewLength / 2
    const seg1Start = Math.max(0, loopEndSec - halfLen)
    const seg1End = loopEndSec
    const seg2Start = loopStartSec
    const seg2End = loopStartSec + halfLen

    const outPath = join(previewDir, `preview-${Date.now()}.wav`)

    const filter =
      `[0:a]atrim=start=${seg1Start.toFixed(4)}:end=${seg1End.toFixed(4)},asetpts=PTS-STARTPTS[a];` +
      `[0:a]atrim=start=${seg2Start.toFixed(4)}:end=${seg2End.toFixed(4)},asetpts=PTS-STARTPTS[b];` +
      `[a][b]concat=n=2:v=0:a=1`

    await execFileAsync('ffmpeg', [
      '-i', filePath,
      '-filter_complex', filter,
      outPath,
      '-y'
    ], { timeout: 30000 })

    if (!existsSync(outPath)) return null

    const wavBuf = readFileSync(outPath)
    const dataUrl = `data:audio/wav;base64,${wavBuf.toString('base64')}`
    try { unlinkSync(outPath) } catch { /* ignore */ }
    return dataUrl
  } catch {
    return null
  }
}

// ── conversion (per track) ──

/**
 * Converts a single source track to .nus3audio in songs-to-validate via the CLI
 * batch action, then persists its loop decision so the converted state survives
 * an app restart. Resolves true when the .nus3audio was produced.
 */
export async function convertNus3Track(
  workspace: string,
  seriesPath: string,
  decision: Nus3TrackDecision,
  onLine: (line: LogLine) => void
): Promise<boolean> {
  const batchInput = {
    seriesPath,
    decisions: [
      {
        filename: decision.trackId,
        mode: decision.mode,
        loopStartSamples: decision.candidate ? Math.round(decision.candidate.loopStart * 48000) : 0,
        loopEndSamples: decision.candidate ? Math.round(decision.candidate.loopEnd * 48000) : 0
      }
    ]
  }

  const jsonPath = join(tmpdir(), `umb-nus3-batch-${Date.now()}.json`)
  writeFileSync(jsonPath, JSON.stringify(batchInput, null, 2), 'utf-8')

  try {
    await spawnCliAction(workspace, 'nus3-convert-batch', [jsonPath], onLine)
  } finally {
    try { unlinkSync(jsonPath) } catch { /* ignore */ }
  }

  const produced = existsSync(nus3PathFor(seriesPath, decision.trackId))
  if (produced) {
    saveConversionMeta(seriesPath, decision.trackId, {
      mode: decision.mode,
      candidate: decision.candidate
    })
  }
  return produced
}

/**
 * Rejects a previously converted track: removes its .nus3audio from
 * songs-to-validate and forgets its persisted decision, returning it to the
 * convert queue.
 */
export function rejectNus3Track(seriesPath: string, trackId: string): void {
  const nus3 = nus3PathFor(seriesPath, trackId)
  try { if (existsSync(nus3)) unlinkSync(nus3) } catch { /* ignore */ }
  removeConversionMeta(seriesPath, trackId)
}

/**
 * Accepts every validated .nus3audio into the series folder (moves them out of
 * songs-to-validate, optionally deleting the source audio) via the CLI.
 */
export async function acceptNus3Files(
  workspace: string,
  seriesPath: string,
  deleteSources: boolean,
  onLine: (line: LogLine) => void
): Promise<number> {
  const jsonPath = join(tmpdir(), `umb-nus3-accept-${Date.now()}.json`)
  writeFileSync(jsonPath, JSON.stringify({ seriesPath, deleteSources }, null, 2), 'utf-8')

  try {
    return await spawnCliAction(workspace, 'accept-nus3-batch', [jsonPath], onLine)
  } finally {
    try { unlinkSync(jsonPath) } catch { /* ignore */ }
  }
}
