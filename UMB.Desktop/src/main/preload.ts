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
  analyzeLoopPoints: (seriesPath: string, filename: string): Promise<Nus3AnalysisResult> => ipcRenderer.invoke(IPC.ANALYZE_LOOP_POINTS, seriesPath, filename),
  loadNus3Conversions: (seriesPath: string): Promise<Record<string, Nus3ConversionMeta>> => ipcRenderer.invoke(IPC.LOAD_NUS3_CONVERSIONS, seriesPath),
  convertNus3Track: (seriesPath: string, decision: Nus3TrackDecision): Promise<boolean> => ipcRenderer.invoke(IPC.CONVERT_NUS3_TRACK, seriesPath, decision),
  rejectNus3Track: (seriesPath: string, trackId: string): Promise<void> => ipcRenderer.invoke(IPC.REJECT_NUS3_TRACK, seriesPath, trackId),
  acceptNus3Files: (seriesPath: string, deleteSources: boolean): Promise<number> => ipcRenderer.invoke(IPC.ACCEPT_NUS3_FILES, seriesPath, deleteSources),
  extractWaveform: (seriesPath: string, filename: string, bars?: number): Promise<number[]> => ipcRenderer.invoke(IPC.EXTRACT_WAVEFORM, seriesPath, filename, bars),
  generateLoopPreview: (seriesPath: string, filename: string, loopStartSec: number, loopEndSec: number, previewLength: number): Promise<string | null> => ipcRenderer.invoke(IPC.GENERATE_LOOP_PREVIEW, seriesPath, filename, loopStartSec, loopEndSec, previewLength),
  runAction: (action: string, args?: string[]) =>
    ipcRenderer.invoke(IPC.RUN_ACTION, action, args) as Promise<void>,
  cancelAction: () => { ipcRenderer.send(IPC.CANCEL_ACTION) },
  subscribeLogs: (cb: (line: LogLine) => void): (() => void) => {
    const handler = (_event: unknown, line: LogLine): void => cb(line)
    ipcRenderer.on(IPC.LOG_STREAM, handler)
    return () => { ipcRenderer.removeListener(IPC.LOG_STREAM, handler) }
  },
  windowMinimize: (): Promise<WindowActionResult> => ipcRenderer.invoke(IPC.WINDOW_MINIMIZE),
  windowFullscreen: (): Promise<WindowActionResult> => ipcRenderer.invoke(IPC.WINDOW_FULLSCREEN),
  windowClose: (): Promise<WindowActionResult> => ipcRenderer.invoke(IPC.WINDOW_CLOSE)
}

contextBridge.exposeInMainWorld('electron', { umb: api })
