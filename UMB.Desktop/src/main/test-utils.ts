import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'fs'
import { tmpdir } from 'os'
import { join } from 'path'

export interface SeriesFixture {
  tracksCsv: string
  seriesToml?: string
  songOrderToml?: string
}

export interface Workspace {
  root: string
  seriesPath(mod: string, series: string): string
  cleanup(): void
}

/**
 * Creates a throwaway workspace under the OS temp dir with the
 * Mods/MusicMods/<mod>/<series>/ layout the main-process modules expect.
 */
export function makeWorkspace(): Workspace {
  const root = mkdtempSync(join(tmpdir(), 'umb-test-'))
  return {
    root,
    seriesPath: (mod, series) => join(root, 'Mods', 'MusicMods', mod, series),
    cleanup: () => rmSync(root, { recursive: true, force: true })
  }
}

export function writeSeries(
  ws: Workspace,
  mod: string,
  series: string,
  fixture: SeriesFixture
): string {
  const dir = ws.seriesPath(mod, series)
  mkdirSync(dir, { recursive: true })
  writeFileSync(join(dir, 'tracks.csv'), fixture.tracksCsv, 'utf8')
  if (fixture.seriesToml !== undefined) {
    writeFileSync(join(dir, 'series.toml'), fixture.seriesToml, 'utf8')
  }
  if (fixture.songOrderToml !== undefined) {
    writeFileSync(join(dir, 'song_order.toml'), fixture.songOrderToml, 'utf8')
  }
  return dir
}

/** Creates a plain (non-series) directory inside a mod, optionally with files. */
export function makeDir(ws: Workspace, ...segments: string[]): string {
  const dir = join(ws.root, 'Mods', 'MusicMods', ...segments)
  mkdirSync(dir, { recursive: true })
  return dir
}
