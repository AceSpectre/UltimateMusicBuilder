import { contextBridge, ipcRenderer } from 'electron'
import { IPC } from '../shared/ipc-channels'

export interface LogLine {
  timestamp: string
  level: 'info' | 'warn' | 'error'
  message: string
}

export interface ModInfo {
  name: string
  path: string
}

export interface ModSeriesInfo {
  name: string
  path: string
}

export interface ModStats {
  seriesCount: number
  trackCount: number
}

export interface TrackOrderItem {
  id: string
  title: string
  subtitle: string
  bgmId: string
  isLocked: boolean
  originalIndex: number | null
}

export interface TrackOrderData {
  seriesName: string
  seriesPath: string
  isExistingSeries: boolean
  hasSongOrder: boolean
  items: TrackOrderItem[]
}

export interface SeriesOrderItem {
  id: string
  name: string
  seriesId: string
  iconDataUrl: string | null
  originalIndex: number
}

export interface SeriesOrderData {
  modName: string
  modPath: string
  hasSeriesOrder: boolean
  items: SeriesOrderItem[]
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

export interface Nus3SourceTrack {
  id: string
  name: string
  src: string
  duration: string
  durationSeconds: number
  converted: boolean
}

export interface Nus3TrackDecision {
  trackId: string
  mode: 'loop' | 'end-to-end'
  candidate?: LoopCandidate
  status: 'accepted' | 'rejected' | 'skipped' | 'pending'
}

export interface Nus3ConversionMeta {
  mode: 'loop' | 'end-to-end'
  candidate?: LoopCandidate
}

export interface Nus3AnalysisResult {
  track: Nus3SourceTrack
  candidates: LoopCandidate[]
}

export interface LoopAnalysisOptions {
  minLoopDuration?: number
  minDurationMultiplier?: number
  disablePruning?: boolean
  force?: boolean
}

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
  items: VolumeRowItem[]
}

export interface VolumeOverride {
  originalIndex: number
  volume: number
}

export interface ExtractIconMatch {
  seriesId: string
  bntxPath: string
  hasExistingIcon: boolean
}

export interface ExtractIconsAnalysis {
  compiledModPath: string
  modPath: string
  modName: string
  matched: ExtractIconMatch[]
  unmatched: string[]
}

export interface ExtractIconsResult {
  extracted: number
  skipped: number
  failed: number
}

export interface MergeSeriesSource {
  modName: string
  modPath: string
  seriesPath: string
}

export interface MergeConflict {
  seriesName: string
  mods: string[]
}

export interface MergeAnalysis {
  modNames: string[]
  modPaths: string[]
  series: { name: string; sources: MergeSeriesSource[] }[]
  conflicts: MergeConflict[]
  totalSeries: number
}

export interface MergeResult {
  outputPath: string
  outputName: string
  totalSeries: number
  totalTracks: number
  conflictsResolved: number
}

export interface DebugPingResult {
  ok: boolean
  workspace: string
}

export interface WindowActionResult {
  ok: boolean
  action: 'minimize' | 'fullscreen' | 'close'
  fullScreen?: boolean
}

const api = {
  getWorkspace: (): Promise<string> => ipcRenderer.invoke(IPC.GET_WORKSPACE),
  debugPing: (): Promise<DebugPingResult> => ipcRenderer.invoke(IPC.DEBUG_PING),
  listMods: (): Promise<ModInfo[]> => ipcRenderer.invoke(IPC.LIST_MODS),
  listModSeries: (modPath: string): Promise<ModSeriesInfo[]> => ipcRenderer.invoke(IPC.LIST_MOD_SERIES, modPath),
  getModStats: (modPath: string): Promise<ModStats> => ipcRenderer.invoke(IPC.GET_MOD_STATS, modPath),
  loadTrackOrder: (seriesPath: string): Promise<TrackOrderData> => ipcRenderer.invoke(IPC.LOAD_TRACK_ORDER, seriesPath),
  saveTrackOrder: (seriesPath: string, orderedIds: string[]): Promise<TrackOrderData> => ipcRenderer.invoke(IPC.SAVE_TRACK_ORDER, seriesPath, orderedIds),
  loadSeriesOrder: (modPath: string): Promise<SeriesOrderData> => ipcRenderer.invoke(IPC.LOAD_SERIES_ORDER, modPath),
  saveSeriesOrder: (modPath: string, orderedIds: string[]): Promise<SeriesOrderData> => ipcRenderer.invoke(IPC.SAVE_SERIES_ORDER, modPath, orderedIds),
  listNus3Sources: (seriesPath: string): Promise<Nus3SourceTrack[]> => ipcRenderer.invoke(IPC.LIST_NUS3_SOURCES, seriesPath),
  analyzeLoopPoints: (seriesPath: string, filename: string, options?: LoopAnalysisOptions): Promise<Nus3AnalysisResult> => ipcRenderer.invoke(IPC.ANALYZE_LOOP_POINTS, seriesPath, filename, options),
  loadNus3Conversions: (seriesPath: string): Promise<Record<string, Nus3ConversionMeta>> => ipcRenderer.invoke(IPC.LOAD_NUS3_CONVERSIONS, seriesPath),
  convertNus3Track: (seriesPath: string, decision: Nus3TrackDecision): Promise<boolean> => ipcRenderer.invoke(IPC.CONVERT_NUS3_TRACK, seriesPath, decision),
  rejectNus3Track: (seriesPath: string, trackId: string): Promise<void> => ipcRenderer.invoke(IPC.REJECT_NUS3_TRACK, seriesPath, trackId),
  acceptNus3Files: (seriesPath: string, deleteSources: boolean): Promise<number> => ipcRenderer.invoke(IPC.ACCEPT_NUS3_FILES, seriesPath, deleteSources),
  loadVolumeConfig: (seriesPath: string, analyze = false): Promise<VolumeConfigData> => ipcRenderer.invoke(IPC.LOAD_VOLUME_CONFIG, seriesPath, analyze),
  saveVolumeConfig: (seriesPath: string, overrides: VolumeOverride[]): Promise<void> => ipcRenderer.invoke(IPC.SAVE_VOLUME_CONFIG, seriesPath, overrides),
  decodeTrackPreview: (seriesPath: string, filename: string): Promise<string | null> => ipcRenderer.invoke(IPC.DECODE_TRACK_PREVIEW, seriesPath, filename),
  extractWaveform: (seriesPath: string, filename: string, bars?: number): Promise<number[]> => ipcRenderer.invoke(IPC.EXTRACT_WAVEFORM, seriesPath, filename, bars),
  getTrackDuration: (seriesPath: string, filename: string): Promise<number> => ipcRenderer.invoke(IPC.GET_TRACK_DURATION, seriesPath, filename),
  generateLoopPreview: (seriesPath: string, filename: string, loopStartSec: number, loopEndSec: number, previewLength: number): Promise<string | null> => ipcRenderer.invoke(IPC.GENERATE_LOOP_PREVIEW, seriesPath, filename, loopStartSec, loopEndSec, previewLength),
  analyzeExtractIcons: (compiledModPath: string, modPath: string): Promise<ExtractIconsAnalysis> =>
    ipcRenderer.invoke(IPC.ANALYZE_EXTRACT_ICONS, compiledModPath, modPath),
  extractIcons: (compiledModPath: string, modPath: string, mode: 'all' | 'missing-only'): Promise<ExtractIconsResult> =>
    ipcRenderer.invoke(IPC.EXTRACT_ICONS, compiledModPath, modPath, mode),
  analyzeMerge: (modPaths: string[]): Promise<MergeAnalysis> =>
    ipcRenderer.invoke(IPC.ANALYZE_MERGE, modPaths),
  validateMergeName: (name: string): Promise<string | null> =>
    ipcRenderer.invoke(IPC.VALIDATE_MERGE_NAME, name),
  executeMerge: (modPaths: string[], outputName: string, priorityModPath: string | null): Promise<MergeResult> =>
    ipcRenderer.invoke(IPC.EXECUTE_MERGE, modPaths, outputName, priorityModPath),
  runAction: (action: string, args?: string[]) =>
    ipcRenderer.invoke(IPC.RUN_ACTION, action, args) as Promise<void>,
  selectFolder: (): Promise<string | null> => ipcRenderer.invoke(IPC.SELECT_FOLDER),
  checkArcOutput: (): Promise<boolean> => ipcRenderer.invoke(IPC.CHECK_ARC_OUTPUT),
  getAppSettings: (): Promise<{ globalVolumeMultiplier: number }> => ipcRenderer.invoke(IPC.GET_APP_SETTINGS),
  saveAppSettings: (settings: { globalVolumeMultiplier: number }): Promise<void> => ipcRenderer.invoke(IPC.SAVE_APP_SETTINGS, settings),
  cancelAction: () => { ipcRenderer.send(IPC.CANCEL_ACTION) },
  subscribeLogs: (cb: (line: LogLine) => void): (() => void) => {
    const handler = (_event: unknown, line: LogLine): void => cb(line)
    ipcRenderer.on(IPC.LOG_STREAM, handler)
    return () => { ipcRenderer.removeListener(IPC.LOG_STREAM, handler) }
  },
  subscribeVolumeProgress: (cb: (progress: { completed: number; total: number; currentFile: string }) => void): (() => void) => {
    const handler = (_event: unknown, progress: { completed: number; total: number; currentFile: string }): void => cb(progress)
    ipcRenderer.on(IPC.VOLUME_PROGRESS, handler)
    return () => { ipcRenderer.removeListener(IPC.VOLUME_PROGRESS, handler) }
  },
  windowMinimize: (): Promise<WindowActionResult> => ipcRenderer.invoke(IPC.WINDOW_MINIMIZE),
  windowFullscreen: (): Promise<WindowActionResult> => ipcRenderer.invoke(IPC.WINDOW_FULLSCREEN),
  windowClose: (): Promise<WindowActionResult> => ipcRenderer.invoke(IPC.WINDOW_CLOSE)
}

contextBridge.exposeInMainWorld('electron', { umb: api })
