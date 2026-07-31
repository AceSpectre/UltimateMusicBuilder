// Domain types live in src/shared/types.ts (single source shared with the main
// process and preload); this module re-exports them for renderer imports and
// declares the window.electron bridge surface.
export * from '$shared/types'

import type {
  AppSettings,
  CreateSeriesInput,
  DebugPingResult,
  ExtractIconsAnalysis,
  ExtractIconsResult,
  LogLine,
  LoopAnalysisOptions,
  ManagePlaylistsData,
  MergeAnalysis,
  MergeResult,
  ModInfo,
  ModSeriesInfo,
  ModStats,
  Nus3AnalysisResult,
  Nus3ConversionMeta,
  Nus3SourceTrack,
  Nus3TrackDecision,
  PlaylistAssignmentInput,
  PlaylistInfoData,
  SaveSeriesItem,
  SaveTrackItem,
  SeriesOrderData,
  TrackOrderData,
  VolumeConfigData,
  VolumeOverride,
  VolumeProgress,
  WindowActionResult
} from '$shared/types'

export interface UmbApi {
  getAppVersion(): Promise<string>
  debugPing(): Promise<DebugPingResult>
  listMods(): Promise<ModInfo[]>
  listModSeries(modPath: string): Promise<ModSeriesInfo[]>
  getModStats(modPath: string): Promise<ModStats>
  loadTrackOrder(seriesPath: string): Promise<TrackOrderData>
  saveTrackOrder(seriesPath: string, items: SaveTrackItem[]): Promise<TrackOrderData>
  loadSeriesOrder(modPath: string): Promise<SeriesOrderData>
  saveSeriesOrder(modPath: string, items: SaveSeriesItem[]): Promise<SeriesOrderData>
  createSeries(modPath: string, input: CreateSeriesInput): Promise<SeriesOrderData>
  setSeriesIcon(modPath: string, seriesId: string, iconDataUrl: string): Promise<string>
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
  loadManagePlaylists(modPath: string): Promise<ManagePlaylistsData>
  saveManagePlaylists(modPath: string, assignments: PlaylistAssignmentInput[]): Promise<ManagePlaylistsData>
  getWorkspace(): Promise<string>
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
