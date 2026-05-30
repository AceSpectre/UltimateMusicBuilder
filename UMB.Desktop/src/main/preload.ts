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
  loadTrackOrder: (seriesPath: string): Promise<TrackOrderData> => ipcRenderer.invoke(IPC.LOAD_TRACK_ORDER, seriesPath),
  saveTrackOrder: (seriesPath: string, orderedIds: string[]): Promise<TrackOrderData> => ipcRenderer.invoke(IPC.SAVE_TRACK_ORDER, seriesPath, orderedIds),
  loadSeriesOrder: (modPath: string): Promise<SeriesOrderData> => ipcRenderer.invoke(IPC.LOAD_SERIES_ORDER, modPath),
  saveSeriesOrder: (modPath: string, orderedIds: string[]): Promise<SeriesOrderData> => ipcRenderer.invoke(IPC.SAVE_SERIES_ORDER, modPath, orderedIds),
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
