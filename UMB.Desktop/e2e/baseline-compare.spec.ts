import { test, expect } from '@playwright/test'
import { cpSync, readFileSync, writeFileSync, rmSync, mkdtempSync, mkdirSync } from 'fs'
import { tmpdir } from 'os'
import { join } from 'path'
import { compareDirs, compareToBaseline, buildManifest } from './baseline-compare'
import { repoRoot } from './e2e-utils'

const BASELINE = join(repoRoot(), 'Tests', 'TestData', 'baselines', 'default-build')

let tmp: string
test.beforeEach(() => { tmp = mkdtempSync(join(tmpdir(), 'umb-cmp-')) })
test.afterEach(() => { rmSync(tmp, { recursive: true, force: true }) })

test('compareDirs reports clean when a directory is compared to a copy of itself', () => {
  const copy = join(tmp, 'copy')
  cpSync(BASELINE, copy, { recursive: true })
  const report = compareDirs(BASELINE, copy)
  expect(report.isClean).toBe(true)
})

// These exercise the comparer's hashed-file logic, which treats any .prc/.msbt/.bin
// purely by content — so they build a synthetic db tree rather than copying the
// real default-build baseline, whose *.prc files are gitignored (game-derived) and
// thus absent on CI.
const DB_REL = ['ui', 'param', 'database']
function seedDb(dir: string, files: Record<string, Buffer>): void {
  mkdirSync(join(dir, ...DB_REL), { recursive: true })
  for (const [name, bytes] of Object.entries(files)) {
    writeFileSync(join(dir, ...DB_REL, name), bytes)
  }
}

test('compareDirs flags a single flipped PRC byte as a hashed mismatch', () => {
  const expected = join(tmp, 'expected')
  const actual = join(tmp, 'actual')
  const bytes = Buffer.from([0x10, 0x20, 0x30, 0x40])
  const flipped = Buffer.from(bytes)
  flipped[0] = flipped[0] ^ 0xff
  seedDb(expected, { 'ui_bgm_db.prc': bytes })
  seedDb(actual, { 'ui_bgm_db.prc': flipped })

  const report = compareDirs(expected, actual)
  expect(report.isClean).toBe(false)
  expect(report.mismatchedHashed.map((m) => m.path)).toContain('ui/param/database/ui_bgm_db.prc')
})

test('compareDirs flags missing and extra hashed files', () => {
  const expected = join(tmp, 'expected')
  const actual = join(tmp, 'actual')
  seedDb(expected, { 'ui_series_db.prc': Buffer.from([1, 2, 3]) })
  seedDb(actual, { 'extra.prc': Buffer.from([1, 2, 3]) })

  const report = compareDirs(expected, actual)
  expect(report.missingHashed).toContain('ui/param/database/ui_series_db.prc')
  expect(report.extraHashed).toContain('ui/param/database/extra.prc')
})

test('compareToBaseline diffs nus3 sizes against a committed manifest file', () => {
  // The nus3-convert baseline ships only a manifest (no nus3 binaries).
  const baselineDir = join(repoRoot(), 'Tests', 'TestData', 'baselines', 'nus3-convert')
  const manifest = JSON.parse(
    readFileSync(join(baselineDir, 'nus3-manifest.json'), 'utf8')
  ) as Record<string, { Size: number }>
  const [name, entry] = Object.entries(manifest)[0]

  // produced dir whose nus3 size differs by 1 byte → mismatch
  const produced = join(tmp, 'produced')
  cpSync(baselineDir, produced, { recursive: true }) // copies the manifest (ignored on actual side)
  rmSync(join(produced, 'nus3-manifest.json'))
  writeFileSync(join(produced, name), Buffer.alloc(entry.Size + 1))

  const report = compareToBaseline(produced, baselineDir)
  expect(report.mismatchedNus3.map((m) => m.path)).toContain(name)
})

test('buildManifest records nus3 sizes keyed by forward-slash relative path', () => {
  cpSync(BASELINE, join(tmp, 'out'), { recursive: true }) // ensure dir exists then add a nus3
  writeFileSync(join(tmp, 'out', 'a.nus3audio'), Buffer.alloc(10))
  const m = buildManifest(join(tmp, 'out'))
  expect(m['a.nus3audio']).toBe(10)
})
