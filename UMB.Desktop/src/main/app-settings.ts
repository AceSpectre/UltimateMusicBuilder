import { readFileSync, writeFileSync, existsSync, readdirSync } from 'fs'
import { join } from 'path'
import { app } from 'electron'
import type { AppSettings } from '../shared/types'

export type { AppSettings } from '../shared/types'

const DEFAULTS: AppSettings = {
  globalVolumeDb: 0,
  applyVolumeToVanilla: false,
  addModSongsToSmashBattle: true,
  smashBattleIncidence: 0,
  albumSelectionOnAllStages: true
}

// AppSettings field -> appsettings.json Sma5hMusic key.
const KEYS: Record<keyof AppSettings, string> = {
  globalVolumeDb: 'GlobalVolumeDb',
  applyVolumeToVanilla: 'ApplyVolumeToVanilla',
  addModSongsToSmashBattle: 'AddModSongsToSmashBattle',
  smashBattleIncidence: 'SmashBattleIncidence',
  albumSelectionOnAllStages: 'EnableAlbumSelectionOnAllStages'
}

const FIELDS = Object.keys(KEYS) as (keyof AppSettings)[]

// The CLI loads appsettings.json from its own directory (AppContext.BaseDirectory):
// the bundled cli/ folder when packaged, or the dev bin/ when running via `dotnet run`.
// Edit that same file so GUI changes actually reach the CLI.
function settingsPath(workspace: string): string {
  if (app.isPackaged) {
    return join(process.resourcesPath, 'cli', 'appsettings.json')
  }
  return join(workspace, 'UMB.CLI', 'bin', 'Debug', 'net8.0', 'appsettings.json')
}

function readJson(path: string): unknown {
  const text = readFileSync(path, 'utf-8')
  return JSON.parse(text.charCodeAt(0) === 0xFEFF ? text.slice(1) : text)
}

export function getAppSettings(workspace: string): AppSettings {
  try {
    const raw = readJson(settingsPath(workspace)) as Record<string, unknown>
    const music = (raw?.Sma5hMusic ?? {}) as Record<string, unknown>
    return Object.fromEntries(FIELDS.map((f) => [f, music[KEYS[f]] ?? DEFAULTS[f]])) as unknown as AppSettings
  } catch {
    return { ...DEFAULTS }
  }
}

export function checkArcOutput(workspace: string): boolean {
  try {
    const raw = readJson(settingsPath(workspace)) as Record<string, unknown>
    const outputDir = (raw?.OutputPath as string) || 'ArcOutput'
    const fullPath = join(workspace, outputDir)
    return existsSync(fullPath) && readdirSync(fullPath).length > 0
  } catch {
    return false
  }
}

export function saveAppSettings(workspace: string, settings: AppSettings): void {
  const path = settingsPath(workspace)
  const raw = readJson(path) as Record<string, Record<string, unknown>>
  raw.Sma5hMusic ??= {}
  for (const f of FIELDS) raw.Sma5hMusic[KEYS[f]] = settings[f]
  writeFileSync(path, JSON.stringify(raw, null, 2), 'utf-8')
}
