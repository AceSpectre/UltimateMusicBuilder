# UltimateMusicBuilder

## Philosophy
Ideally we should need to make only minimal changes to the original code of this project, since this project did work and we're mainly just changing the input format.

## Build & Run
```bash
cd Sma5h.CLI
dotnet build
dotnet run
```
Configuration is in `Sma5h.CLI/bin/Debug/net8.0/appsettings.json`.

## Project Structure
- `Sma5h/Core/Sma5h.Core/` — Core framework (StateManager, interfaces, resource providers)
- `Sma5h/Mods/Sma5h.Mods.Music/` — Music mod logic (audio state, services, mod formats)
  - `MusicMods/FolderMusicMod/` — Folder-based mod format (series.toml + tracks.csv)
  - `MusicMods/MusicModConfig/` — Original JSON-based mod format
  - `Services/` — AudioStateService, Nus3AudioService, metadata services
  - `Helpers/MusicConstants.cs` — All ID prefixes, file constants, valid extensions
- `Sma5h.CLI/` — Console entry point
- `Mods/MusicMods/` — Actual mod data (test mods live here)
- `Resources/` — ParamLabels.csv and other reference data

## Key Concepts
- Mod output goes to `Sma5h.CLI/bin/Debug/net8.0/ArcOutput/`
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

## Testing
Test on Nintendo Switch by copying ArcOutput to the SD card mod folder. No automated test suite exists.