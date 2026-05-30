import { _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'fs'
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
