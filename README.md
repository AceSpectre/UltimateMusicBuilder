# Ultimate Music Builder (UMB)

UMB is a fork of [Sma5hMusic](https://github.com/Deinonychus71/Sma5hMusic) for adding music to Super Smash Bros. Ultimate. It keeps Sma5h's core mod-build pipeline, but replaces the JSON metadata format with a folder-based layout (`series.toml` + `tracks.csv`) that's easier to manipulate for batch song additions to the game.

> **Heads up.** This tool is highly experimental and may not always work as expected.
> * Always keep backups of your files before using them with this tool.
> * **Mods are not safe online.**

---

## Dependencies

1. **FFmpeg** - For LUFS volume normalisation, use `appsettings.json → Sma5hMusic.LufsNormalization.FfmpegPath` to point to local install
   - Just install via commandline `winget/choco/apt-install/pacman/brew` for easiest setup
2. **pymusiclooper** - For detecting loop points during standard audio -> `nus3audio` conversion
   - Global install via `pip`
3. A partial dump of Smash Ultimate assets from `data.arc` (identical to Sma5h) in `Resources/Game`
   - [Video guide to dump](https://youtu.be/CXe_Su-Yo2c?si=8iFYrD_xxfrzwhog)
   - [Guide to setup resources](https://github.com/Deinonychus71/Sma5hMusic/wiki/Setup) 


## Usage 

Download the release for your OS from releases, note that the Linux/MacOS builds haven't been as extensively tested as the Windows build. 

To import your existing Sma5h mods, place them in `Mods/OldMods` and run the convert action from the main menu.

To create a new mod just create a new folder in `Mods/MusicMods`. 

Every action below is reachable interactively through the terminal menu or directly via `UMB.CLI <action>`.

* **Build** - validates the target mod and builds it to be used on your SD card
* **Scaffold** - creates `series.toml`, `tracks.csv` for every series. If song entries are missing, backfills using `[default-track-data]` table. Safe to re-run any time you drop new audio files in.
* **Nus3 Convert** - converts standard audio (`.mp3`/`.flac`/`.wav`/`.ogg`) for a series into `.nus3audio` with detected loop points, results dumped into `songs-to-validate/` subfolder for review. See [Nus3 conversion & acceptance](#nus3-conversion--acceptance) below.
* **Accept Nus3** (`accept-nus3`) - promotes validated `.nus3audio` files from `songs-to-validate/` into the series folder proper, updates `tracks.csv` to point at the new filenames, and (optionally) deletes the source audio files.
* **Convert** (`convert`) - imports an old Sma5hMusic mod (`metadata_mod.json` + audio under `Mods/OldMods/<name>/`) into UMB's folder-based format.
* **Merge** (`merge`) - combines two or more UMB mods into a single output mod, preserving series toml metadata from the priority mod.
* **Extract Icons** (`extract-icons`) - pulls series icons out of an already-built Sma5h mod's BNTX files.
* **Cleanup** (`cleanup`) - removes `tracks.csv` rows that point at deleted files, prunes dead filenames out of `[[playlists]] songs` array. Safe to run any time.
* **Order Series** (`order-series`) - drag-and-drop reorder for the custom-series display order across a mod. Writes `series-order.toml` at mod root.
* **Order Tracks** (`order-tracks`) - drag-and-drop reorder for tracks within series. `existing-series` mods can interleave tracks with vanilla songs.
* **Config Volume** (`config-volume`) - preview tracks at their post-build loudness (after LUFS normalization) and override per-track gain.
* **Dump Stages** (`dump-stages`) - diagnostic. Loads vanilla state and prints every stage's `UiStageId` / `UiSeriesId` / `BgmSetId`, then writes `stages_dump.csv`, for debug purposes.

---

## series.toml - custom series

Each series folder contains a `series.toml` and `tracks.csv`. For new modded series:

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

Fields:

* `[series].id` / `name` - internal id and in-game label.
* `playlist-incidence` - default random-roll weight for the auto-generated `bgm_gametitle_<id>` playlist. Per-`[[playlists]]` blocks can override with their own `incidence`.
* `series-playlist` - optional extra playlist that receives every song. Scaffold sets `bgm_<id>` for new series. `bgm_gametitle_<id>` is created regardless.
* `[[games]]` - one block per "game". Custom series need at least one. `id` is referenced by `tracks.csv`'s `game` column.
* `[[playlists]]` - extra playlists for this series' songs.
  * `id` - playlist identifier (existing stage playlist, or a new one UMB will create).
  * `incidence` - random-roll weight for this block's songs.
  * `songs` - `"*"` for all, or a TOML array of filenames. Stem-tolerant: `"Destroyer"` matches `Destroyer.nus3audio`. Pre-build validation warns on no-match entries; `Cleanup` prunes them.
* `[default-track-data]` - defaults applied to rows `Scaffold` adds to `tracks.csv`.

## series.toml - existing in-game series

When the folder name matches a vanilla series, scaffolding writes a slightly different toml. Structure above still applies, plus:

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

* No `SeriesEntry` is emitted (vanilla one is reused).
* Game-title entries are still emitted. Vanilla duplicates are deduped; custom sub-games get registered.
* `series-playlist` points at the vanilla stage playlist, so your songs join the random rotation on every stage using it.
* Optional `song_order.toml` (written by `Order Tracks`) interleaves your songs with vanilla in Sound Test / My Music.

---

## Nus3 conversion & acceptance

Smash Ultimate plays audio from `.nus3audio` containers (Namco OPUS or IDSP) with embedded loop points. UMB uses a convert -> review -> accept flow to sanity-check loops before building.

**Step 1 - `Nus3 Convert`.** Drop source audio (`.mp3`/`.flac`/`.wav`/`.ogg`) into a series folder. Run `nus3-convert`, pick the series, set the minimum loop score (default 94.5%) and preview length. Optionally enable auto-convert to skip the per-song preview prompt and accept the top-ranked loop above threshold. UMB:

1. Converts each source to temporary `.idsp`/OPUS via `VGAudioCli`.
2. Scans for the highest-scoring loop point (waveform autocorrelation).
3. Wraps it in a `.nus3audio` via `nus3audio`.
4. Writes results to `songs-to-validate/` in the series folder, plus a preview clip.

Files below the loop-score threshold or already in `songs-to-validate/` are skipped.

**Step 2 - review.** Listen to the preview clips. Delete any `.nus3audio` whose loop is wrong; keeping it means "good loop". Re-run `nus3-convert` with a stricter score to gate looser hits.

**Step 3 - `Accept Nus3`.** Run `accept-nus3` on the same series. UMB:

1. Moves surviving `.nus3audio` from `songs-to-validate/` into the series folder.
2. Updates `tracks.csv` so the row's `filename` points at the new `.nus3audio` (matched by basename: `My Song.mp3` -> `My Song.nus3audio`).
3. Optionally deletes the source audio files (prompts).
4. Re-runs `Scaffold` so new rows get defaults.

After acceptance the series folder is ready for `Build`.

---

## Build & Run for developers

```bash
cd UMB.CLI
dotnet build
dotnet run
```

---

## Compiling release builds

```bash
dotnet publish UMB.CLI -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:DebugType=embedded
```

---

## Vulnerabilities

Uses AutoMapper 14.0.0 which has a [known high-severity DoS vulnerability](https://github.com/advisories/GHSA-rvv3-g6hj-g44x). AutoMapper 14 is the latest MIT-licensed version. UMB runs locally so the risk is not practical - but **do not deploy as a service or expose to a network.**

---

## Thanks & repositories

UMB is a fork of [Sma5hMusic](https://github.com/Deinonychus71/Sma5hMusic) by Deinonychus71. Most of the heavy lifting was done there.

Music used in the test suite is from flowerhead's [somewhatgood](https://somewhatgood.bandcamp.com/) music collection, specifically the Karts and Lofi albums. 

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
