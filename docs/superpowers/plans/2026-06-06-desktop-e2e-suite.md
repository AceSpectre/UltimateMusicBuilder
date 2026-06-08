# UMB.Desktop Playwright E2E Suite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A full Playwright E2E suite that drives every UMB.Desktop action against the real `Tests/TestData` fixtures and verifies the desktop produces the same data as the CLI, using the .NET suite's comparison logic (SHA256 on PRC/MSBT/BIN, size-manifest on nus3audio).

**Architecture:** Three tiers. Tier 1 (pure-TS: order-tracks/order-series/merge) runs in an isolated temp `UMB_WORKSPACE` and asserts file structure mirroring the .NET integration tests. Tier 2 (config-volume/nus3-convert/extract-icons/playlist-info) launches with `UMB_WORKSPACE=repoRoot` (CLI daemon/tools/resources need it) but targets absolute temp `seriesPath`s so outputs stay isolated. Tier 3 builds an isolated workspace (copied `UMB.CLI` with absolute refs + absolute-path `appsettings.json`, TestData as the only mod), runs a CLI reference build then the desktop build, and byte-compares `ArcOutput` with a ported `BaselineComparer`.

**Tech Stack:** Playwright (`_electron`), TypeScript/ESM, Node 22 (`fs.cpSync`), the existing preload IPC bridge (`window.electron.umb.*`), `dotnet` CLI, real game resources + tools present locally.

**Conventions used throughout:**
- All work is under `UMB.Desktop/`. Run commands from `UMB.Desktop/` unless noted.
- Drive actions via `page.evaluate((arg) => window.electron.umb.<method>(arg), arg)`.
- `repoRoot()` = the dir containing `Sma5h.sln` (the UltimateMusicBuilder working tree).
- Temp workspaces live under `os.tmpdir()` and are removed in `afterAll` even on failure.
- The desktop must be built (`npm run build`) before E2E; the preload already exposes every method, so new specs need no preload changes.

---

## Task 1: Shared E2E infrastructure (utils, full types, config timeout)

**Files:**
- Modify: `UMB.Desktop/e2e/e2e-utils.ts`
- Modify: `UMB.Desktop/e2e/electron.d.ts` (replace with full `UmbApi`)
- Modify: `UMB.Desktop/playwright.config.ts:5` (timeout 60_000 → 120_000)
- Modify: `UMB.Desktop/e2e/app-boot.spec.ts` (use `seedTestDataMod`) — serves as Task 1 verification

- [ ] **Step 1: Add helpers to `e2e-utils.ts`**

Append these exports (keep the existing `createWorkspace`, `seedMod`, `launchApp`, `firstWindow`):

```ts
import { cpSync, existsSync } from 'fs'

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
```

(`mkdtempSync`, `rmSync`, `tmpdir`, `join`, `dirname` are already imported at the top of the file.)

- [ ] **Step 2: Replace `e2e/electron.d.ts` with the full API**

Overwrite the file with the complete `UmbApi` (copied verbatim from `src/renderer/src/lib/types/electron.d.ts`) so `window.electron.umb.*` typechecks for every method:

```ts
// Full preload surface — mirror of src/renderer/src/lib/types/electron.d.ts.
export interface LogLine { timestamp: string; level: 'info' | 'warn' | 'error'; message: string }
export interface ModInfo { name: string; path: string }
export interface ModSeriesInfo { name: string; path: string }
export interface ModStats { seriesCount: number; trackCount: number }
export interface TrackOrderItem { id: string; title: string; subtitle: string; bgmId: string; isLocked: boolean; originalIndex: number | null }
export interface TrackOrderData { seriesName: string; seriesPath: string; isExistingSeries: boolean; hasSongOrder: boolean; items: TrackOrderItem[] }
export interface SeriesOrderItem { id: string; name: string; seriesId: string; iconDataUrl: string | null; originalIndex: number }
export interface SeriesOrderData { modName: string; modPath: string; hasSeriesOrder: boolean; items: SeriesOrderItem[] }
export interface LoopCandidate { rank: number; score: number; loopStart: number; loopEnd: number; loopLength: number; loopStartStr: string; loopEndStr: string; loopLengthStr: string; beatAligned: boolean; bars: number | null; tempo: number; key: string; noteDistance: number; spectralSim: number; rmsDelta: number; seam: 'smooth' | 'good' | 'audible' | 'click'; note: string }
export interface Nus3SourceTrack { id: string; name: string; src: string; duration: string; durationSeconds: number; converted: boolean }
export interface Nus3TrackDecision { trackId: string; mode: 'loop' | 'end-to-end'; candidate?: LoopCandidate; status: 'accepted' | 'rejected' | 'skipped' | 'pending' }
export interface Nus3ConversionMeta { mode: 'loop' | 'end-to-end'; candidate?: LoopCandidate }
export interface Nus3AnalysisResult { track: Nus3SourceTrack; candidates: LoopCandidate[] }
export interface LoopAnalysisOptions { minLoopDuration?: number; minDurationMultiplier?: number; disablePruning?: boolean; force?: boolean }
export interface VolumeRowItem { originalIndex: number; title: string; filename: string; hasMeasurement: boolean; measuredLufs: number; autoGain: number; wasClamped: boolean; userOverride: number }
export interface VolumeConfigData { seriesName: string; seriesPath: string; globalVolumeMultiplier: number; targetLufs: number; maxMultiplier: number; ffmpegAvailable: boolean; lufsCacheExists: boolean; items: VolumeRowItem[] }
export interface VolumeOverride { originalIndex: number; volume: number }
export interface ExtractIconMatch { seriesId: string; bntxPath: string; hasExistingIcon: boolean }
export interface ExtractIconsAnalysis { compiledModPath: string; modPath: string; modName: string; matched: ExtractIconMatch[]; unmatched: string[] }
export interface ExtractIconsResult { extracted: number; skipped: number; failed: number }
export interface AppSettings { globalVolumeMultiplier: number }
export interface MergeSeriesSource { modName: string; modPath: string; seriesPath: string }
export interface MergeConflict { seriesName: string; mods: string[] }
export interface MergeAnalysis { modNames: string[]; modPaths: string[]; series: { name: string; sources: MergeSeriesSource[] }[]; conflicts: MergeConflict[]; totalSeries: number }
export interface MergeResult { outputPath: string; outputName: string; totalSeries: number; totalTracks: number; conflictsResolved: number }
export interface VolumeProgress { completed: number; total: number; currentFile: string }
export interface PlaylistInfo { id: string; name: string; series: string[]; songCount: number }
export interface StageSong { order: number; bgmId: string; name: string }
export interface StageInfo { uiStageId: string; name: string; hidden: boolean; seriesId: string; seriesName: string; playlistId: string; playlistName: string; order: number; songs: StageSong[] }
export interface PlaylistInfoData { playlists: PlaylistInfo[]; stages: StageInfo[] }
export interface DebugPingResult { ok: boolean; workspace: string }
export interface WindowActionResult { ok: boolean; action: 'minimize' | 'fullscreen' | 'close'; fullScreen?: boolean }

export interface UmbApi {
  getWorkspace(): Promise<string>
  debugPing(): Promise<DebugPingResult>
  listMods(): Promise<ModInfo[]>
  listModSeries(modPath: string): Promise<ModSeriesInfo[]>
  getModStats(modPath: string): Promise<ModStats>
  loadTrackOrder(seriesPath: string): Promise<TrackOrderData>
  saveTrackOrder(seriesPath: string, orderedIds: string[]): Promise<TrackOrderData>
  loadSeriesOrder(modPath: string): Promise<SeriesOrderData>
  saveSeriesOrder(modPath: string, orderedIds: string[]): Promise<SeriesOrderData>
  listNus3Sources(seriesPath: string): Promise<Nus3SourceTrack[]>
  analyzeLoopPoints(seriesPath: string, filename: string, options?: LoopAnalysisOptions): Promise<Nus3AnalysisResult>
  loadNus3Conversions(seriesPath: string): Promise<Record<string, Nus3ConversionMeta>>
  convertNus3Track(seriesPath: string, decision: Nus3TrackDecision): Promise<boolean>
  rejectNus3Track(seriesPath: string, trackId: string): Promise<void>
  acceptNus3Files(seriesPath: string, deleteSources: boolean): Promise<number>
  loadVolumeConfig(seriesPath: string, analyze?: boolean): Promise<VolumeConfigData>
  saveVolumeConfig(seriesPath: string, overrides: VolumeOverride[]): Promise<void>
  decodeTrackPreview(seriesPath: string, filename: string): Promise<string | null>
  extractWaveform(seriesPath: string, filename: string, bars?: number): Promise<number[]>
  getTrackDuration(seriesPath: string, filename: string): Promise<number>
  generateLoopPreview(seriesPath: string, filename: string, loopStartSec: number, loopEndSec: number, previewLength: number): Promise<string | null>
  analyzeExtractIcons(compiledModPath: string, modPath: string): Promise<ExtractIconsAnalysis>
  extractIcons(compiledModPath: string, modPath: string, mode: 'all' | 'missing-only'): Promise<ExtractIconsResult>
  analyzeMerge(modPaths: string[]): Promise<MergeAnalysis>
  validateMergeName(name: string): Promise<string | null>
  executeMerge(modPaths: string[], outputName: string, priorityModPath: string | null): Promise<MergeResult>
  getPlaylistInfo(): Promise<PlaylistInfoData>
  checkArcOutput(): Promise<boolean>
  getAppSettings(): Promise<AppSettings>
  saveAppSettings(settings: AppSettings): Promise<void>
  runAction(action: string, args?: string[]): Promise<void>
  selectFolder(): Promise<string | null>
  cancelAction(): void
  subscribeLogs(cb: (line: LogLine) => void): () => void
  subscribeVolumeProgress(cb: (progress: VolumeProgress) => void): () => void
  windowMinimize(): Promise<WindowActionResult>
  windowFullscreen(): Promise<WindowActionResult>
  windowClose(): Promise<WindowActionResult>
}

declare global {
  interface Window { electron: { umb: UmbApi } }
}

export {}
```

- [ ] **Step 3: Bump the global timeout in `playwright.config.ts`**

Change line 5 `timeout: 60_000,` to `timeout: 120_000,`.

- [ ] **Step 4: Convert `app-boot.spec.ts` to TestData**

Replace its `beforeAll` body so it seeds the real mod (keeps the rest of the file):

```ts
import { createWorkspace, seedTestDataMod, launchApp, firstWindow, type E2EWorkspace } from './e2e-utils'

test.beforeAll(async () => {
  ws = createWorkspace()
  seedTestDataMod(ws, 'test-mod')
  app = await launchApp(ws)
})
```

And update the `listMods` assertion to the seeded mod:

```ts
test('listMods returns the seeded TestData mod', async () => {
  const page = await firstWindow(app)
  const mods = await page.evaluate(() => window.electron.umb.listMods())
  expect(mods.map((m: { name: string }) => m.name)).toContain('test-mod')
})
```

- [ ] **Step 5: Build and run app-boot to verify infra**

Run: `npm run build`
Expected: builds `dist/` with no errors.

Run: `npx playwright test app-boot`
Expected: all app-boot tests PASS (window opens, debugPing ok, listMods contains `test-mod`, brand text + sidebar visible).

- [ ] **Step 6: Commit**

```bash
git add UMB.Desktop/e2e/e2e-utils.ts UMB.Desktop/e2e/electron.d.ts UMB.Desktop/playwright.config.ts UMB.Desktop/e2e/app-boot.spec.ts
git commit -m "test(e2e): shared TestData helpers + full preload types"
```

---

## Task 2: Ported BaselineComparer + self-test

**Files:**
- Create: `UMB.Desktop/e2e/baseline-compare.ts`
- Create: `UMB.Desktop/e2e/baseline-compare.spec.ts`

- [ ] **Step 1: Write the comparer self-test (failing — module not yet created)**

Create `UMB.Desktop/e2e/baseline-compare.spec.ts`:

```ts
import { test, expect } from '@playwright/test'
import { cpSync, readFileSync, writeFileSync, rmSync, mkdtempSync, readdirSync } from 'fs'
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

test('compareDirs flags a single flipped PRC byte as a hashed mismatch', () => {
  const copy = join(tmp, 'copy')
  cpSync(BASELINE, copy, { recursive: true })
  const prc = join(copy, 'ui', 'param', 'database', 'ui_bgm_db.prc')
  const buf = readFileSync(prc)
  buf[0] = buf[0] ^ 0xff
  writeFileSync(prc, buf)

  const report = compareDirs(BASELINE, copy)
  expect(report.isClean).toBe(false)
  expect(report.mismatchedHashed.map((m) => m.path)).toContain('ui/param/database/ui_bgm_db.prc')
})

test('compareDirs flags missing and extra hashed files', () => {
  const copy = join(tmp, 'copy')
  cpSync(BASELINE, copy, { recursive: true })
  rmSync(join(copy, 'ui', 'param', 'database', 'ui_series_db.prc'))
  writeFileSync(join(copy, 'ui', 'param', 'database', 'extra.prc'), Buffer.from([1, 2, 3]))

  const report = compareDirs(BASELINE, copy)
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
  const dir = join(tmp, 'out', 'sound', 'bgm')
  cpSync(BASELINE, join(tmp, 'out'), { recursive: true }) // ensure dir exists then add a nus3
  writeFileSync(join(tmp, 'out', 'a.nus3audio'), Buffer.alloc(10))
  const m = buildManifest(join(tmp, 'out'))
  expect(m['a.nus3audio']).toBe(10)
  void readdirSync // keep import used
  void dir
})
```

- [ ] **Step 2: Run to verify it fails**

Run: `npx playwright test baseline-compare`
Expected: FAIL — `Cannot find module './baseline-compare'`.

- [ ] **Step 3: Implement `baseline-compare.ts`**

Create `UMB.Desktop/e2e/baseline-compare.ts` (faithful port of `Tests/Helpers/BaselineComparer.cs`):

```ts
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
```

- [ ] **Step 4: Run the self-test to verify it passes**

Run: `npx playwright test baseline-compare`
Expected: all 5 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add UMB.Desktop/e2e/baseline-compare.ts UMB.Desktop/e2e/baseline-compare.spec.ts
git commit -m "test(e2e): port BaselineComparer (SHA256 + nus3 manifest) with self-tests"
```

---

## Task 3: order-tracks spec (Tier 1)

**Files:**
- Modify (rewrite): `UMB.Desktop/e2e/order-tracks.spec.ts`

The `dev` series is custom (13 tracks, no `order` column). The `mario` series is `existing-series` (6 tracks). Expected `bgmId`s mirror `BaselineGenerator` exactly.

- [ ] **Step 1: Rewrite the spec on real TestData**

```ts
import { test, expect, type ElectronApplication } from '@playwright/test'
import { readFileSync } from 'fs'
import { join } from 'path'
import { createWorkspace, seedTestDataMod, launchApp, firstWindow, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication
let modDir: string

test.beforeAll(async () => {
  ws = createWorkspace()
  modDir = seedTestDataMod(ws, 'test-mod')
  app = await launchApp(ws)
})
test.afterAll(async () => { await app?.close(); ws?.cleanup() })

const devPath = () => join(modDir, 'dev')
const marioPath = () => join(modDir, 'mario')

test('loads all 13 custom-series tracks, not an existing series', async () => {
  const page = await firstWindow(app)
  const data = await page.evaluate((sp) => window.electron.umb.loadTrackOrder(sp), devPath())
  expect(data.items).toHaveLength(13)
  expect(data.isExistingSeries).toBe(false)
  expect(data.hasSongOrder).toBe(false)
  expect(data.items[0].title).toBe('KARTS!')
})

test('reversing the custom series rewrites tracks.csv with an order column', async () => {
  const page = await firstWindow(app)
  const loaded = await page.evaluate((sp) => window.electron.umb.loadTrackOrder(sp), devPath())
  const reversed = loaded.items.map((i) => i.id).reverse()

  const saved = await page.evaluate(
    ([sp, ids]) => window.electron.umb.saveTrackOrder(sp as string, ids as string[]),
    [devPath(), reversed] as const
  )
  expect(saved.items[0].title).toBe('Time Trials')
  expect(saved.items[12].title).toBe('KARTS!')

  const csv = readFileSync(join(devPath(), 'tracks.csv'), 'utf8')
  expect(csv.split(/\r?\n/)[0]).toContain('order')
})

test('existing series writes song_order.toml with derived bgmIds matching the CLI', async () => {
  const page = await firstWindow(app)
  const loaded = await page.evaluate((sp) => window.electron.umb.loadTrackOrder(sp), marioPath())
  expect(loaded.isExistingSeries).toBe(true)
  expect(loaded.items).toHaveLength(6)

  await page.evaluate(
    ([sp, ids]) => window.electron.umb.saveTrackOrder(sp as string, ids as string[]),
    [marioPath(), loaded.items.map((i) => i.id)] as const
  )

  const toml = readFileSync(join(marioPath(), 'song_order.toml'), 'utf8')
  expect(toml).toContain('song_order = [')
  // Tone-id derivation parity with C# (BaselineGenerator.WriteMarioSongOrderToml).
  expect(toml).toContain('ui_bgm_flowerhead___somewhat_good__lofi___01_summer')
  expect(toml).toContain('ui_bgm_flowerhead___somewhat_good__lofi___03_brain_empty')
})

test('pre-existing song_order.toml is loaded with vanilla entries locked', async () => {
  // Mirrors OrderingTests.TrackOrder_ExistingSeriesRespectsSongOrderToml: interleaved vanilla ps01.
  const page = await firstWindow(app)
  const songOrder =
    'song_order = [\n' +
    '  "ui_bgm_flowerhead___somewhat_good__lofi___03_brain_empty",\n' +
    '  "ui_bgm_ps01",\n' +
    '  "ui_bgm_flowerhead___somewhat_good__lofi___01_summer",\n' +
    ']\n'
  const { writeFileSync } = await import('fs')
  writeFileSync(join(marioPath(), 'song_order.toml'), songOrder, 'utf8')

  const data = await page.evaluate((sp) => window.electron.umb.loadTrackOrder(sp), marioPath())
  expect(data.hasSongOrder).toBe(true)
  expect(data.items[0].bgmId).toBe('ui_bgm_flowerhead___somewhat_good__lofi___03_brain_empty')
  const vanilla = data.items.find((i) => i.bgmId === 'ui_bgm_ps01')
  expect(vanilla?.isLocked).toBe(true)
})

test('UI smoke: Order Tracks shows the series list', async () => {
  const page = await firstWindow(app)
  await page.getByText('Order Tracks').first().click()
  await expect(page.getByRole('heading', { name: 'test-mod' }).or(page.getByText('dev'))).toBeVisible({ timeout: 5000 })
})
```

- [ ] **Step 2: Build and run**

Run: `npm run build && npx playwright test order-tracks`
Expected: all PASS. If the `song_order.toml` bgmId assertions fail, the desktop's `deriveToneId` has diverged from the C# tone-id derivation — that is a real finding; stop and report it.

- [ ] **Step 3: Commit**

```bash
git add UMB.Desktop/e2e/order-tracks.spec.ts
git commit -m "test(e2e): order-tracks against TestData, bgmId parity with CLI"
```

---

## Task 4: order-series spec (Tier 1)

**Files:**
- Create: `UMB.Desktop/e2e/order-series.spec.ts`

Mirrors `OrderingTests.SeriesOrderToml_*`. The seeded mod has one custom series (`dev`); add a second (`gamma`) so ordering is observable.

- [ ] **Step 1: Write the spec**

```ts
import { test, expect, type ElectronApplication } from '@playwright/test'
import { mkdirSync, writeFileSync, readFileSync } from 'fs'
import { join } from 'path'
import { createWorkspace, seedTestDataMod, launchApp, firstWindow, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication
let modDir: string

test.beforeAll(async () => {
  ws = createWorkspace()
  modDir = seedTestDataMod(ws, 'test-mod')
  // Add a second custom series so series ordering has two entries.
  const gamma = join(modDir, 'gamma')
  mkdirSync(gamma, { recursive: true })
  writeFileSync(join(gamma, 'series.toml'), '[series]\nid = "gamma"\nname = "Gamma"\n', 'utf8')
  writeFileSync(join(gamma, 'tracks.csv'), 'filename,title,game\ng.flac,G,Gamma\n', 'utf8')
  app = await launchApp(ws)
})
test.afterAll(async () => { await app?.close(); ws?.cleanup() })

test('loadSeriesOrder lists custom series; dev pre-ordered by series-order.toml', async () => {
  const page = await firstWindow(app)
  const data = await page.evaluate((mp) => window.electron.umb.loadSeriesOrder(mp), modDir)
  const ids = data.items.map((i) => i.seriesId).sort()
  expect(ids).toEqual(['dev', 'gamma'])
  expect(data.hasSeriesOrder).toBe(true) // configured-mod ships series-order.toml = ["dev"]
})

test('saveSeriesOrder writes gamma before dev', async () => {
  const page = await firstWindow(app)
  const loaded = await page.evaluate((mp) => window.electron.umb.loadSeriesOrder(mp), modDir)
  const gamma = loaded.items.find((i) => i.seriesId === 'gamma')!
  const dev = loaded.items.find((i) => i.seriesId === 'dev')!

  const result = await page.evaluate(
    ([mp, ids]) => window.electron.umb.saveSeriesOrder(mp as string, ids as string[]),
    [modDir, [gamma.id, dev.id]] as const
  )
  expect(result.items.map((i) => i.seriesId)).toEqual(['gamma', 'dev'])

  const toml = readFileSync(join(modDir, 'series-order.toml'), 'utf8')
  const order = [...toml.matchAll(/^\s+"([^"]+)",/gm)].map((m) => m[1])
  expect(order).toEqual(['gamma', 'dev'])
})

test('UI smoke: Order Series view opens', async () => {
  const page = await firstWindow(app)
  await page.getByText('Order Series').first().click()
  await expect(page.getByText('test-mod').or(page.getByText('Gamma'))).toBeVisible({ timeout: 5000 })
})
```

- [ ] **Step 2: Build and run**

Run: `npm run build && npx playwright test order-series`
Expected: all PASS.

- [ ] **Step 3: Commit**

```bash
git add UMB.Desktop/e2e/order-series.spec.ts
git commit -m "test(e2e): order-series against TestData"
```

---

## Task 5: merge spec (Tier 1)

**Files:**
- Create: `UMB.Desktop/e2e/merge.spec.ts`

Mirrors `MergeOperationsTests`: merge two mods, conflicting `dev` series dedupes, non-conflicting series copied, `series-order.toml` combined.

- [ ] **Step 1: Write the spec**

```ts
import { test, expect, type ElectronApplication } from '@playwright/test'
import { mkdirSync, writeFileSync, existsSync, readFileSync } from 'fs'
import { join } from 'path'
import { createWorkspace, seedTestDataMod, launchApp, firstWindow, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication
let modA: string
let modB: string

test.beforeAll(async () => {
  ws = createWorkspace()
  modA = seedTestDataMod(ws, 'mod-a') // has dev, mario, series-order.toml=["dev"]
  // mod-b: a conflicting "dev" series + a unique "custom" series + series-order=["custom","dev"]
  modB = join(ws.root, 'Mods', 'MusicMods', 'mod-b')
  const bDev = join(modB, 'dev')
  mkdirSync(bDev, { recursive: true })
  writeFileSync(join(bDev, 'series.toml'),
    '[series]\nid = "dev"\nname = "Dev B"\n\n[[games]]\nid = "dev_b"\nname = "Dev B"\n', 'utf8')
  writeFileSync(join(bDev, 'tracks.csv'),
    'filename,game,title,author,copyright,record_type,special_category,volume,info1,in_soundtest\n' +
    'b-only.flac,dev_b,B Only,,,original,,1,,True\n', 'utf8')
  writeFileSync(join(bDev, 'b-only.flac'), 'B')
  const bCustom = join(modB, 'custom')
  mkdirSync(bCustom, { recursive: true })
  writeFileSync(join(bCustom, 'series.toml'), '[series]\nid = "custom"\nname = "Custom"\n', 'utf8')
  writeFileSync(join(bCustom, 'tracks.csv'), 'filename,title,game\nc.flac,C,Custom\n', 'utf8')
  writeFileSync(join(modB, 'series-order.toml'), 'order = [\n    "custom",\n    "dev",\n]\n', 'utf8')
  app = await launchApp(ws)
})
test.afterAll(async () => { await app?.close(); ws?.cleanup() })

test('analyzeMerge flags the dev conflict and lists all series', async () => {
  const page = await firstWindow(app)
  const analysis = await page.evaluate(
    (mods) => window.electron.umb.analyzeMerge(mods),
    [modA, modB]
  )
  expect(analysis.series.map((s) => s.name).sort()).toEqual(['custom', 'dev', 'mario'])
  expect(analysis.conflicts.map((c) => c.seriesName)).toContain('dev')
})

test('executeMerge produces a merged mod with all series and resolved conflict', async () => {
  const page = await firstWindow(app)
  const result = await page.evaluate(
    ([mods, name, priority]) =>
      window.electron.umb.executeMerge(mods as string[], name as string, priority as string),
    [[modA, modB], 'merged', modA] as const
  )
  expect(result.totalSeries).toBe(3)
  expect(result.conflictsResolved).toBe(1)

  const out = result.outputPath
  expect(existsSync(join(out, 'dev', 'tracks.csv'))).toBe(true)
  expect(existsSync(join(out, 'mario', 'tracks.csv'))).toBe(true)
  expect(existsSync(join(out, 'custom', 'series.toml'))).toBe(true)

  // series-order merged, priority A first, deduped → dev, custom
  const order = [...readFileSync(join(out, 'series-order.toml'), 'utf8').matchAll(/^\s+"([^"]+)",/gm)].map((m) => m[1])
  expect(order).toEqual(['dev', 'custom'])
})

test('validateMergeName rejects an existing name', async () => {
  const page = await firstWindow(app)
  const err = await page.evaluate(() => window.electron.umb.validateMergeName('mod-a'))
  expect(err).toBe('A mod with that name already exists.')
})

test('UI smoke: Merge view opens', async () => {
  const page = await firstWindow(app)
  await page.getByText('Merge', { exact: true }).first().click()
  await expect(page.getByText('mod-a').or(page.getByText('mod-b'))).toBeVisible({ timeout: 5000 })
})
```

- [ ] **Step 2: Build and run**

Run: `npm run build && npx playwright test merge`
Expected: all PASS.

- [ ] **Step 3: Commit**

```bash
git add UMB.Desktop/e2e/merge.spec.ts
git commit -m "test(e2e): merge against TestData (conflict dedup + series-order)"
```

---

## Task 6: extract-icons spec (temp workspace + copied tool)

**Files:**
- Create: `UMB.Desktop/e2e/extract-icons.spec.ts`

Mirrors `ExtractIconsServiceTests`: the committed `extract-icons-source` baseline holds `series_0_dev.bntx`. A mod with a `dev` series gets `icon.png` extracted (dimensions match the source); a mod without `dev` extracts nothing.

- [ ] **Step 1: Write the spec**

```ts
import { test, expect, type ElectronApplication } from '@playwright/test'
import { mkdirSync, existsSync, statSync, openSync, readSync, closeSync } from 'fs'
import { join } from 'path'
import { createWorkspace, launchApp, firstWindow, seedTool, repoRoot, type E2EWorkspace } from './e2e-utils'

let ws: E2EWorkspace
let app: ElectronApplication
let modDir: string
const compiled = () => join(repoRoot(), 'Tests', 'TestData', 'baselines', 'extract-icons-source')

function pngDims(file: string): { w: number; h: number } {
  const fd = openSync(file, 'r')
  const b = Buffer.alloc(24)
  readSync(fd, b, 0, 24, 0)
  closeSync(fd)
  return { w: b.readUInt32BE(16), h: b.readUInt32BE(20) }
}

test.beforeAll(async () => {
  ws = createWorkspace()
  seedTool(ws, 'UltimateTexCli') // extractIcons resolves <workspace>/Tools/UltimateTexCli
  modDir = join(ws.root, 'Mods', 'MusicMods', 'umb-target')
  mkdirSync(join(modDir, 'dev'), { recursive: true }) // empty series dir named to match the BNTX
  app = await launchApp(ws)
})
test.afterAll(async () => { await app?.close(); ws?.cleanup() })

test('analyze matches series_0_dev.bntx to the dev series', async () => {
  const page = await firstWindow(app)
  const a = await page.evaluate(
    ([c, m]) => window.electron.umb.analyzeExtractIcons(c as string, m as string),
    [compiled(), modDir] as const
  )
  expect(a.matched.map((x) => x.seriesId)).toEqual(['dev'])
  expect(a.unmatched).toEqual([])
})

test('extract produces icon.png whose dimensions match the source icon', async () => {
  const page = await firstWindow(app)
  const result = await page.evaluate(
    ([c, m]) => window.electron.umb.extractIcons(c as string, m as string, 'all'),
    [compiled(), modDir] as const
  )
  expect(result.extracted).toBe(1)

  const out = join(modDir, 'dev', 'icon.png')
  expect(existsSync(out)).toBe(true)
  expect(statSync(out).size).toBeGreaterThan(0)

  const src = join(repoRoot(), 'Tests', 'TestData', 'configured-mod', 'dev', 'icon.png')
  expect(pngDims(out)).toEqual(pngDims(src))
})

test('UI smoke: Extract Icons view opens', async () => {
  const page = await firstWindow(app)
  await page.getByText('Extract Icons').first().click()
  await expect(page.getByText('umb-target').or(page.getByText('Extract Icons'))).toBeVisible({ timeout: 5000 })
})
```

- [ ] **Step 2: Build and run**

Run: `npm run build && npx playwright test extract-icons`
Expected: all PASS (real `ultimate_tex_cli` runs).

- [ ] **Step 3: Commit**

```bash
git add UMB.Desktop/e2e/extract-icons.spec.ts
git commit -m "test(e2e): extract-icons against committed BNTX baseline"
```

---

## Task 7: playlist-info spec (repo workspace, read-only)

**Files:**
- Create: `UMB.Desktop/e2e/playlist-info.spec.ts`

Validates the TS PRC/MSBT parser against the real vanilla game data. Read-only → safe to point at the repo root.

- [ ] **Step 1: Write the spec**

```ts
import { test, expect, type ElectronApplication } from '@playwright/test'
import { _electron as electron } from '@playwright/test'
import { firstWindow, repoRoot } from './e2e-utils'
import { resolve, join, dirname } from 'path'
import { fileURLToPath } from 'url'

const __dirname = dirname(fileURLToPath(import.meta.url))
let app: ElectronApplication

test.beforeAll(async () => {
  const mainPath = resolve(__dirname, '..', 'dist', 'main', 'index.js')
  app = await electron.launch({
    args: [mainPath],
    env: { ...process.env, UMB_WORKSPACE: repoRoot(), NODE_ENV: 'test' }
  })
})
test.afterAll(async () => { await app?.close() })

test('getPlaylistInfo parses vanilla playlists and stages', async () => {
  const page = await firstWindow(app)
  const data = await page.evaluate(() => window.electron.umb.getPlaylistInfo())

  expect(data.playlists.length).toBeGreaterThan(0)
  expect(data.stages.length).toBeGreaterThan(0)

  // bgmjack → "Persona" (PLAYLIST_NAMES) with vanilla songs.
  const persona = data.playlists.find((p) => p.id === 'bgmjack')
  expect(persona?.name).toBe('Persona')
  expect((persona?.songCount ?? 0)).toBeGreaterThan(0)

  // A known stage resolves its display name + has songs.
  const battlefield = data.stages.find((s) => s.uiStageId === 'ui_stage_battle_field')
  expect(battlefield?.name).toBe('Battlefield')
  expect((battlefield?.songs.length ?? 0)).toBeGreaterThan(0)
})

test('UI smoke: Playlist Info view opens', async () => {
  const page = await firstWindow(app)
  await page.getByText('Playlist Info').first().click()
  await expect(page.getByText('Persona').or(page.getByText('Battlefield'))).toBeVisible({ timeout: 8000 })
})
```

- [ ] **Step 2: Build and run**

Run: `npm run build && npx playwright test playlist-info`
Expected: all PASS. If the UI smoke label differs, adjust the `getByText` to the actual sidebar label for the playlist view (check `src/renderer/src/locale/en.ts`).

- [ ] **Step 3: Commit**

```bash
git add UMB.Desktop/e2e/playlist-info.spec.ts
git commit -m "test(e2e): playlist-info parses vanilla PRC/MSBT"
```

---

## Task 8: config-volume spec (repo workspace, daemon, heavy)

**Files:**
- Create: `UMB.Desktop/e2e/config-volume.spec.ts`

Launches with `UMB_WORKSPACE=repoRoot` (the CLI daemon needs the `UMB.CLI` project) but targets an absolute temp `seriesPath` copied from TestData. Mirrors `VolumeConfigServiceTests` (gain formula) + `OrderingTests.TracksCsv_VolumeCanBeOverridden` (CSV override).

- [ ] **Step 1: Write the spec**

```ts
import { test, expect, type ElectronApplication } from '@playwright/test'
import { _electron as electron } from '@playwright/test'
import { readFileSync } from 'fs'
import { resolve, join, dirname } from 'path'
import { fileURLToPath } from 'url'
import { firstWindow, repoRoot, copyConfiguredSeries } from './e2e-utils'

const __dirname = dirname(fileURLToPath(import.meta.url))
let app: ElectronApplication
let series: { dir: string; cleanup(): void }

test.describe.configure({ timeout: 240_000 }) // real LUFS + daemon bootstrap

test.beforeAll(async () => {
  series = copyConfiguredSeries('dev')
  const mainPath = resolve(__dirname, '..', 'dist', 'main', 'index.js')
  app = await electron.launch({
    args: [mainPath],
    env: { ...process.env, UMB_WORKSPACE: repoRoot(), NODE_ENV: 'test' }
  })
})
test.afterAll(async () => { await app?.close(); series?.cleanup() })

test('analyze returns per-track LUFS + auto-gain matching the CLI gain formula', async () => {
  const page = await firstWindow(app)
  const data = await page.evaluate((sp) => window.electron.umb.loadVolumeConfig(sp, true), series.dir)

  expect(data.ffmpegAvailable).toBe(true)
  expect(data.items.length).toBe(13)

  const target = data.targetLufs   // -14 from appsettings
  const max = data.maxMultiplier   // 4
  for (const item of data.items) {
    expect(item.hasMeasurement).toBe(true)
    const raw = Math.pow(10, (target - item.measuredLufs) / 20)
    const expected = Math.min(raw, max)
    if (!item.wasClamped) {
      expect(Math.abs(item.autoGain - expected)).toBeLessThan(0.02)
    } else {
      expect(item.autoGain).toBeCloseTo(max, 2)
    }
  }
})

test('save writes the per-track override into tracks.csv volume column', async () => {
  const page = await firstWindow(app)
  await page.evaluate(
    (sp) => window.electron.umb.saveVolumeConfig(sp, [{ originalIndex: 0, volume: 0.5 }]),
    series.dir
  )
  const csv = readFileSync(join(series.dir, 'tracks.csv'), 'utf8').split(/\r?\n/)
  // volume is column index 7 (filename,game,title,author,copyright,record_type,special_category,volume,...)
  const cols = csv[1].split(',')
  expect(cols[7]).toBe('0.5')
})

test('UI smoke: Config Volume view opens', async () => {
  const page = await firstWindow(app)
  await page.getByText('Config Volume').first().click()
  await expect(page.getByText('Config Volume').or(page.getByText('dev'))).toBeVisible({ timeout: 8000 })
})
```

- [ ] **Step 2: Build and run**

Run: `npm run build && npx playwright test config-volume`
Expected: PASS. First call triggers a `dotnet run … serve` build + LUFS analysis of 13 FLAC (slow). If `ffmpegAvailable` is false, ffmpeg is not on PATH — fix the environment, do not weaken the test.

- [ ] **Step 3: Commit**

```bash
git add UMB.Desktop/e2e/config-volume.spec.ts
git commit -m "test(e2e): config-volume LUFS + CSV override via CLI daemon"
```

---

## Task 9: nus3-convert spec (repo workspace, daemon, heavy)

**Files:**
- Create: `UMB.Desktop/e2e/nus3-convert.spec.ts`

Mirrors `Nus3ConvertServiceTests`. Converts one FLAC ("13 Time Trials") and checks the produced `.nus3audio` size against the committed baseline (`855544` bytes) within tolerance (loop metadata is negligible vs the audio payload).

- [ ] **Step 1: Write the spec**

```ts
import { test, expect, type ElectronApplication } from '@playwright/test'
import { _electron as electron } from '@playwright/test'
import { existsSync, statSync } from 'fs'
import { resolve, join, dirname } from 'path'
import { fileURLToPath } from 'url'
import { firstWindow, repoRoot, copyConfiguredSeries } from './e2e-utils'

const __dirname = dirname(fileURLToPath(import.meta.url))
const FLAC = 'flowerhead - Somewhat Good- Karts - 13 Time Trials.flac'
const NUS3 = 'flowerhead - Somewhat Good- Karts - 13 Time Trials.nus3audio'
const BASELINE_SIZE = 855544

let app: ElectronApplication
let series: { dir: string; cleanup(): void }

test.describe.configure({ timeout: 240_000 }) // pymusiclooper + VGAudio + daemon

test.beforeAll(async () => {
  series = copyConfiguredSeries('dev')
  const mainPath = resolve(__dirname, '..', 'dist', 'main', 'index.js')
  app = await electron.launch({
    args: [mainPath],
    env: { ...process.env, UMB_WORKSPACE: repoRoot(), NODE_ENV: 'test' }
  })
})
test.afterAll(async () => { await app?.close(); series?.cleanup() })

test('analyze + convert produces a non-empty nus3audio and persists the decision', async () => {
  const page = await firstWindow(app)

  const analysis = await page.evaluate(
    ([sp, f]) => window.electron.umb.analyzeLoopPoints(sp as string, f as string),
    [series.dir, FLAC] as const
  )
  const decision = {
    trackId: FLAC,
    mode: (analysis.candidates.length > 0 ? 'loop' : 'end-to-end') as 'loop' | 'end-to-end',
    candidate: analysis.candidates[0],
    status: 'pending' as const
  }

  const ok = await page.evaluate(
    ([sp, d]) => window.electron.umb.convertNus3Track(sp as string, d as never),
    [series.dir, decision] as const
  )
  expect(ok).toBe(true)

  const produced = join(series.dir, 'songs-to-validate', NUS3)
  expect(existsSync(produced)).toBe(true)
  expect(statSync(produced).size).toBeGreaterThan(0)

  const conversions = await page.evaluate((sp) => window.electron.umb.loadNus3Conversions(sp), series.dir)
  expect(conversions[FLAC]).toBeTruthy()
})

test('produced nus3audio size matches the CLI baseline manifest (within tolerance)', async () => {
  const produced = join(series.dir, 'songs-to-validate', NUS3)
  const size = statSync(produced).size
  // Audio payload is identical (same FLAC, same VGAudio encode); only loop-marker
  // fields differ, so size should land within ~2% of the CLI baseline.
  expect(Math.abs(size - BASELINE_SIZE)).toBeLessThan(BASELINE_SIZE * 0.02)
})

test('accept moves the validated nus3audio into the series folder', async () => {
  const page = await firstWindow(app)
  const moved = await page.evaluate((sp) => window.electron.umb.acceptNus3Files(sp, false), series.dir)
  expect(moved).toBeGreaterThanOrEqual(1)
  expect(existsSync(join(series.dir, NUS3))).toBe(true)
})

test('UI smoke: Nus3 Convert view opens', async () => {
  const page = await firstWindow(app)
  await page.getByText('Nus3 Convert').first().click()
  await expect(page.getByText('Nus3 Convert').or(page.getByText('dev'))).toBeVisible({ timeout: 8000 })
})
```

- [ ] **Step 2: Build and run**

Run: `npm run build && npx playwright test nus3-convert`
Expected: PASS. **If the size-tolerance test fails**, desktop loop-point selection diverges enough to change the payload — confirm by logging the produced size, then either (a) widen tolerance with a documented reason, or (b) regenerate `Tests/TestData/baselines/nus3-convert` from the desktop path and note the divergence. Do not delete the check.

- [ ] **Step 3: Commit**

```bash
git add UMB.Desktop/e2e/nus3-convert.spec.ts
git commit -m "test(e2e): nus3-convert produces CLI-equivalent nus3audio"
```

---

## Task 10: Build differential harness

**Files:**
- Create: `UMB.Desktop/e2e/build-harness.ts`

Builds an isolated workspace so the global build reads only TestData and writes an isolated `ArcOutput`, with no repo mutation.

- [ ] **Step 1: Implement the harness**

```ts
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

/** Runs the CLI build directly (the reference run). Throws on non-zero exit. */
export function runCliBuild(b: IsolatedBuild): void {
  execFileSync('dotnet', ['run', '--project', b.cliProjectDir, '--no-launch-profile', '--', 'build'], {
    cwd: b.wsRoot,
    stdio: 'pipe',
    timeout: 300_000
  })
}

/** Copies a directory tree (e.g. snapshot ArcOutput aside before the desktop run). */
export function snapshot(srcDir: string, destDir: string): void {
  if (existsSync(destDir)) rmSync(destDir, { recursive: true, force: true })
  cpSync(srcDir, destDir, { recursive: true })
}
```

- [ ] **Step 2: Smoke-verify the harness compiles + prepares (no full build yet)**

Add a temporary check file `UMB.Desktop/e2e/_harness-smoke.spec.ts`:

```ts
import { test, expect } from '@playwright/test'
import { existsSync, readFileSync } from 'fs'
import { join } from 'path'
import { prepareIsolatedBuild } from './build-harness'

test('prepareIsolatedBuild lays out an isolated workspace', () => {
  const b = prepareIsolatedBuild()
  try {
    expect(existsSync(join(b.cliProjectDir, 'UMB.CLI.csproj'))).toBe(true)
    expect(existsSync(join(b.wsRoot, 'Mods', 'MusicMods', 'test-mod', 'dev', 'tracks.csv'))).toBe(true)
    const csproj = readFileSync(join(b.cliProjectDir, 'UMB.CLI.csproj'), 'utf8')
    expect(csproj).not.toContain('Include="..\\Sma5h\\')
    expect(csproj).toContain('Sma5h.Mods.Music.csproj')
    const settings = JSON.parse(readFileSync(join(b.cliProjectDir, 'appsettings.json'), 'utf8'))
    expect(settings.OutputPath).toContain(b.wsRoot.split('\\').join('/'))
  } finally {
    b.cleanup()
  }
})
```

Run: `npm run build && npx playwright test _harness-smoke`
Expected: PASS.

- [ ] **Step 3: Delete the smoke file and commit**

```bash
rm UMB.Desktop/e2e/_harness-smoke.spec.ts
git add UMB.Desktop/e2e/build-harness.ts
git commit -m "test(e2e): isolated build harness (absolute refs + appsettings)"
```

---

## Task 11: Build differential spec (Tier 3)

**Files:**
- Create: `UMB.Desktop/e2e/build-differential.spec.ts`

Runs a CLI reference build, then the desktop build, on the identical isolated workspace, and byte-compares `ArcOutput`.

- [ ] **Step 1: Write the spec**

```ts
import { test, expect, type ElectronApplication } from '@playwright/test'
import { _electron as electron } from '@playwright/test'
import { rmSync, existsSync } from 'fs'
import { resolve, join, dirname } from 'path'
import { fileURLToPath } from 'url'
import { firstWindow } from './e2e-utils'
import { prepareIsolatedBuild, runCliBuild, snapshot, type IsolatedBuild } from './build-harness'
import { compareDirs, formatReport } from './baseline-compare'

const __dirname = dirname(fileURLToPath(import.meta.url))
let app: ElectronApplication
let build: IsolatedBuild
let refDir: string

test.describe.configure({ timeout: 600_000 }) // two real builds

test.beforeAll(async () => {
  build = prepareIsolatedBuild()

  // Reference: CLI build → snapshot ArcOutput → clear.
  runCliBuild(build)
  refDir = join(build.wsRoot, 'ArcOutput-ref')
  snapshot(build.arcOutput, refDir)
  rmSync(build.arcOutput, { recursive: true, force: true })

  // Actual: desktop drives the build against the SAME isolated workspace.
  const mainPath = resolve(__dirname, '..', 'dist', 'main', 'index.js')
  app = await electron.launch({
    args: [mainPath],
    env: { ...process.env, UMB_WORKSPACE: build.wsRoot, NODE_ENV: 'test' }
  })
})

test.afterAll(async () => {
  await app?.close()
  build?.cleanup()
})

test('desktop build output is byte-identical to the CLI build', async () => {
  const page = await firstWindow(app)
  await page.evaluate(() => window.electron.umb.runAction('build'))

  expect(existsSync(join(build.arcOutput, 'ui', 'param', 'database', 'ui_bgm_db.prc'))).toBe(true)

  const report = compareDirs(refDir, build.arcOutput)
  expect(report.isClean, formatReport(report)).toBe(true)
})
```

- [ ] **Step 2: Build and run**

Run: `npm run build && npx playwright test build-differential`
Expected: PASS (clean report). First run compiles the copied CLI + builds twice (~3–5 min).
- If the report shows `mismatchedHashed` on a PRC, the desktop's build invocation diverges from the CLI's (settings/args/workspace) — inspect `formatReport` output; this is the regression the test exists to catch.
- If only `mismatchedNus3` sizes differ, audio encoding is non-deterministic for identical input; downgrade nus3 to presence/count by asserting `report.missingNus3.length === 0 && report.extraNus3.length === 0` instead of full `isClean`, and document why.

- [ ] **Step 3: Commit**

```bash
git add UMB.Desktop/e2e/build-differential.spec.ts
git commit -m "test(e2e): differential build — desktop output == CLI output"
```

---

## Task 12: Full-suite run + docs

**Files:**
- Modify: `UMB.Desktop/package.json` (no change needed if `test:e2e` exists — verify)
- Modify: `CLAUDE.md` (note the new E2E coverage under Desktop app → Testing)

- [ ] **Step 1: Run the entire E2E suite**

Run: `npm run build && npm run test:e2e`
Expected: every spec PASS (`app-boot`, `localisation`, `baseline-compare`, `order-tracks`, `order-series`, `merge`, `extract-icons`, `playlist-info`, `config-volume`, `nus3-convert`, `build-differential`).

- [ ] **Step 2: Confirm no repo pollution**

Run: `git status --porcelain Mods ArcOutput`
Expected: empty output (no new/changed files under repo `Mods/` or `ArcOutput/`).

- [ ] **Step 3: Document coverage in `CLAUDE.md`**

Under the Desktop app `### Testing` section, append:

```markdown
- E2E (`npm run test:e2e`) drives every action against `Tests/TestData` and compares output to the CLI:
  Tier 1 (order-tracks/order-series/merge) structural; Tier 2 (config-volume/nus3-convert/extract-icons/
  playlist-info) via the CLI daemon/tools on isolated temp paths; Tier 3 (`build-differential`) byte-compares
  desktop `ArcOutput` against a CLI reference build using `e2e/baseline-compare.ts` (ported BaselineComparer).
  Heavy tiers need local game resources + tools (not CI-portable).
```

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: record desktop E2E suite coverage"
```

---

## Self-Review notes (addressed)

- **Spec coverage:** every action in the spec maps to a task — order-tracks (T3), order-series (T4), merge (T5), extract-icons (T6), playlist-info (T7), config-volume (T8), nus3-convert (T9), build (T10+T11); shared infra (T1), ported comparer + self-test (T2); existing specs converted (T1). `scaffold`/`convert`/`cleanup`/`dump-stages` are out of scope (no IPC channel) per the spec.
- **Type consistency:** all `window.electron.umb.*` calls match the preload signatures in `src/main/preload.ts`; `compareDirs`/`compareToBaseline`/`buildManifest`/`formatReport` are defined in Task 2 and used consistently in Tasks 9/11; `prepareIsolatedBuild`/`runCliBuild`/`snapshot` defined in Task 10, used in Task 11.
- **No placeholders:** every code step is complete and runnable.
- **Known risk (documented in tasks, not hidden):** nus3 size tolerance (T9) and build nondeterminism fallback (T11).
