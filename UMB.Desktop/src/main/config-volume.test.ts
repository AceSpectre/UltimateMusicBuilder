import { existsSync, readFileSync, writeFileSync } from 'fs'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { LogLine } from './cli'

// spawnCliAction is mocked; asserts the temp-file marshalling and cleanup.

const mocks = vi.hoisted(() => ({ spawnCliAction: vi.fn() }))
vi.mock('./cli', () => ({ spawnCliAction: mocks.spawnCliAction }))

import { loadVolumeConfig, saveVolumeConfig, decodeTrackPreview } from './config-volume'

const WS = '/ws'
const SERIES = '/mods/MusicMods/my-mod/dev'

interface Captured {
  action: string
  workspace: string
  inputPath: string
  input: any
  onLine: (line: LogLine) => void
}

let captured: Captured | null
let lines: LogLine[]

const onLine = (line: LogLine): void => {
  lines.push(line)
}

/**
 * Stands in for the CLI: records the request, then lets the test decide what the
 * action "produced" at the outputPath the caller chose.
 */
function stubCli(produce?: (input: any) => void): void {
  mocks.spawnCliAction.mockImplementation(
    async (workspace: string, action: string, args: string[], cb: (l: LogLine) => void) => {
      const input = JSON.parse(readFileSync(args[0], 'utf-8'))
      captured = { action, workspace, inputPath: args[0], input, onLine: cb }
      produce?.(input)
      return 0
    }
  )
}

const ANALYZE_RESULT = {
  seriesName: 'dev',
  globalVolumeMultiplier: 1.5,
  targetLufs: -14,
  maxMultiplier: 4,
  ffmpegAvailable: true,
  lufsCacheExists: true,
  items: [
    {
      originalIndex: 0,
      title: 'Karts',
      filename: 'karts.flac',
      hasMeasurement: true,
      measuredLufs: -18.5,
      autoGain: 1.68,
      wasClamped: false,
      userOverride: 1
    }
  ]
}

beforeEach(() => {
  mocks.spawnCliAction.mockReset()
  captured = null
  lines = []
})

describe('loadVolumeConfig', () => {
  it('sends seriesPath, outputPath and the analyze flag to config-volume-analyze', async () => {
    stubCli((input) => writeFileSync(input.outputPath, JSON.stringify(ANALYZE_RESULT), 'utf-8'))

    await loadVolumeConfig(WS, SERIES, true, onLine)

    expect(captured!.action).toBe('config-volume-analyze')
    expect(captured!.workspace).toBe(WS)
    expect(captured!.input.seriesPath).toBe(SERIES)
    expect(captured!.input.analyze).toBe(true)
    expect(captured!.input.outputPath).toMatch(/umb-volume-analyze-.*\.json$/)
    expect(captured!.onLine).toBe(onLine)
  })

  it('forwards analyze=false for a cache-only read', async () => {
    stubCli((input) => writeFileSync(input.outputPath, JSON.stringify(ANALYZE_RESULT), 'utf-8'))

    await loadVolumeConfig(WS, SERIES, false, onLine)
    expect(captured!.input.analyze).toBe(false)
  })

  it('returns the analysis with the series path the caller asked for', async () => {
    stubCli((input) => writeFileSync(input.outputPath, JSON.stringify(ANALYZE_RESULT), 'utf-8'))

    const data = await loadVolumeConfig(WS, SERIES, true, onLine)

    expect(data).toEqual({ ...ANALYZE_RESULT, seriesPath: SERIES })
  })

  it('falls back to the folder name when the CLI reports no series name', async () => {
    stubCli((input) =>
      writeFileSync(input.outputPath, JSON.stringify({ ...ANALYZE_RESULT, seriesName: '' }), 'utf-8')
    )

    const data = await loadVolumeConfig(WS, SERIES, true, onLine)
    expect(data.seriesName).toBe('dev')
  })

  it('defaults lufsCacheExists and items when the CLI omits them', async () => {
    stubCli((input) =>
      writeFileSync(
        input.outputPath,
        JSON.stringify({
          seriesName: 'dev',
          globalVolumeMultiplier: 1.5,
          targetLufs: -14,
          maxMultiplier: 4,
          ffmpegAvailable: true
        }),
        'utf-8'
      )
    )

    const data = await loadVolumeConfig(WS, SERIES, true, onLine)
    expect(data.lufsCacheExists).toBe(false)
    expect(data.items).toEqual([])
  })

  it('returns safe defaults when the CLI produces no output file', async () => {
    stubCli()

    const data = await loadVolumeConfig(WS, SERIES, true, onLine)

    expect(data).toEqual({
      seriesName: 'dev',
      seriesPath: SERIES,
      globalVolumeMultiplier: 1,
      targetLufs: -14,
      maxMultiplier: 4,
      ffmpegAvailable: false,
      lufsCacheExists: false,
      items: []
    })
  })

  it('deletes both temp files afterwards', async () => {
    let outputPath = ''
    stubCli((input) => {
      outputPath = input.outputPath
      writeFileSync(input.outputPath, JSON.stringify(ANALYZE_RESULT), 'utf-8')
    })

    await loadVolumeConfig(WS, SERIES, true, onLine)

    expect(existsSync(captured!.inputPath)).toBe(false)
    expect(existsSync(outputPath)).toBe(false)
  })

  it('still deletes the temp files when the CLI call rejects', async () => {
    let inputPath = ''
    mocks.spawnCliAction.mockImplementation(async (_ws, _a, args: string[]) => {
      inputPath = args[0]
      throw new Error('daemon died')
    })

    await expect(loadVolumeConfig(WS, SERIES, true, onLine)).rejects.toThrow('daemon died')
    expect(existsSync(inputPath)).toBe(false)
  })

  it('uses a distinct temp path per call', async () => {
    const paths: string[] = []
    stubCli((input) => {
      paths.push(input.outputPath)
      writeFileSync(input.outputPath, JSON.stringify(ANALYZE_RESULT), 'utf-8')
    })

    await loadVolumeConfig(WS, SERIES, true, onLine)
    await loadVolumeConfig(WS, SERIES, true, onLine)

    expect(paths[0]).not.toBe(paths[1])
  })
})

describe('saveVolumeConfig', () => {
  it('sends the overrides to config-volume-save', async () => {
    stubCli()
    const overrides = [
      { originalIndex: 0, volume: 0.5 },
      { originalIndex: 3, volume: 2 }
    ]

    await saveVolumeConfig(WS, SERIES, overrides, onLine)

    expect(captured!.action).toBe('config-volume-save')
    expect(captured!.input).toEqual({ seriesPath: SERIES, overrides })
    expect(captured!.onLine).toBe(onLine)
  })

  it('sends an empty override list unchanged', async () => {
    stubCli()
    await saveVolumeConfig(WS, SERIES, [], onLine)
    expect(captured!.input.overrides).toEqual([])
  })

  it('deletes the temp input file afterwards', async () => {
    stubCli()
    await saveVolumeConfig(WS, SERIES, [{ originalIndex: 0, volume: 1 }], onLine)
    expect(existsSync(captured!.inputPath)).toBe(false)
  })

  it('still deletes the temp input file when the CLI call rejects', async () => {
    let inputPath = ''
    mocks.spawnCliAction.mockImplementation(async (_ws, _a, args: string[]) => {
      inputPath = args[0]
      throw new Error('boom')
    })

    await expect(saveVolumeConfig(WS, SERIES, [], onLine)).rejects.toThrow('boom')
    expect(existsSync(inputPath)).toBe(false)
  })
})

describe('decodeTrackPreview', () => {
  const WAV = Buffer.from('RIFF____WAVEfmt fake pcm payload')

  it('sends seriesPath, filename and outputPath to config-volume-preview', async () => {
    stubCli((input) => writeFileSync(input.outputPath, WAV))

    await decodeTrackPreview(WS, SERIES, 'karts.flac', onLine)

    expect(captured!.action).toBe('config-volume-preview')
    expect(captured!.input.seriesPath).toBe(SERIES)
    expect(captured!.input.filename).toBe('karts.flac')
    expect(captured!.input.outputPath).toMatch(/umb-volume-preview-.*\.wav$/)
  })

  it('returns the decoded wav as a base64 data URL', async () => {
    stubCli((input) => writeFileSync(input.outputPath, WAV))

    const dataUrl = await decodeTrackPreview(WS, SERIES, 'karts.flac', onLine)

    expect(dataUrl).toBe(`data:audio/wav;base64,${WAV.toString('base64')}`)
  })

  it('returns null when the CLI produced no wav', async () => {
    stubCli()
    expect(await decodeTrackPreview(WS, SERIES, 'karts.flac', onLine)).toBeNull()
  })

  it('deletes both temp files afterwards', async () => {
    let outputPath = ''
    stubCli((input) => {
      outputPath = input.outputPath
      writeFileSync(input.outputPath, WAV)
    })

    await decodeTrackPreview(WS, SERIES, 'karts.flac', onLine)

    expect(existsSync(captured!.inputPath)).toBe(false)
    expect(existsSync(outputPath)).toBe(false)
  })

  it('still deletes the temp files when the CLI call rejects', async () => {
    let inputPath = ''
    mocks.spawnCliAction.mockImplementation(async (_ws, _a, args: string[]) => {
      inputPath = args[0]
      throw new Error('nope')
    })

    await expect(decodeTrackPreview(WS, SERIES, 'karts.flac', onLine)).rejects.toThrow('nope')
    expect(existsSync(inputPath)).toBe(false)
  })
})
