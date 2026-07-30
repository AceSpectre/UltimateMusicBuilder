import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mkdirSync, readFileSync } from 'fs'
import { join } from 'path'
import { getAppSettings, checkArcOutput, saveAppSettings } from './app-settings'
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

describe('getAppSettings', () => {
  it('reads the configured GlobalVolumeMultiplier', () => {
    writeSettings(JSON.stringify({ Sma5hMusic: { GlobalVolumeMultiplier: 2.5 } }))
    expect(getAppSettings(ws.root).globalVolumeMultiplier).toBe(2.5)
  })

  it('defaults to 1.5 when the key is absent', () => {
    writeSettings(JSON.stringify({ Sma5hMusic: {} }))
    expect(getAppSettings(ws.root).globalVolumeMultiplier).toBe(1.5)
  })

  it('defaults to 1.5 when the settings file is missing', () => {
    expect(getAppSettings(ws.root).globalVolumeMultiplier).toBe(1.5)
  })

  it('tolerates a UTF-8 BOM prefix', () => {
    writeSettings('﻿' + JSON.stringify({ Sma5hMusic: { GlobalVolumeMultiplier: 3 } }))
    expect(getAppSettings(ws.root).globalVolumeMultiplier).toBe(3)
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
})

describe('saveAppSettings', () => {
  it('persists the multiplier while preserving other keys', () => {
    writeSettings(JSON.stringify({ OutputPath: 'ArcOutput', Sma5hMusic: { Foo: 1 } }))

    saveAppSettings(ws.root, { globalVolumeMultiplier: 3 })

    const raw = JSON.parse(readFileSync(join(SETTINGS_DIR(ws.root), 'appsettings.json'), 'utf8'))
    expect(raw.Sma5hMusic.GlobalVolumeMultiplier).toBe(3)
    expect(raw.Sma5hMusic.Foo).toBe(1)
    expect(raw.OutputPath).toBe('ArcOutput')
    expect(getAppSettings(ws.root).globalVolumeMultiplier).toBe(3)
  })

  it('creates the Sma5hMusic section if absent', () => {
    writeSettings(JSON.stringify({ OutputPath: 'ArcOutput' }))
    saveAppSettings(ws.root, { globalVolumeMultiplier: 2 })
    expect(getAppSettings(ws.root).globalVolumeMultiplier).toBe(2)
  })
})
