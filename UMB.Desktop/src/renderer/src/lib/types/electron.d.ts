export interface LogLine {
  timestamp: string
  level: 'info' | 'warn' | 'error'
  message: string
}

export interface ModInfo {
  name: string
  path: string
}

export interface UmbApi {
  getWorkspace(): Promise<string>
  listMods(): Promise<ModInfo[]>
  runAction(action: string, args?: string[]): Promise<void>
  cancelAction(): void
  subscribeLogs(cb: (line: LogLine) => void): () => void
  windowMinimize(): void
  windowMaximize(): void
  windowClose(): void
}

declare global {
  interface Window {
    electron: {
      umb: UmbApi
    }
  }
}
