import { createHash } from 'crypto'
import { existsSync, readFileSync, readdirSync, statSync } from 'fs'
import { join, extname, relative } from 'path'

const HASHED = new Set(['.prc', '.msbt', '.bin'])
const MANIFEST_EXT = new Set(['.nus3audio', '.nus3bank'])
const MANIFEST_FILE = 'nus3-manifest.json'

export interface BaselineReport {
  missingHashed: string[]
  mismatchedHashed: { path: string; expected: string; actual: string }[]
  extraHashed: string[]
  missingNus3: string[]
  mismatchedNus3: { path: string; expected: number; actual: number }[]
  extraNus3: string[]
  setupError: string | null
  isClean: boolean
}

function walk(root: string): string[] {
  const out: string[] = []
  if (!existsSync(root)) return out
  for (const entry of readdirSync(root)) {
    const full = join(root, entry)
    if (statSync(full).isDirectory()) out.push(...walk(full))
    else out.push(full)
  }
  return out
}

function toRel(root: string, file: string): string {
  return relative(root, file).split('\\').join('/')
}

function hashTree(root: string): Record<string, string> {
  const result: Record<string, string> = {}
  for (const file of walk(root)) {
    if (!HASHED.has(extname(file).toLowerCase())) continue
    if (file.toLowerCase().endsWith(MANIFEST_FILE)) continue
    result[toRel(root, file)] = createHash('sha256').update(readFileSync(file)).digest('hex')
  }
  return result
}

/** {relPath: size} for every nus3audio/nus3bank under dir. */
export function buildManifest(root: string): Record<string, number> {
  const result: Record<string, number> = {}
  for (const file of walk(root)) {
    if (!MANIFEST_EXT.has(extname(file).toLowerCase())) continue
    result[toRel(root, file)] = statSync(file).size
  }
  return result
}

function diff(
  expectedHashes: Record<string, string>,
  expectedManifest: Record<string, number>,
  actualDir: string,
  setupError: string | null
): BaselineReport {
  const actualHashes = hashTree(actualDir)
  const actualManifest = buildManifest(actualDir)

  const r: BaselineReport = {
    missingHashed: [], mismatchedHashed: [], extraHashed: [],
    missingNus3: [], mismatchedNus3: [], extraNus3: [],
    setupError, isClean: false
  }

  for (const [rel, hash] of Object.entries(expectedHashes)) {
    if (!(rel in actualHashes)) r.missingHashed.push(rel)
    else if (actualHashes[rel] !== hash) r.mismatchedHashed.push({ path: rel, expected: hash, actual: actualHashes[rel] })
  }
  for (const rel of Object.keys(actualHashes)) if (!(rel in expectedHashes)) r.extraHashed.push(rel)

  for (const [rel, size] of Object.entries(expectedManifest)) {
    if (!(rel in actualManifest)) r.missingNus3.push(rel)
    else if (actualManifest[rel] !== size) r.mismatchedNus3.push({ path: rel, expected: size, actual: actualManifest[rel] })
  }
  for (const rel of Object.keys(actualManifest)) if (!(rel in expectedManifest)) r.extraNus3.push(rel)

  r.isClean = !r.setupError &&
    r.missingHashed.length === 0 && r.mismatchedHashed.length === 0 && r.extraHashed.length === 0 &&
    r.missingNus3.length === 0 && r.mismatchedNus3.length === 0 && r.extraNus3.length === 0
  return r
}

/** Symmetric directory compare (both sides hold real files). Used by the build differential. */
export function compareDirs(expectedDir: string, actualDir: string): BaselineReport {
  const setup = existsSync(expectedDir) ? null : `Expected dir does not exist: ${expectedDir}`
  return diff(hashTree(expectedDir), buildManifest(expectedDir), actualDir, setup)
}

/** Compare against a committed baseline dir that holds hashed files + a nus3 manifest FILE. */
export function compareToBaseline(actualDir: string, baselineDir: string): BaselineReport {
  if (!existsSync(baselineDir)) {
    return diff({}, {}, actualDir, `Baseline dir does not exist: ${baselineDir}`)
  }
  const manifestPath = join(baselineDir, MANIFEST_FILE)
  let manifest: Record<string, number> = {}
  if (existsSync(manifestPath)) {
    const raw = JSON.parse(readFileSync(manifestPath, 'utf8')) as Record<string, { Size: number }>
    manifest = Object.fromEntries(Object.entries(raw).map(([k, v]) => [k.split('\\').join('/'), v.Size]))
  }
  return diff(hashTree(baselineDir), manifest, actualDir, null)
}

/** Human-readable failure summary for test assertions. */
export function formatReport(report: BaselineReport): string {
  const lines: string[] = []
  if (report.setupError) lines.push('SETUP: ' + report.setupError)
  for (const m of report.mismatchedHashed) lines.push(`HASH MISMATCH ${m.path}: ${m.expected.slice(0, 12)} != ${m.actual.slice(0, 12)}`)
  for (const p of report.missingHashed) lines.push('HASH MISSING ' + p)
  for (const p of report.extraHashed) lines.push('HASH EXTRA ' + p)
  for (const m of report.mismatchedNus3) lines.push(`NUS3 SIZE ${m.path}: expected ${m.expected} got ${m.actual}`)
  for (const p of report.missingNus3) lines.push('NUS3 MISSING ' + p)
  for (const p of report.extraNus3) lines.push('NUS3 EXTRA ' + p)
  return lines.join('\n')
}
