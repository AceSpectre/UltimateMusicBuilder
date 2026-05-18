# Ultimate Music Builder (UMB)

UMB is a fork of [Sma5hMusic](https://github.com/Deinonychus71/Sma5hMusic) for adding music to Super Smash Bros. Ultimate. It keeps Sma5h's core mod-build pipeline, but replaces the JSON metadata format with a folder-based layout (`series.toml` + `tracks.csv`) that's easier to manipulate for batch song additions to the game.

> **Heads up.** This tool is highly experimental and may not always work as expected.
> * Always keep backups of your files before using them with this tool.
> * **Mods are not safe online.**

---

## Dependencies

1. **FFmpeg.** - UMB uses it for LUFS-based loudness normalization at build time and audio playback in the Config Volume preview window. Install ffmpeg system-wide and update `appsettings.json → Sma5hMusic.LufsNormalization.FfmpegPath` to point at the local install OR drop a build into `Tools/FFmpeg/ffmpeg.exe` (the path UMB checks first via ).
2. **Windows-only, today.** Every helper in `Tools/` is a Windows `.exe` or DLL: `nus3audio.exe`, `bgm-property.exe`, `VGAudioCli.exe`, `vgmstream` DLLs, `ultimate_tex_cli.exe`, `paracobNET.dll`. UMB itself is .NET 8 cross-platform code, so in principle a Linux/macOS port is straightforward - swap each binary for one compiled for that platform.

---

## Build & Run

```bash
cd UMB.CLI
dotnet build
dotnet run
```

Configuration lives in `UMB.CLI/bin/Debug/net8.0/appsettings.json`. Mods to build live in `Mods/MusicMods/`. Output goes to `ArcOutput/`, which UMB fully clears at the start of every build (after a Y/N confirmation, unless `SkipOutputPathCleanupConfirmation = true`).

---

## series.toml - custom series

Each series folder under a mod (e.g. `Mods/MusicMods/<mod-name>/<series-folder>/`) contains a `series.toml` describing the series and a `tracks.csv` listing its songs. The `series.toml` for new modded series uses the following tables and fields:

```toml
[series]
id = "the-finals"
name = "THE FINALS"
playlist-incidence = 100
series-playlist = "bgm_the-finals"

[[games]]
id = "the-finals"
name = "THE FINALS"

[[playlists]]
id = "bgm_the-finals"
incidence = 100
songs = "*"

[default-track-data]
game = "the-finals"
author = "Embark Studios"
copyright = "© 2023 Embark Studios"
record-type = "original"
volume = 1.0
```

Field notes:

* `[series].id` / `name` - internal id and the human-readable label shown in-game.
* `playlist-incidence` - the random-roll weight every track in this series gets when it's added to the auto-generated `bgm_gametitle_<id>` playlist. Per-`[[playlists]]` blocks override this with their own `incidence`.
* `series-playlist` - an optional additional playlist that always receives every song in the series. Scaffold writes `bgm_<id>` for new series. The unconditional `bgm_gametitle_<id>` playlist is created regardless of this field.
* `[[games]]` - one block per "game" the series songs belong to. Custom series need at least one. The `id` here is what `tracks.csv`'s `game` column references.
* `[[playlists]]` - additional playlists this series' songs should appear in.
  * `id` - playlist identifier (e.g. an existing stage playlist id, or a new one UMB will create).
  * `incidence` - random-roll weight for the songs added by this block.
  * `songs` - either `"*"` (every song in the series) or a TOML array of filenames. The matcher is stem-tolerant: `"Destroyer"` and `"Destroyer.nus3audio"` both match a `tracks.csv` row whose filename is `Destroyer.nus3audio`. Pre-build validation warns about entries that don't match anything; `Cleanup` prunes them automatically.
* `[default-track-data]` - defaults applied to new rows that the `Scaffold` action adds to `tracks.csv` for any audio file it discovers.

## series.toml - extra flags for existing in-game series

When the series folder name matches a series that already exists in vanilla, scaffolding produces a slightly different toml. The structure above still applies, plus:

```toml
[series]
id = "example_series"               # matches the vanilla series id (without the ui_series_ prefix)
name = "Example Series"             # the english label this series uses in vanilla
existing-series = true              # tells UMB the series exists already - skip creating a SeriesEntry
playlist-incidence = 100
series-playlist = "bgmexample"      # the vanilla stage playlist for this series (scaffold fills this in)

[[games]]
id = "example_game_one"             # vanilla games for this series, plus any custom sub-games you want
name = "Example Game One"
[[games]]
id = "example_game_two"
name = "Example Game Two"
```

When `existing-series = true`:

* No `SeriesEntry` is emitted (the vanilla one is reused).
* Game-title entries are still emitted - vanilla duplicates are deduped by the audio state service, but custom sub-games (e.g. a new entry under an existing series) get registered.
* `series-playlist` is set to the series' canonical vanilla stage playlist, so your songs join the random rotation on every stage that uses it.
* An optional `song_order.toml` next to `series.toml` (written by the `Order Tracks` action) lets you interleave your songs with vanilla ones in the Sound Test / My Music ordering.

---

## Main menu actions

Every action below is reachable interactively (`dotnet run`) or as a CLI argument (`dotnet run -- <action>`). Action keys appear in parentheses.

* **Build** (`build`) - runs the pre-build validator (orphaned audio files, missing `order` columns, unknown `songs` references, games not declared in `series.toml`, etc.), wipes `ArcOutput/` after confirmation, then compiles every selected mod into game-ready `.prc`/`.msbt`/`.nus3audio`/`.nus3bank`/`.bntx` files. You can pick a single mod or a subset of series within it from the prompts.
* **Scaffold** (`scaffold`) - for every series folder under `Mods/MusicMods/`: creates `series.toml` and `tracks.csv` if missing, backfills `[default-track-data]`, `series-playlist`, and `songs = "*"` on any `[[playlists]]` block that lacks them, and adds rows to `tracks.csv` for every audio file in the folder that isn't already listed. Idempotent - safe to re-run any time you drop new audio files in.
* **Nus3 Convert** (`nus3-convert`) - converts source audio (`.mp3`/`.flac`/`.wav`/`.ogg`) for a chosen series into `.nus3audio` with detected loop points, dropping the results into a `songs-to-validate/` subfolder for review. See [Nus3 conversion & acceptance](#nus3-conversion--acceptance) below.
* **Accept Nus3** (`accept-nus3`) - promotes validated `.nus3audio` files from `songs-to-validate/` into the series folder proper, updates `tracks.csv` to point at the new filenames, and (optionally) deletes the source audio files.
* **Convert** (`convert`) - imports an old Sma5hMusic mod (`metadata_mod.json` + audio under `Mods/OldMods/<name>/`) into UMB's folder-based format. Auto-runs `Scaffold` on the converted mod afterward so `series-playlist`, `songs`, and `[default-track-data]` are all present.
* **Merge** (`merge`) - combines two or more UMB mods into a single output mod, preserving series toml metadata from the priority mod.
* **Extract Icons** (`extract-icons`) - pulls series icons out of an already-built Sma5h mod's BNTX files (useful when re-exporting from an existing mod).
* **Cleanup** (`cleanup`) - removes `tracks.csv` rows that point at audio files no longer on disk, and prunes dead filenames out of every `[[playlists]] songs` array. Safe to run any time.
* **Order Series** (`order-series`) - drag-and-drop reorder for the custom-series display order across a mod. Writes `series-order.toml` at the mod root.
* **Order Tracks** (`order-tracks`) - drag-and-drop reorder for tracks within one series. For `existing-series` mods you can interleave your tracks with vanilla ones; result writes a `song_order.toml` and updates the `order` column in `tracks.csv`.
* **Config Volume** (`config-volume`) - preview tracks at their post-build loudness (after LUFS normalization is applied) and override per-track gain. Requires FFmpeg.
* **Dump Stages** (`dump-stages`) - diagnostic. Loads vanilla state and prints every stage's `UiStageId` / `UiSeriesId` / `BgmSetId`, then writes `stages_dump.csv`. Use this to confirm which playlist (e.g. `bgmzelda`) a given stage rolls from before targeting it in `[[playlists]]`.

---

## Nus3 conversion & acceptance

Smash Ultimate plays audio out of `.nus3audio` containers (Namco OPUS or IDSP inside) with embedded loop points. UMB ships a two-step "convert → review → accept" flow so you can sanity-check loops before they make it into a build.

**Step 1 - `Nus3 Convert`.** Drop your source audio (`.mp3`, `.flac`, `.wav`, `.ogg`) into a series folder. Run `nus3-convert`, pick the series, and pick a minimum loop score (default 94.5%) and preview length. UMB:

1. Converts each source file to a temporary `.idsp`/OPUS (via `VGAudioCli.exe`).
2. Scans for the highest-scoring loop point (autocorrelation against the audio waveform).
3. Wraps it in a `.nus3audio` container (via `nus3audio.exe`).
4. Writes the result to a `songs-to-validate/` subfolder inside the series folder, alongside a short preview clip you can listen to in any audio app to confirm the loop is musically sensible.

Files that fail the loop-score threshold or already exist in `songs-to-validate/` are skipped and reported.

**Step 2 - review.** Listen to the preview clips. If a loop is wrong, delete that `.nus3audio` from `songs-to-validate/`; keeping it means "I'm happy with this loop." (You can also re-run `nus3-convert` with a stricter score if you want to gate looser hits.)

**Step 3 - `Accept Nus3`.** Run `accept-nus3` on the same series. UMB:

1. Moves every surviving `.nus3audio` from `songs-to-validate/` into the series folder.
2. Updates `tracks.csv` so the corresponding row's `filename` points at the new `.nus3audio` instead of the original source file (matched by basename - `My Song.mp3` → `My Song.nus3audio`).
3. Optionally deletes the source audio files (you'll be prompted).
4. Re-runs `Scaffold` so any newly-added rows get the proper defaults.

After acceptance, the series folder is ready for `Build`.

---

## Existing series fix (2026-03-25)

Adding songs to existing series (Final Fantasy, Persona, etc.) required two fixes:

1. **`GameTitleEntry` creation** (`FolderMusicMod.cs`) - previously skipped when `existing-series = true`. Custom sub-games still need entries so the game-title → series lookup resolves; UMB now always creates them and `AudioStateService.AddGameTitleEntry()` dedupes against vanilla.
2. **Stage playlist assignment** (`Sma5hMusic.cs`) - `AddModSongsToAllPlaylists()` only added mod songs to `bgmsmashbtl` (Battlefield). It now maps each song's game-title → series → stage `BgmSetId` so songs land on the correct series playlists.
3. **Playlist merging** (`AudioStateService.cs`) - `AddPlaylistEntry()` silently dropped tracks when a playlist id already existed. It now merges new tracks into the existing playlist.

---

## Vulnerabilities

This project uses AutoMapper 14.0.0, which has a [known high-severity DoS vulnerability](https://github.com/advisories/GHSA-rvv3-g6hj-g44x). AutoMapper 14 is the latest version available under the MIT license. Because UMB runs locally, the vulnerability poses no practical risk - but **do not deploy it as a service or expose it on a network.**

---

## Thanks & repositories

UMB is a fork of [Sma5hMusic](https://github.com/Deinonychus71/Sma5hMusic) by Deinonychus71. Most of the heavy lifting was done there.

Tools and contributors:

1. Research: soneek
2. Original Sma5hMusic testing: Demonslayerx8, Segtendo
3. Original Sma5hMusic icon: Segtendo
4. prcEditor - https://github.com/BenHall-7/paracobNET - BenHall-7
5. paramLabels - https://github.com/ultimate-research/param-labels - BenHall-7, jam1garner, Dr-HyperCake, Birdwards, ThatNintendoNerd, ScanMountGoat, Meshima, Blazingflare, TheSmartKid, jugeeya, Demonslayerx8
6. msbtEditor - https://github.com/IcySon55/3DLandMSBTeditor - IcySon55, exelix11
7. nus3audio - https://github.com/jam1garner/nus3audio-rs - jam1garner
8. bgm-property - https://github.com/jam1garner/smash-bgm-property - jam1garner
9. VGAudio - https://github.com/Thealexbarney/VGAudio - Thealexbarney, soneek, jam1garner, devlead, Raytwo, nnn1590
10. vgmstream - https://github.com/vgmstream/vgmstream - bnnm, kode54, NicknineTheEagle, bxaimc, Thealexbarney; full contributor list at https://github.com/vgmstream/vgmstream/graphs/contributors
11. CrossArc - https://github.com/Ploaj/ArcCross - Ploaj, ScanMountGoat, BenHall-7, shadowninja108, jam1garner, M-1-RLG
12. ultimate_tex_cli - https://github.com/ScanMountGoat/ultimate_tex - ScanMountGoat (used to build series-icon BNTX files at build time)
13. FFmpeg - https://ffmpeg.org/ - required at runtime for LUFS normalization and audio preview; not bundled
