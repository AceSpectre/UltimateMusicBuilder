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
  lufsCacheExists: boolean
  items: VolumeRowItem[]
}

export interface VolumeOverride {
  originalIndex: number
  volume: number
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

export interface UmbApi {
  getWorkspace(): Promise<string>
  debugPing(): Promise<DebugPingResult>
  listMods(): Promise<ModInfo[]>
  listModSeries(modPath: string): Promise<ModSeriesInfo[]>
  getModStats(modPath: string): Promise<ModStats>
  loadTrackOrder(seriesPath: string): Promise<TrackOrderData>
  saveTrackOrder(seriesPath: string, orderedIds: string[]): Promise<TrackOrderData>
  loadSeriesOrder(modPath: string): Promise<SeriesOrderData>
  saveSeriesOrder(modPath: string, orderedIds: string[]): Promise<SeriesOrderData>
  listNus3Sources(seriesPath: string): Promise<Nus3SourceTrack[]>
  analyzeLoopPoints(seriesPath: string, filename: string, options?: LoopAnalysisOptions): Promise<Nus3AnalysisResult>
  loadNus3Conversions(seriesPath: string): Promise<Record<string, Nus3ConversionMeta>>
  convertNus3Track(seriesPath: string, decision: Nus3TrackDecision): Promise<boolean>
  rejectNus3Track(seriesPath: string, trackId: string): Promise<void>
  acceptNus3Files(seriesPath: string, deleteSources: boolean): Promise<number>
  loadVolumeConfig(seriesPath: string, analyze?: boolean): Promise<VolumeConfigData>
  saveVolumeConfig(seriesPath: string, overrides: VolumeOverride[]): Promise<void>
  decodeTrackPreview(seriesPath: string, filename: string): Promise<string | null>
  extractWaveform(seriesPath: string, filename: string, bars?: number): Promise<number[]>
  getTrackDuration(seriesPath: string, filename: string): Promise<number>
  generateLoopPreview(seriesPath: string, filename: string, loopStartSec: number, loopEndSec: number, previewLength: number): Promise<string | null>
  runAction(action: string, args?: string[]): Promise<void>
  cancelAction(): void
  subscribeLogs(cb: (line: LogLine) => void): () => void
  windowMinimize(): Promise<WindowActionResult>
  windowFullscreen(): Promise<WindowActionResult>
  windowClose(): Promise<WindowActionResult>
}

declare global {
  interface Window {
    electron: {
      umb: UmbApi
    }
  }
}
