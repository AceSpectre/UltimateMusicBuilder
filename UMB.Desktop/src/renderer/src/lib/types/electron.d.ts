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

export interface TrackFields {
  title: string
  game: string
  author: string
  copyright: string
  record_type: string
  special_category: string
  info1: string
  in_soundtest: string
}

export interface TrackOrderItem {
  id: string
  title: string
  subtitle: string
  bgmId: string
  filename: string
  isLocked: boolean
  originalIndex: number | null
  fields: TrackFields | null
  isPinchTarget: boolean
}

export interface SeriesGame {
  id: string
  name: string
}

export interface SaveTrackItem {
  id: string
  fields: TrackFields | null
}

export interface VanillaSongOption {
  infoId: string
  name: string
}

export interface DefaultTrackData {
  game: string
  author: string
  copyright: string
  record_type: string
}

export interface TrackOrderData {
  seriesName: string
  seriesPath: string
  isExistingSeries: boolean
  hasSongOrder: boolean
  games: SeriesGame[]
  vanillaSongs: VanillaSongOption[]
  defaultTrackData: DefaultTrackData | null
  items: TrackOrderItem[]
}

export interface SeriesFields {
  name: string
  seriesPlaylist: string
  playlistIncidence: number
  games: SeriesGame[]
  defaultGame: string
  defaultAuthor: string
  defaultCopyright: string
  defaultRecordType: string
  defaultVolume: number
}

export interface SeriesOrderItem {
  id: string
  name: string
  seriesId: string
  iconDataUrl: string | null
  originalIndex: number
  fields: SeriesFields
}

export interface SaveSeriesItem {
  id: string
  fields: SeriesFields | null
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

export interface AppSettings {
  globalVolumeMultiplier: number
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

export interface VolumeOverride {
  originalIndex: number
  volume: number
}

export interface VolumeProgress {
  completed: number
  total: number
  currentFile: string
}

export interface PlaylistInfo {
  id: string
  name: string
  series: string[]
  songCount: number
}

export interface StageSong {
  order: number
  bgmId: string
  name: string
}

export interface StageInfo {
  uiStageId: string
  name: string
  hidden: boolean
  seriesId: string
  seriesName: string
  playlistId: string
  playlistName: string
  order: number
  songs: StageSong[]
}

export interface PlaylistInfoData {
  playlists: PlaylistInfo[]
  stages: StageInfo[]
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
  saveTrackOrder(seriesPath: string, items: SaveTrackItem[]): Promise<TrackOrderData>
  loadSeriesOrder(modPath: string): Promise<SeriesOrderData>
  saveSeriesOrder(modPath: string, items: SaveSeriesItem[]): Promise<SeriesOrderData>
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
  analyzeExtractIcons(compiledModPath: string, modPath: string): Promise<ExtractIconsAnalysis>
  extractIcons(compiledModPath: string, modPath: string, mode: 'all' | 'missing-only'): Promise<ExtractIconsResult>
  analyzeMerge(modPaths: string[]): Promise<MergeAnalysis>
  validateMergeName(name: string): Promise<string | null>
  executeMerge(modPaths: string[], outputName: string, priorityModPath: string | null): Promise<MergeResult>
  getPlaylistInfo(): Promise<PlaylistInfoData>
  checkArcOutput(): Promise<boolean>
  getAppSettings(): Promise<AppSettings>
  saveAppSettings(settings: AppSettings): Promise<void>
  runAction(action: string, args?: string[]): Promise<void>
  selectFolder(): Promise<string | null>
  cancelAction(): void
  subscribeLogs(cb: (line: LogLine) => void): () => void
  subscribeVolumeProgress(cb: (progress: VolumeProgress) => void): () => void
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
