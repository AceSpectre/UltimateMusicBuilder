import { app, BrowserWindow, dialog, ipcMain, shell } from 'electron'
import { join, resolve } from 'path'
import { listModSeries, listMods, getModStats } from './mods'
import { loadTrackOrderData, saveTrackOrderData, type SaveTrackItem } from './order-tracks'
import { createSeries, loadSeriesOrderData, saveSeriesOrderData, setSeriesIcon, type CreateSeriesInput, type SaveSeriesItem } from './order-series'
import { spawnCliAction, cancelCurrentAction, shutdownDaemon } from './cli'
import {
  listNus3Sources, analyzeLoopPoints, extractWaveformPeaks, getTrackDuration, generateLoopPreview,
  loadConversions, convertNus3Track, rejectNus3Track, acceptNus3Files
} from './nus3-convert'
import type { Nus3TrackDecision, LoopAnalysisOptions } from './nus3-convert'
import { loadVolumeConfig, saveVolumeConfig, decodeTrackPreview, type VolumeOverride } from './config-volume'
import { getAppSettings, saveAppSettings, checkArcOutput, type AppSettings } from './app-settings'
import { analyzeExtractIcons, extractIcons } from './extract-icons'
import { analyzeMerge, validateOutputName, executeMerge } from './merge'
import { getPlaylistInfo } from './playlist-info'
import { loadManagePlaylists, saveManagePlaylists, type PlaylistAssignmentInput } from './manage-playlists'
import { IPC } from '../shared/ipc-channels'

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

  ipcMain.handle(IPC.GET_WORKSPACE, () => workspace)
  ipcMain.handle(IPC.DEBUG_PING, () => ({ ok: true, workspace }))
  // Packaged builds carry the real version (set by the release action); in dev show "dev".
  ipcMain.handle(IPC.GET_APP_VERSION, () => (app.isPackaged ? app.getVersion() : 'dev'))

  ipcMain.handle(IPC.LIST_MODS, () => listMods(workspace))

  ipcMain.handle(IPC.LIST_MOD_SERIES, (_event, modPath: string) => listModSeries(workspace, modPath))

  ipcMain.handle(IPC.GET_MOD_STATS, (_event, modPath: string) => getModStats(workspace, modPath))

  ipcMain.handle(IPC.LOAD_TRACK_ORDER, (_event, seriesPath: string) => loadTrackOrderData(workspace, seriesPath))

  ipcMain.handle(IPC.SAVE_TRACK_ORDER, (_event, seriesPath: string, items: SaveTrackItem[]) => saveTrackOrderData(workspace, seriesPath, items))

  ipcMain.handle(IPC.LOAD_SERIES_ORDER, (_event, modPath: string) => loadSeriesOrderData(workspace, modPath))

  ipcMain.handle(IPC.SAVE_SERIES_ORDER, (_event, modPath: string, items: SaveSeriesItem[]) => saveSeriesOrderData(workspace, modPath, items))

  ipcMain.handle(IPC.CREATE_SERIES, (_event, modPath: string, input: CreateSeriesInput) => createSeries(workspace, modPath, input))

  ipcMain.handle(IPC.SET_SERIES_ICON, (_event, modPath: string, seriesId: string, iconDataUrl: string) => setSeriesIcon(workspace, modPath, seriesId, iconDataUrl))

  ipcMain.handle(IPC.LIST_NUS3_SOURCES, (_event, seriesPath: string) => listNus3Sources(seriesPath))

  ipcMain.handle(IPC.ANALYZE_LOOP_POINTS, (_event, seriesPath: string, filename: string, options?: LoopAnalysisOptions) =>
    analyzeLoopPoints(join(seriesPath, filename), options)
  )

  ipcMain.handle(IPC.EXTRACT_WAVEFORM, (_event, seriesPath: string, filename: string, bars?: number) =>
    extractWaveformPeaks(join(seriesPath, filename), bars)
  )

  ipcMain.handle(IPC.GET_TRACK_DURATION, (_event, seriesPath: string, filename: string) =>
    getTrackDuration(join(seriesPath, filename))
  )

  ipcMain.handle(IPC.GENERATE_LOOP_PREVIEW, (_event, seriesPath: string, filename: string, loopStartSec: number, loopEndSec: number, previewLength: number) =>
    generateLoopPreview(join(seriesPath, filename), loopStartSec, loopEndSec, previewLength)
  )

  ipcMain.handle(IPC.LOAD_NUS3_CONVERSIONS, (_event, seriesPath: string) => loadConversions(seriesPath))

  ipcMain.handle(IPC.CONVERT_NUS3_TRACK, (_event, seriesPath: string, decision: Nus3TrackDecision) =>
    convertNus3Track(workspace, seriesPath, decision, (line) => {
      mainWindow?.webContents.send(IPC.LOG_STREAM, line)
    })
  )

  ipcMain.handle(IPC.REJECT_NUS3_TRACK, (_event, seriesPath: string, trackId: string) =>
    rejectNus3Track(seriesPath, trackId)
  )

  ipcMain.handle(IPC.ACCEPT_NUS3_FILES, (_event, seriesPath: string, deleteSources: boolean) =>
    acceptNus3Files(workspace, seriesPath, deleteSources, (line) => {
      mainWindow?.webContents.send(IPC.LOG_STREAM, line)
    })
  )

  ipcMain.handle(IPC.LOAD_VOLUME_CONFIG, (_event, seriesPath: string, analyze: boolean) =>
    loadVolumeConfig(workspace, seriesPath, analyze, (line) => {
      const match = line.message.match(/^__LUFS_PROGRESS__\t(\d+)\t(\d+)\t(.+)$/)
      if (match) {
        mainWindow?.webContents.send(IPC.VOLUME_PROGRESS, {
          completed: parseInt(match[1]),
          total: parseInt(match[2]),
          currentFile: match[3]
        })
        return
      }
      mainWindow?.webContents.send(IPC.LOG_STREAM, line)
    })
  )

  ipcMain.handle(IPC.SAVE_VOLUME_CONFIG, (_event, seriesPath: string, overrides: VolumeOverride[]) =>
    saveVolumeConfig(workspace, seriesPath, overrides, (line) => {
      mainWindow?.webContents.send(IPC.LOG_STREAM, line)
    })
  )

  ipcMain.handle(IPC.DECODE_TRACK_PREVIEW, (_event, seriesPath: string, filename: string) =>
    decodeTrackPreview(workspace, seriesPath, filename, (line) => {
      mainWindow?.webContents.send(IPC.LOG_STREAM, line)
    })
  )

  ipcMain.handle(IPC.ANALYZE_EXTRACT_ICONS, (_event, compiledModPath: string, modPath: string) =>
    analyzeExtractIcons(workspace, compiledModPath, modPath)
  )

  ipcMain.handle(IPC.EXTRACT_ICONS, (_event, compiledModPath: string, modPath: string, mode: 'all' | 'missing-only') =>
    extractIcons(workspace, compiledModPath, modPath, mode, (line) => {
      mainWindow?.webContents.send(IPC.LOG_STREAM, line)
    })
  )

  ipcMain.handle(IPC.ANALYZE_MERGE, (_event, modPaths: string[]) =>
    analyzeMerge(workspace, modPaths)
  )

  ipcMain.handle(IPC.VALIDATE_MERGE_NAME, (_event, name: string) =>
    validateOutputName(workspace, name)
  )

  ipcMain.handle(IPC.EXECUTE_MERGE, (_event, modPaths: string[], outputName: string, priorityModPath: string | null) =>
    executeMerge(workspace, modPaths, outputName, priorityModPath, (line) => {
      mainWindow?.webContents.send(IPC.LOG_STREAM, line)
    })
  )

  ipcMain.handle(IPC.GET_PLAYLIST_INFO, () => getPlaylistInfo(workspace))

  ipcMain.handle(IPC.LOAD_MANAGE_PLAYLISTS, (_event, modPath: string) => loadManagePlaylists(workspace, modPath))

  ipcMain.handle(IPC.SAVE_MANAGE_PLAYLISTS, (_event, modPath: string, assignments: PlaylistAssignmentInput[]) =>
    saveManagePlaylists(workspace, modPath, assignments))

  ipcMain.handle(IPC.CHECK_ARC_OUTPUT, () => checkArcOutput(workspace))

  ipcMain.handle(IPC.GET_APP_SETTINGS, () => getAppSettings(workspace))

  ipcMain.handle(IPC.SAVE_APP_SETTINGS, (_event, settings: AppSettings) =>
    saveAppSettings(workspace, settings)
  )

  ipcMain.handle(IPC.RUN_ACTION, (_event, action: string, args?: string[]) => {
    if (!mainWindow) return
    return spawnCliAction(workspace, action, args || [], (line) => {
      mainWindow?.webContents.send(IPC.LOG_STREAM, line)
    })
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
