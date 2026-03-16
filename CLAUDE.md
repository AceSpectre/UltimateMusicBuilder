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

## Testing
Test on Nintendo Switch by copying ArcOutput to the SD card mod folder. No automated test suite exists.