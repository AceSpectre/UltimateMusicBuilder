import { readFileSync, writeFileSync, unlinkSync, existsSync } from 'fs'
import { join, basename } from 'path'
import { tmpdir } from 'os'
import { spawnCliAction, type LogLine } from './cli'

/** One track row mirroring the Avalonia VolumeRowViewModel. */
export interface VolumeRowItem {
  originalIndex: number
  title: string
  filename: string
  hasMeasurement: boolean
  measuredLufs: number
  autoGain: number
  wasClamped: boolean
  userOverride: number
}

export interface VolumeConfigData {
  seriesName: string
  seriesPath: string
  globalVolumeMultiplier: number
  targetLufs: number
  maxMultiplier: number
  ffmpegAvailable: boolean
  lufsCacheExists: boolean
  items: VolumeRowItem[]
}

export interface VolumeOverride {
  originalIndex: number
  volume: number
}

/** What the CLI config-volume-analyze action writes to its output file. */
interface VolumeAnalyzeResult {
  seriesName: string
  globalVolumeMultiplier: number
  targetLufs: number
  maxMultiplier: number
  ffmpegAvailable: boolean
  lufsCacheExists: boolean
  items: VolumeRowItem[]
}

function tempPath(prefix: string, ext: string): string {
  // Math.random()/Date.now() avoided elsewhere in the codebase only inside the
  // workflow sandbox; here in the main process they are fine for temp names.
  return join(tmpdir(), `${prefix}-${Date.now()}-${Math.floor(Math.random() * 1e6)}.${ext}`)
}

/**
 * Runs LUFS analysis for every track in a series via the CLI and returns the
 * per-track loudness / auto-gain / override data for the renderer to display.
 */
export async function loadVolumeConfig(
  workspace: string,
  seriesPath: string,
  analyze: boolean,
  onLine: (line: LogLine) => void
): Promise<VolumeConfigData> {
  const outputPath = tempPath('umb-volume-analyze', 'json')
  const inputPath = tempPath('umb-volume-analyze-in', 'json')
  writeFileSync(inputPath, JSON.stringify({ seriesPath, outputPath, analyze }, null, 2), 'utf-8')

  try {
    await spawnCliAction(workspace, 'config-volume-analyze', [inputPath], onLine)

    if (!existsSync(outputPath)) {
      return {
        seriesName: basename(seriesPath),
        seriesPath,
        globalVolumeMultiplier: 1,
        targetLufs: -14,
        maxMultiplier: 4,
        ffmpegAvailable: false,
        lufsCacheExists: false,
        items: []
      }
    }

    const result = JSON.parse(readFileSync(outputPath, 'utf-8')) as VolumeAnalyzeResult
    return {
      seriesName: result.seriesName || basename(seriesPath),
      seriesPath,
      globalVolumeMultiplier: result.globalVolumeMultiplier,
      targetLufs: result.targetLufs,
      maxMultiplier: result.maxMultiplier,
      ffmpegAvailable: result.ffmpegAvailable,
      lufsCacheExists: result.lufsCacheExists ?? false,
      items: result.items ?? []
    }
  } finally {
    try { unlinkSync(inputPath) } catch { /* ignore */ }
    try { unlinkSync(outputPath) } catch { /* ignore */ }
  }
}

/** Persists per-track volume overrides back into the series' tracks.csv. */
export async function saveVolumeConfig(
  workspace: string,
  seriesPath: string,
  overrides: VolumeOverride[],
  onLine: (line: LogLine) => void
): Promise<void> {
  const inputPath = tempPath('umb-volume-save', 'json')
  writeFileSync(inputPath, JSON.stringify({ seriesPath, overrides }, null, 2), 'utf-8')

  try {
    await spawnCliAction(workspace, 'config-volume-save', [inputPath], onLine)
  } finally {
    try { unlinkSync(inputPath) } catch { /* ignore */ }
  }
}

/**
 * Decodes a single track to WAV (any Smash or standard format) and returns it
 * as a data URL the renderer can feed into the Web Audio API for gain preview.
 */
export async function decodeTrackPreview(
  workspace: string,
  seriesPath: string,
  filename: string,
  onLine: (line: LogLine) => void
): Promise<string | null> {
  const outputPath = tempPath('umb-volume-preview', 'wav')
  const inputPath = tempPath('umb-volume-preview-in', 'json')
  writeFileSync(inputPath, JSON.stringify({ seriesPath, filename, outputPath }, null, 2), 'utf-8')

  try {
    await spawnCliAction(workspace, 'config-volume-preview', [inputPath], onLine)
    if (!existsSync(outputPath)) return null
    const wavBuf = readFileSync(outputPath)
    return `data:audio/wav;base64,${wavBuf.toString('base64')}`
  } finally {
    try { unlinkSync(inputPath) } catch { /* ignore */ }
    try { unlinkSync(outputPath) } catch { /* ignore */ }
  }
}
