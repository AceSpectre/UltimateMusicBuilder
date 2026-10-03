import { app, BrowserWindow, dialog, ipcMain, shell } from 'electron'
import { join, resolve } from 'path'
import { callCli, spawnCliAction, cancelCurrentAction, shutdownDaemon } from './cli'
import { loadVolumeConfig, saveVolumeConfig, decodeTrackPreview } from './config-volume'
import { getAppSettings, saveAppSettings, checkArcOutput } from './app-settings'
import { IPC } from '../shared/ipc-channels'
import type {
  AppSettings, CreateSeriesInput, LoopAnalysisOptions, Nus3TrackDecision, PlaylistAssignmentInput,
  SaveSeriesItem, SaveTrackItem, VolumeOverride
} from '../shared/types'

let mainWindow: BrowserWindow | null = null

function getWorkspacePath(): string {
  if (process.env['UMB_WORKSPACE']) {
    return resolve(process.env['UMB_WORKSPACE'])
  }
  if (app.isPackaged) {
    // Layout: <root>/desktop/resources/cli/UMB.CLI.exe and <root>/UMB.CLI.exe (standalone).
    // Workspace is <root> — the folder holding both the desktop/ subfolder and the CLI —
    // so the GUI and the standalone CLI share the same Resources/, Mods/, ArcOutput/.
    // process.resourcesPath = <root>/desktop/resources → up two = <root>.
    return resolve(process.resourcesPath, '..', '..')
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
  // CLI output can still arrive while the app quits, after the window is gone.
  const send = (channel: string, payload: unknown): void => {
    if (mainWindow && !mainWindow.isDestroyed()) mainWindow.webContents.send(channel, payload)
  }
  const sendLog = (line: unknown): void => send(IPC.LOG_STREAM, line)
  // Mod data logic lives in the CLI (UMB.CLI/Desktop/DesktopApi.cs); these handlers just forward.
  const api = <T>(action: string, input: unknown = {}): Promise<T> => callCli<T>(workspace, action, input, sendLog)

  ipcMain.handle(IPC.GET_WORKSPACE, () => workspace)
  ipcMain.handle(IPC.DEBUG_PING, () => ({ ok: true, workspace }))
  // Packaged builds carry the real version (set by the release action); in dev show "dev".
  ipcMain.handle(IPC.GET_APP_VERSION, () => (app.isPackaged ? app.getVersion() : 'dev'))

  ipcMain.handle(IPC.LIST_MODS, () => api('mods-list'))

  ipcMain.handle(IPC.LIST_MOD_SERIES, (_event, modPath: string) => api('mod-series-list', { modPath }))

  ipcMain.handle(IPC.GET_MOD_STATS, (_event, modPath: string) => api('mod-stats', { modPath }))

  ipcMain.handle(IPC.LOAD_TRACK_ORDER, (_event, seriesPath: string) => api('track-order-load', { seriesPath }))

  ipcMain.handle(IPC.SAVE_TRACK_ORDER, (_event, seriesPath: string, items: SaveTrackItem[]) =>
    api('track-order-save', { seriesPath, items }))

  ipcMain.handle(IPC.LOAD_SERIES_ORDER, (_event, modPath: string) => api('series-order-load', { modPath }))

  ipcMain.handle(IPC.SAVE_SERIES_ORDER, (_event, modPath: string, items: SaveSeriesItem[]) =>
    api('series-order-save', { modPath, items }))

  ipcMain.handle(IPC.CREATE_SERIES, (_event, modPath: string, input: CreateSeriesInput) =>
    api('series-create', { modPath, input }))

  ipcMain.handle(IPC.SET_SERIES_ICON, (_event, modPath: string, seriesId: string, iconDataUrl: string) =>
    api('series-set-icon', { modPath, seriesId, iconDataUrl }))

  ipcMain.handle(IPC.LIST_NUS3_SOURCES, (_event, seriesPath: string) => api('nus3-list-sources', { seriesPath }))

  ipcMain.handle(IPC.ANALYZE_LOOP_POINTS, (_event, seriesPath: string, filename: string, options?: LoopAnalysisOptions) =>
    api('nus3-analyze-loop', { filePath: join(seriesPath, filename), options }))

  ipcMain.handle(IPC.EXTRACT_WAVEFORM, (_event, seriesPath: string, filename: string, bars?: number) =>
    api('nus3-waveform', { filePath: join(seriesPath, filename), bars }))

  ipcMain.handle(IPC.GET_TRACK_DURATION, (_event, seriesPath: string, filename: string) =>
    api('nus3-duration', { filePath: join(seriesPath, filename) }))

  ipcMain.handle(IPC.GENERATE_LOOP_PREVIEW, (_event, seriesPath: string, filename: string, loopStart: number, loopEnd: number, previewLength: number) =>
    api('nus3-loop-preview', { filePath: join(seriesPath, filename), loopStart, loopEnd, previewLength }))

  ipcMain.handle(IPC.LOAD_NUS3_CONVERSIONS, (_event, seriesPath: string) => api('nus3-load-conversions', { seriesPath }))

  ipcMain.handle(IPC.CONVERT_NUS3_TRACK, (_event, seriesPath: string, decision: Nus3TrackDecision) =>
    api('nus3-convert-track', { seriesPath, trackId: decision.trackId, mode: decision.mode, candidate: decision.candidate ?? null }))

  ipcMain.handle(IPC.REJECT_NUS3_TRACK, (_event, seriesPath: string, trackId: string) =>
    api('nus3-reject', { seriesPath, trackId }))

  // Resolves an exit code (0) like the other CLI actions the renderer runs.
  ipcMain.handle(IPC.ACCEPT_NUS3_FILES, async (_event, seriesPath: string, deleteSources: boolean) => {
    await api('nus3-accept', { seriesPath, deleteSources })
    return 0
  })

  ipcMain.handle(IPC.LOAD_VOLUME_CONFIG, (_event, seriesPath: string, analyze: boolean) =>
    loadVolumeConfig(workspace, seriesPath, analyze, (line) => {
      const match = line.message.match(/^__LUFS_PROGRESS__\t(\d+)\t(\d+)\t(.+)$/)
      if (match) {
        send(IPC.VOLUME_PROGRESS, {
          completed: parseInt(match[1]),
          total: parseInt(match[2]),
          currentFile: match[3]
        })
        return
      }
      sendLog(line)
    })
  )

  ipcMain.handle(IPC.SAVE_VOLUME_CONFIG, (_event, seriesPath: string, overrides: VolumeOverride[]) =>
    saveVolumeConfig(workspace, seriesPath, overrides, sendLog)
  )

  ipcMain.handle(IPC.DECODE_TRACK_PREVIEW, (_event, seriesPath: string, filename: string) =>
    decodeTrackPreview(workspace, seriesPath, filename, sendLog)
  )

  ipcMain.handle(IPC.ANALYZE_EXTRACT_ICONS, (_event, compiledModPath: string, modPath: string) =>
    api('extract-icons-analyze', { compiledModPath, modPath }))

  ipcMain.handle(IPC.EXTRACT_ICONS, (_event, compiledModPath: string, modPath: string, mode: 'all' | 'missing-only') =>
    api('extract-icons-run', { compiledModPath, modPath, mode }))

  ipcMain.handle(IPC.ANALYZE_MERGE, (_event, modPaths: string[]) => api('merge-analyze', { modPaths }))

  ipcMain.handle(IPC.VALIDATE_MERGE_NAME, (_event, outputName: string) => api('merge-validate-name', { outputName }))

  ipcMain.handle(IPC.EXECUTE_MERGE, (_event, modPaths: string[], outputName: string, priorityModPath: string | null) =>
    api('merge-execute', { modPaths, outputName, priorityModPath }))

  ipcMain.handle(IPC.GET_PLAYLIST_INFO, () => api('playlist-info'))

  ipcMain.handle(IPC.LOAD_MANAGE_PLAYLISTS, (_event, modPath: string) => api('playlists-load', { modPath }))

  ipcMain.handle(IPC.SAVE_MANAGE_PLAYLISTS, (_event, modPath: string, assignments: PlaylistAssignmentInput[]) =>
    api('playlists-save', { modPath, assignments }))

  ipcMain.handle(IPC.CHECK_ARC_OUTPUT, () => checkArcOutput(workspace))

  ipcMain.handle(IPC.GET_APP_SETTINGS, () => getAppSettings(workspace))

  ipcMain.handle(IPC.SAVE_APP_SETTINGS, (_event, settings: AppSettings) =>
    saveAppSettings(workspace, settings)
  )

  ipcMain.handle(IPC.RUN_ACTION, (_event, action: string, args?: string[]) => {
    if (!mainWindow) return
    return spawnCliAction(workspace, action, args || [], sendLog)
  })

  ipcMain.handle(IPC.SELECT_FOLDER, async () => {
    if (!mainWindow) return null
    const result = await dialog.showOpenDialog(mainWindow, {
      properties: ['openDirectory']
    })
    if (result.canceled || result.filePaths.length === 0) return null
    return result.filePaths[0]
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

app.on('before-quit', () => {
  shutdownDaemon()
})
