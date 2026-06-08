# UMB.Desktop Playwright E2E Suite — Design

**Date:** 2026-06-06
**Status:** Approved (brainstorming) — pending spec review → implementation plan

## Goal

A full Playwright E2E suite for the UMB.Desktop Electron app that drives **every action** against the
real `Tests/TestData/configured-mod` fixtures and verifies the desktop app produces the **same data as
the CLI**, applying the **same comparison logic the .NET test suite uses** (SHA256 on PRC/MSBT/BIN,
size-manifest on nus3audio/nus3bank).

## Key architectural findings (drive the whole design)

1. **Half the actions just spawn the CLI.** `build`, `config-volume-*`, `nus3-convert-batch`,
   `accept-nus3-batch` all route through `spawnCliAction` → `dotnet run --project <ws>/UMB.CLI -- <action>`
   (or the persistent `serve` daemon). These are **identical to the CLI by construction**; the test value
   is "drives correctly + produces correct/baseline-matching output".
2. **Half the actions are reimplemented in TypeScript with no headless CLI equivalent.** `order-tracks`,
   `order-series`, `merge`, `extract-icons`, `playlist-info`. The CLI versions are Avalonia/Spectre-bound;
   the .NET integration tests for these **don't byte-compare** — they assert file/structure content. The
   desktop TS is the only headless implementation, so "same as CLI" means "produces the data the .NET
   suite considers correct".
3. **The committed .NET build baselines used MOCKED nus3 (64-byte stubs) + mocked cue points.** A *real*
   desktop build won't match them byte-for-byte (real nus3 sizes; real cue points embedded in
   `ui_bgm_db.prc`). So a desktop build must be compared against a **freshly generated real-CLI
   reference**, not the committed baseline.
4. **The build is globally repo-bound in dev mode.** `UMB.CLI/Program.cs` forces the working directory to
   the repo root (walks up to the dir containing `Resources/`) and reads `Mods/MusicMods` → writes
   `ArcOutput` via **relative** appsettings paths. The desktop's dev build also requires
   `UMB_WORKSPACE` to *be* a dir containing `UMB.CLI` (it runs `dotnet run --project <ws>/UMB.CLI`).
   → A clean isolated build needs a dedicated harness (below).
5. **Per-series actions take absolute paths** (`seriesPath`, `modPath`), so they isolate cleanly even when
   `UMB_WORKSPACE` must be the repo root (for the daemon/tools).

## Decisions (from brainstorming)

| Decision | Choice |
|---|---|
| Compare strategy | **Hybrid** — structural assertions for TS-only actions; one differential byte-compare for the build |
| Heavy tests (build/nus3/LUFS) | **Always run** (no env gate) |
| Drive method | **IPC primary** (`window.electron.umb.*`) + **light UI smoke** per action |
| Existing specs | **Convert to a shared TestData helper, keep both layers** (keep boot/localisation smokes) |
| Build depth | **Full differential** (CLI ref build + desktop build, byte-compare) |
| Isolation | **OS temp dir, always cleanup** (afterAll, even on failure). Never touches repo `Mods/MusicMods` or repo `ArcOutput` |

## Test tiers

### Tier 1 — Pure-TS actions, isolated temp workspace
`order-tracks`, `order-series`, `merge`. No tools/resources/CLI. Seed `Tests/TestData/configured-mod`
into a temp `UMB_WORKSPACE`, drive via IPC, assert produced files match the **exact expectations the .NET
integration tests encode**.

- **order-tracks** (mirrors `OrderingTests`):
  - Custom series `dev` (13 tracks): reverse order via `saveTrackOrder` → `tracks.csv` gains an `order`
    column with reversed sequential values; reload via `loadTrackOrder` confirms new order; the
    last-CSV-row track ("Time Trials") is now first.
  - Existing series `mario` (6 tracks, `existing-series = true`): `saveTrackOrder` with an interleaved
    vanilla ref writes `song_order.toml`; assert it contains the modded `ui_bgm_*` ids in order. (Mirrors
    `SongOrderToml_*`; the 7-entry interleaved `ps01` scenario from `BaselineGenerator.SetupTrackOrdered`.)
- **order-series** (mirrors `OrderingTests.SeriesOrderToml_*`): add a second custom series (`gamma`) and
  `saveSeriesOrder(['gamma','dev'])` → `series-order.toml` lists gamma then dev with the standard header
  comments; reload confirms order + `hasSeriesOrder`.
- **merge** (mirrors `MergeOperationsTests`): build two mods from TestData series, `executeMerge` →
  merged folder has both series; conflicting series dedupes tracks by filename (priority kept), unions
  games/playlists, merges `series-order.toml` (dedup, priority order). Assert via `analyzeMerge` +
  reading the merged `tracks.csv`/`series.toml`/`series-order.toml`.

### Tier 2 — Tool/resource-dependent, repo-root workspace, isolated targets
Launch with `UMB_WORKSPACE = repoRoot` (daemon/tools/resources need it) but point each action at an
**absolute temp `seriesPath`/`modPath`** copied from TestData → outputs isolated.

- **config-volume** (mirrors `VolumeConfigServiceTests` math + `OrderingTests.TracksCsv_VolumeCanBeOverridden`):
  `loadVolumeConfig(seriesPath, analyze=true)` runs real LUFS (ffmpeg via daemon) → assert
  `ffmpegAvailable`, `items.length === trackCount`, each item has a measurement, and `autoGain` equals the
  documented gain formula for its `measuredLufs` (cross-check against the .NET `CalculateGain` formula).
  `saveVolumeConfig` with an override → reload/read `tracks.csv` confirms the `volume` column changed.
  (Delegates to CLI daemon → "== CLI" by construction.)
- **nus3-convert** (mirrors `Nus3ConvertServiceTests`): on a one-FLAC temp series ("13 Time Trials.flac"),
  `analyzeLoopPoints` (real pymusiclooper) → `convertNus3Track` (daemon) → assert a non-empty
  `.nus3audio` in `songs-to-validate/` + `.conversions.json` persisted. Then `acceptNus3Files` moves it
  into the series folder. **Same-as-CLI check:** compare the produced nus3 against the committed
  `Tests/TestData/baselines/nus3-convert/nus3-manifest.json` (size) via the ported comparer.
  *Risk to validate in implementation:* desktop loop-point selection (pymusiclooper candidate) may differ
  from the .NET service's auto-mode selection, changing nus3 size. If sizes diverge, fall back to a
  non-empty + structural assertion and document the divergence (the .NET test itself skips when the
  baseline is absent).
- **extract-icons** (mirrors `ExtractIconsServiceTests`): `compiledModPath` = committed
  `Tests/TestData/baselines/extract-icons-source`; `modPath` = temp mod with an empty `dev/` series.
  `analyzeExtractIcons` reports `dev` matched; `extractIcons('all')` (real ultimate_tex_cli, resolved from
  repo `Tools/`) → `dev/icon.png` produced, non-empty, **PNG dimensions match** the source
  `configured-mod/dev/icon.png`. A `gamma`-only mod extracts nothing.
- **playlist-info** (read-only): `getPlaylistInfo()` reads vanilla PRC/MSBT from repo `Resources/Game`.
  Assert known vanilla facts (e.g. a Persona/`bgmjack` playlist exists with its expected series + song
  count; total playlists/stages > 0). Read-only → safe against repo root.

### Tier 3 — Differential full build (the one true desktop-vs-CLI byte compare)

**Harness** (`e2e/build-harness.ts`), all under `os.tmpdir()`:
1. Copy `<repo>/UMB.CLI/*.cs` + `UMB.CLI.csproj` into `<ws>/UMB.CLI/`.
2. Rewrite the csproj's relative refs (`..\Sma5h\…`, `..\Tools\VGAudioCli.exe`, `..\Resources\…`) to
   **absolute** `<repoRoot>\…` (single `..\` → `<repoRoot>\`). Project builds against the real Sma5h
   projects + tools; nothing in the repo is modified.
3. Write `<ws>/UMB.CLI/appsettings.json` with **absolute** paths: `GameResourcesPath`/`ResourcesPath`/
   `ToolsPath` → repo; `OutputPath`/`ModPath`/`TempPath`/`LogPath`/`CachePath` → `<ws>`. Set
   `LufsNormalization.Enabled=false` + a fixed `GlobalVolumeMultiplier` for determinism.
   (Absolute paths make `Program.cs`'s cwd-forcing irrelevant.)
4. Copy `Tests/TestData/configured-mod` → `<ws>/Mods/MusicMods/test-mod` (the only mod).

**Flow:**
1. **Reference:** `dotnet run --project <ws>/UMB.CLI -- build` (direct child_process) → snapshot
   `<ws>/ArcOutput` aside as `REF`; clear `ArcOutput`.
2. **Actual:** launch the desktop with `UMB_WORKSPACE=<ws>`, call `runAction('build')` (desktop runs the
   identical command) → `<ws>/ArcOutput` = `ACTUAL`. The already-built `<ws>/UMB.CLI` is reused (no
   source change → no rebuild).
3. **Compare** `REF` vs `ACTUAL` with the ported `BaselineComparer` → expect `isClean`.

This validates the desktop's workspace/settings/invocation wiring end-to-end with a byte-exact PRC/MSBT
compare + nus3 size compare. Cost: ~2 real builds (~2–4 min). Windows + dotnet + tools + game resources
required (all present locally; **not** CI-portable — documented).

## Shared infrastructure (`UMB.Desktop/e2e/`)

- **`e2e-utils.ts`** (extend): `seedTestDataMod(ws, name)` (recursive copy of `configured-mod`),
  `repoRoot()` (walk up to `Sma5h.sln`), `copySeriesToTemp(seriesId)`, keep existing `seedMod`/`launchApp`.
- **`baseline-compare.ts`** (new): faithful TS port of `Tests/Helpers/BaselineComparer.cs` —
  `compare(actualDir, baselineDir) → { missingHashed, mismatchedHashed, extraHashed, missingNus3,
  mismatchedNus3, extraNus3, setupError, isClean }`. SHA256 on `.prc/.msbt/.bin`; `{relPath: size}`
  manifest on `.nus3audio/.nus3bank`; rel paths normalized to `/`. Plus a `buildManifest(dir)` helper.
- **`build-harness.ts`** (new): the Tier 3 isolated-workspace builder + `runCliBuild()` +
  `snapshotArcOutput()`.
- **`electron.d.ts`** (extend): the e2e type decl currently covers only a subset of the preload API. Add
  the full surface (`loadSeriesOrder`/`saveSeriesOrder`, `analyzeMerge`/`validateMergeName`/`executeMerge`,
  `loadVolumeConfig`/`saveVolumeConfig`, `listNus3Sources`/`analyzeLoopPoints`/`convertNus3Track`/
  `rejectNus3Track`/`acceptNus3Files`/`loadNus3Conversions`, `analyzeExtractIcons`/`extractIcons`,
  `getPlaylistInfo`, `getModStats`, `getAppSettings`/`saveAppSettings`/`checkArcOutput`) — mirror
  `src/renderer/src/lib/types/electron.d.ts`.

## Spec files (`UMB.Desktop/e2e/`)

| File | Tier | Notes |
|---|---|---|
| `app-boot.spec.ts` | — | keep; convert fixture to `seedTestDataMod` |
| `localisation.spec.ts` | — | keep |
| `order-tracks.spec.ts` | 1 | rewrite on TestData (`dev` + `mario`) |
| `order-series.spec.ts` | 1 | new |
| `merge.spec.ts` | 1 | new |
| `config-volume.spec.ts` | 2 | new |
| `nus3-convert.spec.ts` | 2 | new |
| `extract-icons.spec.ts` | 2 | new |
| `playlist-info.spec.ts` | 2 | new |
| `build-differential.spec.ts` | 3 | new |
| `baseline-compare.spec.ts` | — | self-test: mutate a byte → comparer reports mismatch (proves teeth) |

Each action spec: IPC-driven data assertions + one command-palette/sidebar UI-click smoke proving the
view wires up.

## Drive pattern

```ts
const data = await page.evaluate((sp) => window.electron.umb.saveTrackOrder(sp, [...]), seriesPath)
```
UI smokes use `getByText`/`getByRole` clicks (matching the existing order-tracks smoke). `runAction` logs
are observed via `subscribeLogs`; completion is awaited on the `runAction` promise (daemon prints the
`__DONE__` sentinel; one-shot resolves on process close).

## Self-tests (the .NET "break it" guarantee)

`baseline-compare.spec.ts`: copy a baseline, flip one PRC byte → `compare` reports exactly one
`mismatchedHashed`; remove one nus3 → one `missingNus3`; add a stray → one `extraNus3`. Confirms the
comparison has teeth (mirrors the .NET regen-then-mutate confidence check).

## Out of scope / non-goals

- CI portability of Tier 2/3 (needs copyrighted `Resources/Game` + tools) — local-only; documented.
- Drag-and-drop reorder via real mouse (flaky) — reordering driven through IPC; UI verified by click smokes.
- Rewriting the .NET suite or adding CLI headless commands for order/merge.
- `scaffold`, `convert` (import), `cleanup`, `dump-stages` actions — not surfaced as desktop actions
  (no IPC channel); excluded.

## Risks

- **nus3 loop-point parity** (Tier 2 nus3-convert): desktop vs .NET selection may differ → size mismatch.
  Mitigation: non-empty/structural fallback + documented; validate during implementation.
- **Build determinism** (Tier 3): real VGAudio/nus3audio encoding assumed deterministic for identical
  input+settings. If flaky, pin to PRC/MSBT hash compare + nus3 *presence/count* rather than exact size.
- **Build time** (~2–4 min): acceptable per "always run"; keep Tier 3 in its own spec so the fast tiers
  can be run alone during development (`playwright test order-tracks merge …`).

## Verification

1. `cd UMB.Desktop && npm run build && npm run test:e2e` → all specs green.
2. Tier 1/2 assertions match the corresponding `Tests/Integration/*Tests.cs` expectations.
3. `baseline-compare.spec.ts` negative cases fail the comparer as designed.
4. Tier 3: inspect `REF` vs `ACTUAL` → `isClean`; temporarily corrupt one ACTUAL PRC byte → spec fails.
5. After any run, no new files under repo `Mods/MusicMods` or repo `ArcOutput`; temp workspaces removed.
