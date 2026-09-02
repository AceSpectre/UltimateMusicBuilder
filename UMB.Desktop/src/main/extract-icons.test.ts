import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { join } from 'path'

// Stub electron's app (hoisted above the imports).
vi.mock('electron', () => ({ app: { isPackaged: false } }))

import { analyzeExtractIcons, extractIcons, resolveUltimateTexCli } from './extract-icons'
import { makeWorkspace, makeDir, writeFile, type Workspace } from './test-utils'

let ws: Workspace

beforeEach(() => {
  ws = makeWorkspace()
})

afterEach(() => {
  ws.cleanup()
})

const BNTX_DIR = (root: string): string =>
  join(root, 'compiled', 'ui', 'replace', 'series', 'series_0')

describe('analyzeExtractIcons', () => {
  it('throws when the mod path escapes Mods/MusicMods', () => {
    expect(() =>
      analyzeExtractIcons(ws.root, join(ws.root, 'compiled'), join(ws.root, 'outside'))
    ).toThrow('Invalid mod path.')
  })

  it('throws when the compiled series_0 folder is missing', () => {
    const modPath = makeDir(ws, 'mymod')
    expect(() => analyzeExtractIcons(ws.root, join(ws.root, 'compiled'), modPath)).toThrow(
      /series_0/
    )
  })

  it('matches bntx files to series folders and reports unmatched ids', () => {
    const modPath = makeDir(ws, 'mymod')
    makeDir(ws, 'mymod', 'mario')
    makeDir(ws, 'mymod', 'persona')
    writeFile(join(modPath, 'mario'), 'icon.png', 'IMG')

    const bntx = BNTX_DIR(ws.root)
    writeFile(bntx, 'series_0_mario.bntx', 'B')
    writeFile(bntx, 'series_0_persona.bntx', 'B')
    writeFile(bntx, 'series_0_ghost.bntx', 'B')
    writeFile(bntx, 'series_0_skip.png', 'B')
    writeFile(bntx, 'unrelated.txt', 'B')

    const analysis = analyzeExtractIcons(ws.root, join(ws.root, 'compiled'), modPath)

    expect(analysis.modName).toBe('mymod')
    expect(analysis.unmatched).toEqual(['ghost'])

    const byId = Object.fromEntries(analysis.matched.map((m) => [m.seriesId, m]))
    expect(Object.keys(byId).sort()).toEqual(['mario', 'persona'])
    expect(byId.mario.hasExistingIcon).toBe(true)
    expect(byId.persona.hasExistingIcon).toBe(false)
  })
})

describe('extractIcons', () => {
  it('reports an error and extracts nothing when the CLI tool is absent', async () => {
    const modPath = makeDir(ws, 'mymod')
    const lines: { level: string; message: string }[] = []

    const result = await extractIcons(
      ws.root,
      join(ws.root, 'compiled'),
      modPath,
      'all',
      (l) => lines.push(l)
    )

    expect(result).toEqual({ extracted: 0, skipped: 0, failed: 0 })
    expect(lines.some((l) => l.level === 'error' && /ultimate_tex_cli not found/.test(l.message))).toBe(
      true
    )
  })

  it('selects only the executable for the requested platform', () => {
    writeFile(join(ws.root, 'Tools', 'UltimateTexCli'), 'ultimate_tex_cli.exe', 'windows')
    writeFile(join(ws.root, 'Tools', 'UltimateTexCli'), 'ultimate_tex_cli', 'unix')

    expect(resolveUltimateTexCli(ws.root, 'win32')).toMatch(/ultimate_tex_cli\.exe$/)
    expect(resolveUltimateTexCli(ws.root, 'darwin')).toMatch(/ultimate_tex_cli$/)
    expect(resolveUltimateTexCli(ws.root, 'linux')).toMatch(/ultimate_tex_cli$/)
  })

  it('does not fall back to a Windows executable on macOS', () => {
    writeFile(join(ws.root, 'Tools', 'UltimateTexCli'), 'ultimate_tex_cli.exe', 'windows')

    expect(resolveUltimateTexCli(ws.root, 'darwin')).toBeNull()
  })
})
