import { cpSync, mkdirSync, readFileSync, writeFileSync, rmSync, mkdtempSync, existsSync } from 'fs'
import { tmpdir } from 'os'
import { join } from 'path'
import { execFileSync } from 'child_process'
import { repoRoot, configuredModSource } from './e2e-utils'

export interface IsolatedBuild {
  wsRoot: string
  cliProjectDir: string
  arcOutput: string
  cleanup(): void
}

/**
 * Creates <tmp>/ with a copied UMB.CLI project (absolute project refs + absolute-path
 * appsettings.json) and the configured-mod as the only mod, so a build is fully isolated.
 */
export function prepareIsolatedBuild(): IsolatedBuild {
  const repo = repoRoot()
  const wsRoot = mkdtempSync(join(tmpdir(), 'umb-e2e-build-'))
  const cliProjectDir = join(wsRoot, 'UMB.CLI')

  // 1. Copy UMB.CLI source (skip bin/obj).
  cpSync(join(repo, 'UMB.CLI'), cliProjectDir, {
    recursive: true,
    filter: (src) => !/[\\/](bin|obj)[\\/]/.test(src) && !src.endsWith(`${'\\'}bin`) && !src.endsWith(`${'\\'}obj`)
  })

  // 2. Rewrite the two relative refs in the csproj to absolute repo paths.
  const csprojPath = join(cliProjectDir, 'UMB.CLI.csproj')
  let csproj = readFileSync(csprojPath, 'utf8')
  csproj = csproj.replace('Include="..\\Sma5h\\', `Include="${repo}\\Sma5h\\`)
  csproj = csproj.replace('<HintPath>..\\Tools\\', `<HintPath>${repo}\\Tools\\`)
  writeFileSync(csprojPath, csproj, 'utf8')

  // 3. Absolute-path appsettings.json (cwd-independent; deterministic build settings).
  const fwd = (p: string): string => p.split('\\').join('/')
  const appsettings = {
    GameResourcesPath: fwd(join(repo, 'Resources', 'Game')),
    ResourcesPath: fwd(join(repo, 'Resources')),
    OutputPath: fwd(join(wsRoot, 'ArcOutput')),
    ToolsPath: fwd(join(repo, 'Tools')),
    TempPath: fwd(join(wsRoot, 'Temp')),
    LogPath: fwd(join(wsRoot, 'Log')),
    SkipOutputPathCleanupConfirmation: true,
    Sma5hMusic: {
      ModPath: fwd(join(wsRoot, 'Mods', 'MusicMods')),
      CachePath: fwd(join(wsRoot, 'Cache')),
      EnableAudioCaching: false,
      AudioConversionFormat: 'idsp',
      DefaultLocale: 'en_us',
      GlobalVolumeMultiplier: 1.5,
      PlaylistMapping: { GenerationMode: 'Manual', AutoMappingIncidence: 0, AutoMapping: {} },
      LufsNormalization: { Enabled: false, TargetLufs: -14, MaxGainMultiplier: 4, LufsCacheFileName: 'LUFS.csv' }
    },
    Sma5hStagePlaylist: { ModFile: 'Mods/StagePlaylistMod/metadata_stage_playlists.json' }
  }
  writeFileSync(join(cliProjectDir, 'appsettings.json'), JSON.stringify(appsettings, null, 2), 'utf8')

  // 4. Seed the only mod.
  const modDir = join(wsRoot, 'Mods', 'MusicMods', 'test-mod')
  mkdirSync(join(wsRoot, 'Mods', 'MusicMods'), { recursive: true })
  cpSync(configuredModSource(), modDir, { recursive: true })

  return {
    wsRoot,
    cliProjectDir,
    arcOutput: join(wsRoot, 'ArcOutput'),
    cleanup: () => rmSync(wsRoot, { recursive: true, force: true })
  }
}

/**
 * Runs the CLI build directly (the reference run). Throws on non-zero exit,
 * surfacing the captured dotnet stdout/stderr in the error so a build failure
 * is diagnosable (otherwise execFileSync only reports "Command failed").
 */
export function runCliBuild(b: IsolatedBuild): void {
  try {
    execFileSync('dotnet', ['run', '--project', b.cliProjectDir, '--no-launch-profile', '--', 'build'], {
      cwd: b.wsRoot,
      stdio: 'pipe',
      timeout: 300_000
    })
  } catch (err) {
    const e = err as { stdout?: Buffer; stderr?: Buffer; message?: string }
    const out = e.stdout?.toString() ?? ''
    const errOut = e.stderr?.toString() ?? ''
    throw new Error(`CLI reference build failed: ${e.message}\n--- stdout ---\n${out}\n--- stderr ---\n${errOut}`)
  }
}

/** Copies a directory tree (e.g. snapshot ArcOutput aside before the desktop run). */
export function snapshot(srcDir: string, destDir: string): void {
  if (existsSync(destDir)) rmSync(destDir, { recursive: true, force: true })
  cpSync(srcDir, destDir, { recursive: true })
}
