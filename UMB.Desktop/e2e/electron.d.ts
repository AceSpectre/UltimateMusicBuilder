interface ElectronUmbApi {
  getWorkspace(): Promise<string>
  debugPing(): Promise<{ ok: boolean; workspace: string }>
  listMods(): Promise<Array<{ name: string; path: string }>>
  listModSeries(modPath: string): Promise<Array<{ name: string; path: string }>>
  loadTrackOrder(seriesPath: string): Promise<{
    seriesName: string
    seriesPath: string
    isExistingSeries: boolean
    hasSongOrder: boolean
    items: Array<{
      id: string
      title: string
      subtitle: string
      bgmId: string
      isLocked: boolean
      originalIndex: number | null
    }>
  }>
  saveTrackOrder(seriesPath: string, orderedIds: string[]): Promise<{
    seriesName: string
    seriesPath: string
    isExistingSeries: boolean
    hasSongOrder: boolean
    items: Array<{
      id: string
      title: string
      subtitle: string
      bgmId: string
      isLocked: boolean
      originalIndex: number | null
    }>
  }>
  runAction(action: string, args?: string[]): Promise<void>
  cancelAction(): void
  subscribeLogs(cb: (line: { timestamp: string; level: string; message: string }) => void): () => void
  windowMinimize(): Promise<{ ok: boolean; action: string }>
  windowFullscreen(): Promise<{ ok: boolean; action: string; fullScreen?: boolean }>
  windowClose(): Promise<{ ok: boolean; action: string }>
}

declare global {
  interface Window {
    electron: {
      umb: ElectronUmbApi
    }
  }
}

export {}
