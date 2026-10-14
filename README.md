# Ultimate Music Builder (UMB)

UMB is a fork of [Sma5hMusic](https://github.com/Deinonychus71/Sma5hMusic) for adding music to Super Smash Bros. Ultimate. It keeps Sma5h's core mod-build pipeline, but replaces the JSON metadata format with a folder-based layout (`series.toml` + `tracks.csv`) that's easier to manipulate for batch song additions to the game.

> **Heads up.** This tool is highly experimental and may not always work as expected.
> * Always keep backups of your files before using them with this tool.
> * **Mods are not safe online.**

---

## Dependencies

1. **FFmpeg** - For LUFS volume normalisation, decoding, waveforms, and playback previews. `ffmpeg`, `ffprobe`, and `ffplay` must be on PATH.
2. **pymusiclooper** - For detecting loop points during standard audio -> `nus3audio` conversion
   - Installed in an isolated environment using pipx, rather than into system Python.
3. A partial dump of Smash Ultimate assets from `data.arc` (identical to Sma5h) in `Resources/Game`
   - [Video guide to dump](https://youtu.be/CXe_Su-Yo2c?si=8iFYrD_xxfrzwhog)
   - [Guide to setup resources](https://github.com/Deinonychus71/Sma5hMusic/wiki/Setup) 

### One-time setup

Run from the extracted release folder or a source checkout:

```bash
# macOS / Linux
bash setup.sh
bash setup.sh --dry-run   # inspect commands without installing anything
```

```powershell
# Windows
powershell -NoProfile -ExecutionPolicy Bypass -File .\setup.ps1
# Add -DryRun to inspect commands without installing anything.
```

Setup checks for existing commands and installs only missing dependencies.
macOS requires Homebrew; Linux supports Debian/Ubuntu (`apt`), Fedora (`dnf`),
and Arch (`pacman`); Windows requires App Installer (`winget`). System package
installs may ask for administrator access. Fedora uses its `ffmpeg-free` package;
codec availability depends on the distribution's build. Setup uses pipx for
pymusiclooper and adds its application directory to PATH. Open a new terminal
after setup, and restart UMB if it was already running.

Game data and `Resources/template.nus3bank` must be supplied separately; setup
does not download these files.

### Developer setup

Install the .NET 8 SDK, Node.js 20+ and npm first. Unix developers also need
Rust/cargo to build `bgm-property` if it isn't already present. Then run:

```bash
bash setup.sh --dev
```

On Windows, add `-Dev` to the PowerShell command above. Developer setup also
runs the existing `scripts/fetch-tools.sh` / `scripts/fetch-tools.ps1` scripts
and `npm ci` for the desktop app. It checks the toolchain before installing
runtime dependencies, and works from any current directory.

Release archives bundle tools for their target OS. A Git checkout currently
contains Windows tool binaries; the fetch scripts supply pinned versions for
the developer's OS. On Apple Silicon, the currently pinned `nus3audio` binary
requires Rosetta. On macOS, setup builds pinned `vgmstream-cli` sources with
static codec libraries; Homebrew installs CMake and the codec build dependencies
once. This requires Xcode Command Line Tools (`xcode-select --install`). The
resulting executable does not depend on Homebrew codec versions at runtime.
Released apps are self-contained and don't need Node.js,
the .NET SDK, or Rust installed.


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
* **Cleanup** (`cleanup`) - removes `tracks.csv` rows that point at deleted files, prunes dead filenames out of `[[playlists]] songs` array, and drops `playlists.toml` songs that are neither vanilla nor in the mod (needs the game files). Safe to run any time.
* **Order Series** (`order-series`) - drag-and-drop reorder for the custom-series display order across a mod. Writes `series-order.toml` at mod root. The desktop app also lists every vanilla series: editing one (games, song defaults, icon) adds it to the mod as an `existing-series` folder.
* **Order Tracks** (`order-tracks`) - drag-and-drop reorder for tracks within series. In `existing-series` folders vanilla songs are editable rows too, and can be interleaved with mod songs.
* **Config Volume** (`config-volume`) - preview tracks at their post-build loudness (after LUFS normalization) and set the series and per-track dB boosts. Vanilla songs take manual track boosts only.
* **Dump Stages** (`dump-stages`) - diagnostic. Loads vanilla state and prints every stage's `UiStageId` / `UiSeriesId` / `BgmSetId`, then writes `stages_dump.csv`, for debug purposes.

---

## series.toml - custom series

Each series folder contains a `series.toml` and `tracks.csv`. For new modded series:

```toml
[series]
id = "the-finals"
name = "THE FINALS"
playlist-incidence = 100

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
volume-db = 0.0

[[game-defaults]]
game = "the-finals"
author = "Embark Studios Audio"

[[presets]]
name = "Remix"
record-type = "arrange"
```

Fields:

* `[series].id` / `name` - internal id and in-game label.
* `playlist-incidence` - default random-roll weight of the series' songs in its own playlists.
* The series' playlist is set in [playlists.toml](#playliststoml---playlists-and-stage-playlists) (Scaffold and Manage Series write it there: `bgm_<id>` for new series). A `series-playlist` key here is legacy and is migrated by Manage Playlists.
* The build also makes a hidden `bgm_gametitle_<id>` playlist of every song in the series, so its songs are always in a playlist. Mods cannot edit it or give it to a stage.
* `[[games]]` - one block per "game". Custom series need at least one. `id` is referenced by `tracks.csv`'s `game` column.
* `[[playlists]]` - legacy: extra playlists for this series' songs. Manage Playlists offers to migrate these into [playlists.toml](#playliststoml---playlists-and-stage-playlists).
  * `id` - playlist identifier (existing stage playlist, or a new one UMB will create).
  * `incidence` - random-roll weight for this block's songs.
  * `songs` - `"*"` for all, or a TOML array of filenames. Stem-tolerant: `"Destroyer"` matches `Destroyer.nus3audio`. Pre-build validation warns on no-match entries; `Cleanup` prunes them.
* `[default-track-data]` - defaults applied to rows `Scaffold` adds to `tracks.csv`, plus `volume-db`: the series dB boost (+ or -) added to every track in the series. Edit it in Config Volume.
* `[[game-defaults]]` / `[[presets]]` - song presets, edited in the desktop app's Manage Presets view and applied from Manage Songs. A game default fills songs from that `game`; named presets can be picked per song. Keys left out (`author`, `copyright`, `record-type`, `in-soundtest`) fall back to the game default, then `[default-track-data]`.

Volumes are dB boosts (0 = unchanged): the build adds the global boost (`GlobalVolumeDb` in settings), the series `volume-db`, LUFS auto-gain and the track's `volume_db` column in `tracks.csv`. Older mods that stored linear `volume` multipliers are converted automatically the next time the CLI starts. Vanilla songs never get LUFS auto-gain, and their volume is left untouched unless the experimental `ApplyVolumeToVanilla` setting is on: then they get the global, series and `volume_db` row boosts (see below), and Config Volume lists them.

## series.toml - existing in-game series

When the folder name matches a vanilla series, scaffolding writes a slightly different toml. Structure above still applies, plus:

```toml
[series]
id = "example_series"               # matches the vanilla series id (without the ui_series_ prefix)
name = "Example Series"             # the english label this series uses in vanilla
existing-series = true              # tells UMB the series exists already - skip creating a SeriesEntry
playlist-incidence = 100

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
* Scaffold flags the vanilla stage playlist with the series in playlists.toml, so your songs join the random rotation on every stage using it.
* Optional `song_order.toml` (written by `Order Tracks`) interleaves your songs with vanilla in Sound Test / My Music.
* An `icon.png` replaces the vanilla series icon.

### Editing vanilla songs

A `tracks.csv` row whose `filename` is a vanilla song's id (e.g. `ui_bgm_a01_smb_chijyou`, no extension) edits that song instead of adding one. The desktop app writes a row for every vanilla song of the series when the folder is created or opened in Manage Songs / Config Volume. Edited columns:

* `title`, `author`, `copyright` - a changed value replaces the text in every language; an unchanged one keeps the game's translations.
* `game`, `record_type`, `in_soundtest`, and `special_category` / `info1` (pinch song).
* `volume_db` - the song's dB boost; with `ApplyVolumeToVanilla` on, its nus3bank is regenerated with it.

## playlists.toml - playlists and stage playlists

An optional `playlists.toml` at the mod root holds the playlists and stage assignments edited in the desktop app's Manage Playlists view. When a mod still has legacy series.toml `[[playlists]]` blocks, Manage Playlists asks before migrating them here (back up the mod first); a block without a song list becomes a `series` flag.

```toml
[[playlists]]
id = "bgmmymix"                     # a new playlist, or a game/series playlist to replace
name = "My Mix"                     # only for new playlists
series = ["the-finals"]             # optional: always holds every song of these series
tracks = [
    { song = "ui_bgm_a01_smb_chijyou", order = 0, incidence = 100 },
    { song = "ui_bgm_destroyer", order = [1, 1, 1, -1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1], incidence = 50 },
]

[[stages]]
id = "ui_stage_battle_field"
playlist = "bgmmymix"
order = 0
```

* A playlist here replaces the whole playlist of that id. The build then still adds the songs it always keeps: every song of a series in the playlists flagged with it, and every mod song in its series' stage playlists and in `bgmsmashbtl` (settings `AddModSongsToSmashBattle` and `SmashBattleIncidence`, default on at incidence 0). The build also turns on album selection (My Music) on every stage unless `EnableAlbumSelectionOnAllStages` is off; all three are in the desktop app's General settings. New songs therefore join saved playlists too; to keep one off a stage, leave it out of that order instead. Songs are ui_bgm ids; a mod song's id is `ui_bgm_` plus its filename in lowercase with other characters as `_`.
* Every stage plays one order (0-15) of its playlist. `order` and `incidence` are one value for all 16 orders, or an array with one per order; order `-1` leaves the song out of that order.
* `series` makes a dedicated series playlist: every song of those series is always in it, without listing them. Without `tracks` the playlist is left as it is (or created) plus those songs; Manage Series' "Series Playlist" field edits this flag.
* `[[stages]]` sets the playlist and order a stage plays.
* Manage Playlists can reset a changed game or series playlist to its default, which removes it from this file. Changing a series' playlist in Manage Series renames its own playlist here, with the stages playing it, or moves the flag to the playlist chosen.

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
