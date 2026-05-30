import { app, BrowserWindow, ipcMain, shell } from 'electron'
import { join, resolve } from 'path'
import { listModSeries, listMods } from './mods'
import { loadTrackOrderData, saveTrackOrderData } from './order-tracks'
import { spawnCliAction, cancelCurrentAction } from './cli'
import { IPC } from '../shared/ipc-channels'

let mainWindow: BrowserWindow | null = null

function getWorkspacePath(): string {
  if (process.env['UMB_WORKSPACE']) {
    return resolve(process.env['UMB_WORKSPACE'])
  }
  if (app.isPackaged) {
    return resolve(process.resourcesPath, '..')
  }
  return resolve(__dirname, '..', '..', '..')
}

function createWindow(): void {
  mainWindow = new BrowserWindow({
    width: 1320,
    height: 820,
    minWidth: 900,
    minHeight: 600,
    frame: false,
    titleBarStyle: 'hidden',
    backgroundColor: '#09090b',
    show: false,
    webPreferences: {
      preload: join(__dirname, '..', 'preload', 'index.mjs'),
      sandbox: false,
      contextIsolation: true,
      nodeIntegration: false
    }
  })

  mainWindow.on('ready-to-show', () => {
    mainWindow?.show()
  })

  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    shell.openExternal(url)
    return { action: 'deny' }
  })

  if (process.env['ELECTRON_RENDERER_URL']) {
    mainWindow.loadURL(process.env['ELECTRON_RENDERER_URL'])
  } else {
    mainWindow.loadFile(join(__dirname, '..', 'renderer', 'index.html'))
  }
}

function registerIpcHandlers(): void {
  const workspace = getWorkspacePath()

  ipcMain.handle(IPC.GET_WORKSPACE, () => workspace)
  ipcMain.handle(IPC.DEBUG_PING, () => ({ ok: true, workspace }))

  ipcMain.handle(IPC.LIST_MODS, () => listMods(workspace))

  ipcMain.handle(IPC.LIST_MOD_SERIES, (_event, modPath: string) => listModSeries(workspace, modPath))

  ipcMain.handle(IPC.LOAD_TRACK_ORDER, (_event, seriesPath: string) => loadTrackOrderData(workspace, seriesPath))

  ipcMain.handle(IPC.SAVE_TRACK_ORDER, (_event, seriesPath: string, orderedIds: string[]) => saveTrackOrderData(workspace, seriesPath, orderedIds))

  ipcMain.handle(IPC.RUN_ACTION, (_event, action: string, args?: string[]) => {
    if (!mainWindow) return
    spawnCliAction(workspace, action, args || [], (line) => {
      mainWindow?.webContents.send(IPC.LOG_STREAM, line)
    })
  })

  ipcMain.on(IPC.CANCEL_ACTION, () => {
    cancelCurrentAction()
  })

  ipcMain.handle(IPC.WINDOW_MINIMIZE, () => {
    mainWindow?.minimize()
    return { ok: true, action: 'minimize' }
  })

  ipcMain.handle(IPC.WINDOW_FULLSCREEN, () => {
    if (!mainWindow) {
      return { ok: false, action: 'fullscreen' }
    }

    mainWindow.setFullScreen(!mainWindow.isFullScreen())
    return { ok: true, action: 'fullscreen', fullScreen: mainWindow.isFullScreen() }
  })

  ipcMain.handle(IPC.WINDOW_CLOSE, () => {
    mainWindow?.close()
    return { ok: true, action: 'close' }
  })
}

app.whenReady().then(() => {
  registerIpcHandlers()
  createWindow()
})

app.on('window-all-closed', () => {
  app.quit()
})
