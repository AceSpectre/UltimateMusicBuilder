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

  cpSync(join(repo, 'UMB.CLI'), cliProjectDir, {
    recursive: true,
    filter: (src) => !/[\\/](bin|obj)[\\/]/.test(src) && !src.endsWith(`${'\\'}bin`) && !src.endsWith(`${'\\'}obj`)
  })

  const csprojPath = join(cliProjectDir, 'UMB.CLI.csproj')
  let csproj = readFileSync(csprojPath, 'utf8')
  csproj = csproj.replace('Include="..\\Sma5h\\', `Include="${repo}\\Sma5h\\`)
  csproj = csproj.replace('<HintPath>..\\Tools\\', `<HintPath>${repo}\\Tools\\`)
  writeFileSync(csprojPath, csproj, 'utf8')

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

/** Runs the CLI reference build. Throws with dotnet stdout/stderr on failure. */
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

/** Copies a directory tree, replacing the destination. */
export function snapshot(srcDir: string, destDir: string): void {
  if (existsSync(destDir)) rmSync(destDir, { recursive: true, force: true })
  cpSync(srcDir, destDir, { recursive: true })
}
