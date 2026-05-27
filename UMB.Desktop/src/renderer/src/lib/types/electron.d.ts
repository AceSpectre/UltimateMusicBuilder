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
  loadTrackOrder(seriesPath: string): Promise<TrackOrderData>
  saveTrackOrder(seriesPath: string, orderedIds: string[]): Promise<TrackOrderData>
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
