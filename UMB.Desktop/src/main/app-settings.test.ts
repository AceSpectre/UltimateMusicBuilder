import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mkdirSync, readFileSync } from 'fs'
import { join } from 'path'
import { getAppSettings, checkArcOutput, saveAppSettings, type AppSettings } from './app-settings'
import { makeWorkspace, writeFile, type Workspace } from './test-utils'

// Stub electron's app so the dev settings path is used.
vi.mock('electron', () => ({ app: { isPackaged: false } }))

let ws: Workspace

beforeEach(() => {
  ws = makeWorkspace()
})

afterEach(() => {
  ws.cleanup()
})

const SETTINGS_DIR = (root: string): string => join(root, 'UMB.CLI', 'bin', 'Debug', 'net8.0')

function writeSettings(content: string): void {
  writeFile(SETTINGS_DIR(ws.root), 'appsettings.json', content)
}

function settings(overrides: Partial<AppSettings> = {}): AppSettings {
  return {
    globalVolumeDb: 0,
    applyVolumeToVanilla: false,
    addModSongsToSmashBattle: true,
    smashBattleIncidence: 0,
    albumSelectionOnAllStages: true,
    ...overrides
  }
}

describe('getAppSettings', () => {
  it('reads the configured GlobalVolumeDb', () => {
    writeSettings(JSON.stringify({ Sma5hMusic: { GlobalVolumeDb: 2.5 } }))
    expect(getAppSettings(ws.root).globalVolumeDb).toBe(2.5)
  })

  it('defaults to 0 dB when the key is absent', () => {
    writeSettings(JSON.stringify({ Sma5hMusic: {} }))
    expect(getAppSettings(ws.root).globalVolumeDb).toBe(0)
  })

  it('defaults to 0 dB when the settings file is missing', () => {
    expect(getAppSettings(ws.root).globalVolumeDb).toBe(0)
  })

  it('reads the experimental vanilla flag, defaulting to off', () => {
    writeSettings(JSON.stringify({ Sma5hMusic: { ApplyVolumeToVanilla: true } }))
    expect(getAppSettings(ws.root).applyVolumeToVanilla).toBe(true)
    writeSettings(JSON.stringify({ Sma5hMusic: {} }))
    expect(getAppSettings(ws.root).applyVolumeToVanilla).toBe(false)
  })

  it('reads the playlist and stage settings, defaulting to the build defaults', () => {
    writeSettings(JSON.stringify({ Sma5hMusic: {} }))
    expect(getAppSettings(ws.root)).toEqual(settings())

    writeSettings(JSON.stringify({ Sma5hMusic: { AddModSongsToSmashBattle: false, SmashBattleIncidence: 40, EnableAlbumSelectionOnAllStages: false } }))
    expect(getAppSettings(ws.root)).toEqual(settings({ addModSongsToSmashBattle: false, smashBattleIncidence: 40, albumSelectionOnAllStages: false }))
  })

  it('tolerates a UTF-8 BOM prefix', () => {
    writeSettings('﻿' + JSON.stringify({ Sma5hMusic: { GlobalVolumeDb: 3 } }))
    expect(getAppSettings(ws.root).globalVolumeDb).toBe(3)
  })
})

describe('checkArcOutput', () => {
  it('returns true when the default ArcOutput dir has files', () => {
    writeSettings('{}')
    writeFile(join(ws.root, 'ArcOutput'), 'foo.bin', 'x')
    expect(checkArcOutput(ws.root)).toBe(true)
  })

  it('respects a custom OutputPath', () => {
    writeSettings(JSON.stringify({ OutputPath: 'MyOut' }))
    writeFile(join(ws.root, 'MyOut'), 'foo.bin', 'x')
    expect(checkArcOutput(ws.root)).toBe(true)
  })

  it('returns false when the output dir is empty', () => {
    writeSettings('{}')
    mkdirSync(join(ws.root, 'ArcOutput'), { recursive: true })
    expect(checkArcOutput(ws.root)).toBe(false)
  })

  it('returns false when settings are missing', () => {
    expect(checkArcOutput(ws.root)).toBe(false)
  })

  it('returns false when the output dir does not exist', () => {
    writeSettings('{}')
    expect(checkArcOutput(ws.root)).toBe(false)
  })

  it('returns false when settings are unreadable', () => {
    writeSettings('{ not json')
    writeFile(join(ws.root, 'ArcOutput'), 'foo.bin', 'x')
    expect(checkArcOutput(ws.root)).toBe(false)
  })
})

describe('saveAppSettings', () => {
  it('persists the global dB boost while preserving other keys', () => {
    writeSettings(JSON.stringify({ OutputPath: 'ArcOutput', Sma5hMusic: { Foo: 1 } }))

    saveAppSettings(ws.root, settings({ globalVolumeDb: 3, applyVolumeToVanilla: true, smashBattleIncidence: 40, albumSelectionOnAllStages: false }))

    const raw = JSON.parse(readFileSync(join(SETTINGS_DIR(ws.root), 'appsettings.json'), 'utf8'))
    expect(raw.Sma5hMusic.GlobalVolumeDb).toBe(3)
    expect(raw.Sma5hMusic.ApplyVolumeToVanilla).toBe(true)
    expect(raw.Sma5hMusic.SmashBattleIncidence).toBe(40)
    expect(raw.Sma5hMusic.EnableAlbumSelectionOnAllStages).toBe(false)
    expect(raw.Sma5hMusic.Foo).toBe(1)
    expect(raw.OutputPath).toBe('ArcOutput')
    expect(getAppSettings(ws.root).globalVolumeDb).toBe(3)
  })

  it('creates the Sma5hMusic section if absent', () => {
    writeSettings(JSON.stringify({ OutputPath: 'ArcOutput' }))
    saveAppSettings(ws.root, settings({ globalVolumeDb: 2 }))
    expect(getAppSettings(ws.root).globalVolumeDb).toBe(2)
  })
})
