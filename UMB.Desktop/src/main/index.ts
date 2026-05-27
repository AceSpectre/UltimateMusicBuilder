import { app, BrowserWindow, ipcMain, shell } from 'electron'
import { join, resolve } from 'path'
import { listMods } from './mods'
import { spawnCliAction, cancelCurrentAction } from './cli'
import { IPC } from '../shared/ipc-channels'

let mainWindow: BrowserWindow | null = null

function getWorkspacePath(): string {
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
      preload: join(__dirname, '..', 'preload', 'index.js'),
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

  ipcMain.handle(IPC.LIST_MODS, () => listMods(workspace))

  ipcMain.handle(IPC.RUN_ACTION, (_event, action: string, args?: string[]) => {
    if (!mainWindow) return
    spawnCliAction(workspace, action, args || [], (line) => {
      mainWindow?.webContents.send(IPC.LOG_STREAM, line)
    })
  })

  ipcMain.on(IPC.CANCEL_ACTION, () => {
    cancelCurrentAction()
  })

  ipcMain.on('window:minimize', () => mainWindow?.minimize())
  ipcMain.on('window:maximize', () => {
    if (mainWindow?.isMaximized()) {
      mainWindow.unmaximize()
    } else {
      mainWindow?.maximize()
    }
  })
  ipcMain.on('window:close', () => mainWindow?.close())
}

app.whenReady().then(() => {
  registerIpcHandlers()
  createWindow()
})

app.on('window-all-closed', () => {
  app.quit()
})
