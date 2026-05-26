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

const api = {
  getWorkspace: (): Promise<string> => ipcRenderer.invoke(IPC.GET_WORKSPACE),
  listMods: (): Promise<ModInfo[]> => ipcRenderer.invoke(IPC.LIST_MODS),
  runAction: (action: string, args?: string[]) =>
    ipcRenderer.invoke(IPC.RUN_ACTION, action, args) as Promise<void>,
  cancelAction: () => { ipcRenderer.send(IPC.CANCEL_ACTION) },
  subscribeLogs: (cb: (line: LogLine) => void): (() => void) => {
    const handler = (_event: unknown, line: LogLine): void => cb(line)
    ipcRenderer.on(IPC.LOG_STREAM, handler)
    return () => { ipcRenderer.removeListener(IPC.LOG_STREAM, handler) }
  },
  windowMinimize: (): void => ipcRenderer.send('window:minimize'),
  windowMaximize: (): void => ipcRenderer.send('window:maximize'),
  windowClose: (): void => ipcRenderer.send('window:close')
}

contextBridge.exposeInMainWorld('electron', { umb: api })
