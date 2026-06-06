import { _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtempSync, mkdirSync, writeFileSync, rmSync, cpSync, existsSync } from 'fs'
import { tmpdir } from 'os'
import { join, resolve, dirname } from 'path'
import { fileURLToPath } from 'url'

const __dirname = dirname(fileURLToPath(import.meta.url))

export interface E2EWorkspace {
  root: string
  cleanup(): void
}

export function createWorkspace(): E2EWorkspace {
  const root = mkdtempSync(join(tmpdir(), 'umb-e2e-'))
  return {
    root,
    cleanup: () => rmSync(root, { recursive: true, force: true })
  }
}

export function seedMod(
  ws: E2EWorkspace,
  modName: string,
  seriesName: string,
  opts: { tracksCsv: string; seriesToml?: string; songOrderToml?: string }
): string {
  const dir = join(ws.root, 'Mods', 'MusicMods', modName, seriesName)
  mkdirSync(dir, { recursive: true })
  writeFileSync(join(dir, 'tracks.csv'), opts.tracksCsv, 'utf8')
  if (opts.seriesToml) {
    writeFileSync(join(dir, 'series.toml'), opts.seriesToml, 'utf8')
  }
  if (opts.songOrderToml) {
    writeFileSync(join(dir, 'song_order.toml'), opts.songOrderToml, 'utf8')
  }
  return dir
}

export async function launchApp(workspace: E2EWorkspace): Promise<ElectronApplication> {
  const mainPath = resolve(__dirname, '..', 'dist', 'main', 'index.js')
  return electron.launch({
    args: [mainPath],
    env: {
      ...process.env,
      UMB_WORKSPACE: workspace.root,
      NODE_ENV: 'test'
    }
  })
}

export async function firstWindow(app: ElectronApplication): Promise<Page> {
  const page = await app.firstWindow()
  await page.waitForLoadState('domcontentloaded')
  return page
}

/** Walks up from this file to the UltimateMusicBuilder working tree (contains Sma5h.sln). */
export function repoRoot(): string {
  let dir = __dirname
  // eslint-disable-next-line no-constant-condition
  while (true) {
    if (existsSync(join(dir, 'Sma5h.sln'))) return dir
    const parent = dirname(dir)
    if (parent === dir) throw new Error('repo root (Sma5h.sln) not found above ' + __dirname)
    dir = parent
  }
}

/** Absolute path to Tests/TestData/configured-mod in the repo. */
export function configuredModSource(): string {
  return join(repoRoot(), 'Tests', 'TestData', 'configured-mod')
}

/** Copies the real configured-mod into <workspace>/Mods/MusicMods/<modName>. Returns the mod dir. */
export function seedTestDataMod(ws: E2EWorkspace, modName = 'test-mod'): string {
  const dest = join(ws.root, 'Mods', 'MusicMods', modName)
  cpSync(configuredModSource(), dest, { recursive: true })
  return dest
}

/** Copies one configured-mod series into a standalone temp dir (for absolute-path actions). */
export function copyConfiguredSeries(seriesId: string): { dir: string; cleanup(): void } {
  const base = mkdtempSync(join(tmpdir(), 'umb-e2e-series-'))
  const dir = join(base, seriesId)
  cpSync(join(configuredModSource(), seriesId), dir, { recursive: true })
  return { dir, cleanup: () => rmSync(base, { recursive: true, force: true }) }
}

/** Copies a tool folder from repo Tools/ into <workspace>/Tools/ (e.g. 'UltimateTexCli'). */
export function seedTool(ws: E2EWorkspace, toolFolder: string): void {
  cpSync(
    join(repoRoot(), 'Tools', toolFolder),
    join(ws.root, 'Tools', toolFolder),
    { recursive: true }
  )
}
