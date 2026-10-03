# UltimateMusicBuilder

## Philosophy
Ideally we should need to make only minimal changes to the original code of this project, since this project did work and we're mainly just changing the input format.

## Build & Run
```bash
cd UMB.CLI
dotnet build
dotnet run
```
Configuration is in `UMB.CLI/bin/Debug/net8.0/appsettings.json`.

### Release build (local)
`scripts/publish-local.ps1` produces the exact GitHub-release artifact (self-contained,
single-file, win-x64) into `publish/` so you can test it without waiting on CI:
```bash
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-local.ps1
# -Desktop  also builds + bundles the Electron app into publish/desktop/
```
Mirrors the `Publish` step in `.github/workflows/release.yml`.

### Workspace / path resolution
Relative paths (`Mods/`, `Resources/`, `Tools/`, `ArcOutput/`, `Cache/`, `Temp/`, `Log/`)
resolve against a **single workspace root** shared by the CLI and the desktop GUI — the
release archive root that holds both `UMB.CLI.exe` and the `desktop/` subfolder.
`Program.cs` picks the working dir in this order:
1. `UMB_WORKSPACE` env var (the desktop app sets this to the archive root when spawning the CLI).
2. The launch CWD, if it already contains `Resources/` (standalone CLI run from a populated folder).
3. Walk up from the executable to find `Resources/` (dev: repo root).
Earlier the CLI unconditionally walked up to wherever `Resources/` sat next to the binary,
which snapped the desktop app's working dir back into its read-only bundle and made it ignore
the user's mods. `appsettings.json` is always loaded from the exe's own directory
(`AppContext.BaseDirectory`); only the relative paths inside it follow the workspace root.

## Project Structure
- `Sma5h/Core/Sma5h.Core/` — Core framework (StateManager, interfaces, resource providers)
- `Sma5h/Mods/Sma5h.Mods.Music/` — Music mod logic (audio state, services, mod formats)
  - `MusicMods/FolderMusicMod/` — Folder-based mod format (series.toml + tracks.csv)
  - `MusicMods/MusicModConfig/` — Original JSON-based mod format
  - `Services/` — AudioStateService, Nus3AudioService, metadata services
  - `Helpers/MusicConstants.cs` — All ID prefixes, file constants, valid extensions
- `UMB.CLI/` — Console entry point
- `UMB.Desktop/` — Electron + Svelte 5 desktop app (see below)
- `Mods/MusicMods/` — Actual mod data (test mods live here)
- `Resources/` — ParamLabels.csv and other reference data

## Key Concepts
- Mod output goes to `UMB.CLI/bin/Debug/net8.0/ArcOutput/`
- `EnableBgmSelectorOnAllStages()` in Sma5hMusic.cs sets `bgm_selector=true` on all stages during build, enabling My Music/album selection on every stage.

## Existing Series Fix (2026-03-25)
Adding songs to existing series (Final Fantasy, Persona, etc.) required two fixes:
1. **GameTitleEntry creation** (`FolderMusicMod.cs`): Previously skipped creating `GameTitleEntry` objects when `existing-series = true`. But custom sub-games (e.g. `final_fantasy_xiii` under the FF series) still need entries so the game title → series lookup works. Now always creates them; `AudioStateService.AddGameTitleEntry()` already handles duplicates.
2. **Stage playlist assignment** (`Sma5hMusic.cs`): `AddModSongsToAllPlaylists()` only added mod songs to `bgmsmashbtl` (Battlefield). Now maps each song's game title → series → stage `BgmSetId` to add songs to the correct series playlists (e.g. `bgmff` for Final Fantasy, `bgmjack` for Persona).
3. **Playlist merging** (`AudioStateService.cs`): `AddPlaylistEntry()` silently dropped tracks when a playlist ID already existed. Now merges new tracks into the existing playlist.

## Song Ordering (TestDispOrder)
- Core (vanilla) songs load their `TestDispOrder` from the game's PRC files.
- Modded songs get `TestDispOrder = short.MaxValue` (32767) because `MappingMusicModConfig.cs` ignores that field and the `BgmDbRootEntry` constructor defaults to `short.MaxValue`.
- During `SaveBgmEntriesToStateManager()`, all songs are sorted by `TestDispOrder` and reassigned sequential values 0, 1, 2...
- Result: **modded songs always appear after all vanilla songs** in the Sound Test and My Music views. Among modded songs, order follows the JSON array order (series → games → bgms).
- Vanilla Persona has 11 songs (ps01–ps11): Mass Destruction, Battle Hymn of the Soul, Reach Out to the Truth, I'll Face Myself, Time to Make History, Wake Up Get Up Get Out There, Last Surprise, Rivers in the Desert, Our Beginning, Aria of the Soul, Beneath the Mask.

## Nus3 Conversion Fixes (2026-07-01)
1. **Sample rate** (`Nus3ConvertService.cs`): the Namco Opus encoder only accepts
   8/12/16/24/48 kHz. Source `.wav` files were passed straight through (ffmpeg skipped),
   so a 44.1 kHz `.wav` made VGAudio print "Sample rate is invalid" and write nothing →
   "VGAudioCli produced no output". Both `Run()` and `ConvertBatch()` now also run ffmpeg when a
   `.wav` isn't already 48 kHz. VGAudio's swallowed stdout is now surfaced in the error, and a
   `Console.Out` restore leak on exception was fixed.
2. **VGAudioCli in the release build** (`Program.cs` + `UMB.CLI.csproj`): `VGAudioCli.exe` is a
   loose `<Reference>` assembly in `Tools/`, not a NuGet package. A single-file publish neither
   bundled it nor listed it in `.deps.json`, so *all* audio ops (build + nus3) threw
   `FileNotFoundException` in the release. Fixed with an `AssemblyLoadContext.Resolving` handler
   that loads it from `Tools/VGAudioCli.exe`, plus a post-publish MSBuild target that copies the
   exe into `publish/Tools/`. Dev is unaffected (it's copied next to the binary there).

## MSBT Locale Output (2026-10-03)
`StateManager.WriteChanges()` writes `msg_bgm` / `msg_title` MSBTs locale-less (`msg_bgm.msbt`)
when only one locale is in `Resources/Game` (the normal setup). When several locales are present
(e.g. `+eu_fr` and `+us_en`), each keeps its `+locale` suffix — previously they all collapsed onto
one path, so only one locale survived.

## CLI ↔ Desktop Bridge (2026-10-03)
The desktop spawns `UMB.CLI` one-shot or talks to `UMB.CLI serve` (daemon: one JSON request per
stdin line, `__DONE__\t<id>\t<code>` reply). Rules the CLI side now guarantees:
- **Exit / `__DONE__` codes**: 0 ok, 1 failed, 2 unknown action or malformed request. An action
  "failed" if it threw or logged any error (`ErrorCountingLoggerProvider`), so services keep
  reporting failure by `LogError` + return.
- **UTF-8 stdio** when redirected (`CliOutput.Init`); Windows otherwise uses the OEM code page.
- **Ordered stdout**: when redirected, logs are written synchronously through `CliOutput`, as are
  `__DONE__`/`__LUFS_PROGRESS__`, so a request's logs always precede its `__DONE__`.
- **External tools** go through `ProcessRunner` (drains both pipes, 15 min timeout, kills the tree).
- **VGAudio** goes through `VGAudioRunner` (it writes to the global `Console.Out`, including from a
  timer after returning — never dispose or race the capture writer).

**All mod data logic lives in C#.** The desktop's data actions (mods, series/track order, merge,
extract icons, nus3 workflow, playlist info, playlist assignment) are JSON actions in
`UMB.CLI/Desktop/DesktopApi.cs`: `<action> <input.json> <output.json>`, writing `{"result": …}` or
`{"error": "…"}` (a `DesktopApiException` message is shown to the user). `Desktop/Models.cs`
mirrors `UMB.Desktop/src/shared/types.ts` — change both together. The services behind them are
shared with the CLI's interactive commands/Avalonia windows, so don't re-implement mod logic in
TypeScript. The desktop runs these through two daemons (`cli.ts`): long-running actions
(`BACKGROUND_ACTIONS`) get their own so quick UI calls aren't queued behind them.

## Testing
Test on Nintendo Switch by copying ArcOutput to the SD card mod folder.

## Desktop app (UMB.Desktop)
Electron app wrapping the same mod-build logic. Stack: Electron + Svelte 5 (runes) + Tailwind, built with electron-vite, packaged with electron-builder.

### Build & Run
```bash
cd UMB.Desktop
npm install
npm run dev      # builds the CLI (build:cli), then electron-vite dev (hot reload)
npm run build    # compile to dist/
npm run package  # electron-builder installer
```

### Structure
- `src/main/` — Electron main process (Node). Action handlers invoked over IPC:
  - `index.ts` — app entry, window + IPC wiring
  - `preload.ts` — contextBridge exposing the IPC API to the renderer
  - `cli.ts` — runs the C# CLI: one-shot actions, the daemons and `callCli` (JSON actions)
  - `config-volume.ts`, `app-settings.ts` — the remaining main-process modules; everything else
    forwards to the CLI. In dev the CLI is `<workspace>/UMB.CLI` if present, else the repo's,
    run from its `bin/Debug/net8.0` build.
- `src/renderer/src/` — Svelte 5 UI
  - `lib/components/actions/` — one view per action (build, config-volume, nus3-convert, order-series, order-tracks)
  - `lib/components/` — shared UI (app-bar, sidebar, command-palette, log-drawer, bottom-panel)
  - `lib/stores/*.svelte.ts` — rune-based stores (logs, mods, sidebar, theme)
  - `lib/types/electron.d.ts` — typings for the preload API
  - `locale/en.ts`, `locale/index.ts` — svelte-i18n strings. **All static text must be localised here.**
- `src/shared/ipc-channels.ts` — IPC channel name constants shared by main + renderer

### Testing
- `npm test` — Vitest unit tests (colocated `*.test.ts` in `src/main/`); the mod logic's own
  tests are xUnit (`Tests/Unit/Desktop/`)
- `npm run test:e2e` — Playwright E2E against the built Electron app (`e2e/`)
- E2E (`npm run test:e2e`) drives every action against `Tests/TestData` and compares output to the CLI:
  Tier 1 (order-tracks/order-series/merge) structural; Tier 2 (config-volume/nus3-convert/extract-icons/
  playlist-info) via the CLI daemon/tools on isolated temp paths; Tier 3 (`build-differential`) byte-compares
  desktop `ArcOutput` against a CLI reference build using `e2e/baseline-compare.ts` (ported BaselineComparer).
  Heavy tiers need local game resources + tools. They auto-skip (Playwright `test.skip`)
  when those are absent — see `hasGameResources`/`hasTool` in `e2e/e2e-utils.ts` — so CI
  (`.github/workflows/test.yml`) runs the portable subset green and skips the rest.