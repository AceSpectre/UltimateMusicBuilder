import { readFileSync, writeFileSync, unlinkSync, existsSync } from 'fs'
import { basename } from 'path'
import { spawnCliAction, type LogLine } from './cli'
import { tempPath } from './utils'
import type { VolumeConfigData, VolumeOverride, VolumeRowItem } from '../shared/types'

export type { VolumeConfigData, VolumeOverride, VolumeRowItem } from '../shared/types'

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
