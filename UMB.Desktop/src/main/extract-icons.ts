import { existsSync, readdirSync, readFileSync, statSync, writeFileSync, unlinkSync, mkdirSync } from 'fs'
import { join, resolve, relative, isAbsolute, basename } from 'path'
import { execFile } from 'child_process'
import { tmpdir } from 'os'
import { randomUUID } from 'crypto'

export interface ExtractIconsAnalysis {
  compiledModPath: string
  modPath: string
  modName: string
  matched: ExtractIconMatch[]
  unmatched: string[]
}

export interface ExtractIconMatch {
  seriesId: string
  bntxPath: string
  hasExistingIcon: boolean
}

export interface ExtractIconsResult {
  extracted: number
  skipped: number
  failed: number
}

interface LogLine {
  timestamp: string
  level: 'info' | 'warn' | 'error'
  message: string
}

const BNTX_PREFIX = 'series_0_'

function nowTs(): string {
  return new Date().toLocaleTimeString('en-GB', { hour12: false })
}

function log(level: LogLine['level'], message: string): LogLine {
  return { timestamp: nowTs(), level, message }
}

function isChildOf(parentPath: string, childPath: string): boolean {
  const rel = relative(parentPath, childPath)
  return rel !== '' && !rel.startsWith('..') && !isAbsolute(rel)
}

function getMusicModsRoot(workspace: string): string {
  return resolve(workspace, 'Mods', 'MusicMods')
}

function resolveUltimateTexCli(workspace: string): string | null {
  // Tools/ lives in the shared workspace root in both dev and packaged builds
  // (the release archive ships Tools/ next to the standalone CLI at <root>).
  const toolsDir = resolve(workspace, 'Tools')

  const candidates = [
    join(toolsDir, 'UltimateTexCli', 'ultimate_tex_cli.exe'),
    join(toolsDir, 'UltimateTexCli', 'ultimate_tex_cli')
  ]

  for (const c of candidates) {
    if (existsSync(c)) return c
  }
  return null
}

function tryFixOverMipmapBntx(sourceBytes: Buffer): { patched: Buffer; oldMip: number; newMip: number } | null {
  if (sourceBytes.length < 0x40) return null
  if (sourceBytes[0] !== 0x42 || sourceBytes[1] !== 0x4E ||
      sourceBytes[2] !== 0x54 || sourceBytes[3] !== 0x58) return null

  let brti = -1
  for (let i = 0x20; i <= sourceBytes.length - 0x30; i++) {
    if (sourceBytes[i] === 0x42 && sourceBytes[i + 1] === 0x52 &&
        sourceBytes[i + 2] === 0x54 && sourceBytes[i + 3] === 0x49) {
      brti = i
      break
    }
  }
  if (brti < 0 || brti + 0x30 > sourceBytes.length) return null

  const mipOffset = brti + 0x16
  const mipCount = sourceBytes.readUInt16LE(mipOffset)
  const width = sourceBytes.readInt32LE(brti + 0x24)
  const height = sourceBytes.readInt32LE(brti + 0x28)

  if (width <= 0 || height <= 0 || mipCount <= 0) return null

  const maxDim = Math.max(width, height)
  let maxMipmaps = 0
  for (let v = maxDim; v > 0; v >>= 1) maxMipmaps++

  if (mipCount <= maxMipmaps) return null

  const copy = Buffer.from(sourceBytes)
  copy.writeUInt16LE(maxMipmaps, mipOffset)
  return { patched: copy, oldMip: mipCount, newMip: maxMipmaps }
}

export function analyzeExtractIcons(workspace: string, compiledModPath: string, modPath: string): ExtractIconsAnalysis {
  const modsDir = getMusicModsRoot(workspace)
  const resolvedModPath = resolve(modPath)
  if (!isChildOf(modsDir, resolvedModPath)) {
    throw new Error('Invalid mod path.')
  }

  const series0Dir = join(compiledModPath, 'ui', 'replace', 'series', 'series_0')
  if (!existsSync(series0Dir)) {
    throw new Error('No ui/replace/series/series_0/ folder found in compiled mod.')
  }

  const bntxFiles = readdirSync(series0Dir)
    .filter(f => f.startsWith(BNTX_PREFIX) && f.endsWith('.bntx'))
    .map(f => join(series0Dir, f))

  const matched: ExtractIconMatch[] = []
  const unmatched: string[] = []

  for (const bntxFile of bntxFiles) {
    const seriesId = basename(bntxFile, '.bntx').substring(BNTX_PREFIX.length)
    const targetSeriesDir = join(resolvedModPath, seriesId)

    if (existsSync(targetSeriesDir) && statSync(targetSeriesDir).isDirectory()) {
      const iconPath = join(targetSeriesDir, 'icon.png')
      matched.push({
        seriesId,
        bntxPath: bntxFile,
        hasExistingIcon: existsSync(iconPath)
      })
    } else {
      unmatched.push(seriesId)
    }
  }

  return {
    compiledModPath,
    modPath: resolvedModPath,
    modName: basename(resolvedModPath),
    matched,
    unmatched
  }
}

function execFileAsync(command: string, args: string[]): Promise<{ stdout: string; stderr: string }> {
  return new Promise((resolve, reject) => {
    execFile(command, args, { timeout: 30000 }, (error, stdout, stderr) => {
      if (error) {
        reject(new Error(stderr?.trim() || stdout?.trim() || `exit code ${error.code}`))
      } else {
        resolve({ stdout: stdout ?? '', stderr: stderr ?? '' })
      }
    })
  })
}

export async function extractIcons(
  workspace: string,
  compiledModPath: string,
  modPath: string,
  mode: 'all' | 'missing-only',
  onLine: (line: LogLine) => void
): Promise<ExtractIconsResult> {
  const modsDir = getMusicModsRoot(workspace)
  const resolvedModPath = resolve(modPath)
  if (!isChildOf(modsDir, resolvedModPath)) {
    throw new Error('Invalid mod path.')
  }

  const toolPath = resolveUltimateTexCli(workspace)
  if (!toolPath) {
    onLine(log('error', 'ultimate_tex_cli not found. Ensure Tools/UltimateTexCli/ contains the binary.'))
    return { extracted: 0, skipped: 0, failed: 0 }
  }

  const analysis = analyzeExtractIcons(workspace, compiledModPath, resolvedModPath)
  let extracted = 0
  let skipped = 0
  let failed = 0

  for (const match of analysis.matched) {
    if (mode === 'missing-only' && match.hasExistingIcon) {
      skipped++
      continue
    }

    const targetSeriesDir = join(resolvedModPath, match.seriesId)
    const outputPng = join(targetSeriesDir, 'icon.png')

    let toolInput = match.bntxPath
    let tempBntx: string | null = null

    try {
      const sourceBytes = readFileSync(match.bntxPath)
      const fix = tryFixOverMipmapBntx(sourceBytes)
      if (fix) {
        tempBntx = join(tmpdir(), `umb_ext_${randomUUID().replace(/-/g, '')}_${basename(match.bntxPath)}`)
        writeFileSync(tempBntx, fix.patched)
        toolInput = tempBntx
        onLine(log('info', `Patched BNTX mipmap count ${fix.oldMip}→${fix.newMip} for '${match.seriesId}'.`))
      }

      await execFileAsync(toolPath, [toolInput, outputPng])

      if (existsSync(outputPng)) {
        onLine(log('info', `Extracted icon for '${match.seriesId}'.`))
        extracted++
      } else {
        onLine(log('error', `Extraction produced no output for '${match.seriesId}'.`))
        failed++
      }
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err)
      onLine(log('error', `Failed to extract icon for '${match.seriesId}': ${msg}`))
      failed++
    } finally {
      if (tempBntx && existsSync(tempBntx)) {
        try { unlinkSync(tempBntx) } catch { /* best-effort */ }
      }
    }
  }

  onLine(log('info', `Done. Extracted: ${extracted}, Skipped: ${skipped}, Failed: ${failed}.`))
  return { extracted, skipped, failed }
}
