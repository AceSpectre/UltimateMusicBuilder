import { contextBridge, ipcRenderer } from 'electron'
import { IPC } from '../shared/ipc-channels'
import type {
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
} from '../shared/types'

/** Subscribes to a broadcast channel; returns the unsubscribe function. */
const subscribe = <T>(channel: string) => (cb: (value: T) => void): (() => void) => {
  const handler = (_event: unknown, value: T): void => cb(value)
  ipcRenderer.on(channel, handler)
  return () => { ipcRenderer.removeListener(channel, handler) }
}

const api = {
  platform: process.platform,
  getWorkspace: (): Promise<string> => ipcRenderer.invoke(IPC.GET_WORKSPACE),
  getAppVersion: (): Promise<string> => ipcRenderer.invoke(IPC.GET_APP_VERSION),
  debugPing: (): Promise<DebugPingResult> => ipcRenderer.invoke(IPC.DEBUG_PING),
  listMods: (): Promise<ModInfo[]> => ipcRenderer.invoke(IPC.LIST_MODS),
  listModSeries: (modPath: string): Promise<ModSeriesInfo[]> => ipcRenderer.invoke(IPC.LIST_MOD_SERIES, modPath),
  getModStats: (modPath: string): Promise<ModStats> => ipcRenderer.invoke(IPC.GET_MOD_STATS, modPath),
  loadTrackOrder: (seriesPath: string): Promise<TrackOrderData> => ipcRenderer.invoke(IPC.LOAD_TRACK_ORDER, seriesPath),
  saveTrackOrder: (seriesPath: string, items: SaveTrackItem[]): Promise<TrackOrderData> => ipcRenderer.invoke(IPC.SAVE_TRACK_ORDER, seriesPath, items),
  loadSeriesOrder: (modPath: string): Promise<SeriesOrderData> => ipcRenderer.invoke(IPC.LOAD_SERIES_ORDER, modPath),
  saveSeriesOrder: (modPath: string, items: SaveSeriesItem[]): Promise<SeriesOrderData> => ipcRenderer.invoke(IPC.SAVE_SERIES_ORDER, modPath, items),
  createSeries: (modPath: string, input: CreateSeriesInput): Promise<SeriesOrderData> => ipcRenderer.invoke(IPC.CREATE_SERIES, modPath, input),
  setSeriesIcon: (modPath: string, seriesId: string, iconDataUrl: string): Promise<string> => ipcRenderer.invoke(IPC.SET_SERIES_ICON, modPath, seriesId, iconDataUrl),
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
  getPlaylistInfo: (): Promise<PlaylistInfoData> => ipcRenderer.invoke(IPC.GET_PLAYLIST_INFO),
  loadManagePlaylists: (modPath: string): Promise<ManagePlaylistsData> => ipcRenderer.invoke(IPC.LOAD_MANAGE_PLAYLISTS, modPath),
  saveManagePlaylists: (modPath: string, assignments: PlaylistAssignmentInput[]): Promise<ManagePlaylistsData> =>
    ipcRenderer.invoke(IPC.SAVE_MANAGE_PLAYLISTS, modPath, assignments),
  runAction: (action: string, args?: string[]) =>
    ipcRenderer.invoke(IPC.RUN_ACTION, action, args) as Promise<void>,
  selectFolder: (): Promise<string | null> => ipcRenderer.invoke(IPC.SELECT_FOLDER),
  checkArcOutput: (): Promise<boolean> => ipcRenderer.invoke(IPC.CHECK_ARC_OUTPUT),
  getAppSettings: (): Promise<{ globalVolumeMultiplier: number }> => ipcRenderer.invoke(IPC.GET_APP_SETTINGS),
  saveAppSettings: (settings: { globalVolumeMultiplier: number }): Promise<void> => ipcRenderer.invoke(IPC.SAVE_APP_SETTINGS, settings),
  cancelAction: () => { ipcRenderer.send(IPC.CANCEL_ACTION) },
  subscribeLogs: subscribe<LogLine>(IPC.LOG_STREAM),
  subscribeVolumeProgress: subscribe<VolumeProgress>(IPC.VOLUME_PROGRESS),
  windowMinimize: (): Promise<WindowActionResult> => ipcRenderer.invoke(IPC.WINDOW_MINIMIZE),
  windowFullscreen: (): Promise<WindowActionResult> => ipcRenderer.invoke(IPC.WINDOW_FULLSCREEN),
  windowClose: (): Promise<WindowActionResult> => ipcRenderer.invoke(IPC.WINDOW_CLOSE)
}

contextBridge.exposeInMainWorld('electron', { umb: api })
