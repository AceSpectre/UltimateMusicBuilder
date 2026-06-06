import { readFileSync, writeFileSync, existsSync, readdirSync } from 'fs'
import { join } from 'path'

export interface AppSettings {
  globalVolumeMultiplier: number
}

const DEFAULTS: AppSettings = {
  globalVolumeMultiplier: 1.5
}

function settingsPath(workspace: string): string {
  return join(workspace, 'UMB.CLI', 'bin', 'Debug', 'net8.0', 'appsettings.json')
}

function readJson(path: string): unknown {
  const text = readFileSync(path, 'utf-8')
  return JSON.parse(text.charCodeAt(0) === 0xFEFF ? text.slice(1) : text)
}

export function getAppSettings(workspace: string): AppSettings {
  try {
    const raw = readJson(settingsPath(workspace)) as Record<string, unknown>
    const music = raw?.Sma5hMusic as Record<string, unknown> | undefined
    return {
      globalVolumeMultiplier: (music?.GlobalVolumeMultiplier as number) ?? DEFAULTS.globalVolumeMultiplier
    }
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
  raw.Sma5hMusic.GlobalVolumeMultiplier = settings.globalVolumeMultiplier
  writeFileSync(path, JSON.stringify(raw, null, 2), 'utf-8')
}
