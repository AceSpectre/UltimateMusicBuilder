using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Models;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;

namespace UMB.CLI.Services
{
    public class ScaffoldService
    {
        private const string DefaultLocale = "en_us";

        private static readonly string[] ExpectedCsvColumns =
        {
            "filename", "game", "title", "author", "copyright",
            "record_type", "special_category", "volume", "info1", "in_soundtest"
        };

        private readonly ILogger _logger;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;
        private readonly IAudioStateService _audioStateService;

        private bool _vanillaLoadAttempted;
        private Dictionary<string, SeriesEntry> _vanillaSeriesById;
        private ILookup<string, GameTitleEntry> _vanillaGamesBySeriesId;
        private List<BgmDbRootEntry> _vanillaBgmEntries;
        private Dictionary<string, string> _vanillaPlaylistBySeriesId;

        public ScaffoldService(IOptionsMonitor<Sma5hMusicOptions> musicConfig, IAudioStateService audioStateService, ILogger<ScaffoldService> logger)
        {
            _musicConfig = musicConfig;
            _audioStateService = audioStateService;
            _logger = logger;
        }

        public void Run()
        {
            Script.PrintBanner(_logger);

            var modPath = _musicConfig.CurrentValue.Sma5hMusic.ModPath;
            Directory.CreateDirectory(modPath);

            var modDirs = Directory.GetDirectories(modPath, "*", SearchOption.TopDirectoryOnly);
            if (modDirs.Length == 0)
            {
                _logger.LogWarning("No mod folders found in {ModPath}. Create a mod folder first.", modPath);
                return;
            }

            var validExtensions = new HashSet<string>(MusicConstants.VALID_MUSIC_EXTENSIONS, StringComparer.OrdinalIgnoreCase);
            int totalScaffolded = 0;
            int totalAdded = 0;
            int totalDefaultsAdded = 0;
            int totalColumnsUpdated = 0;
            int totalSongOrdersCreated = 0;
            int totalSeriesOrderAdded = 0;

            foreach (var modDir in modDirs)
            {
                if (Path.GetFileName(modDir).StartsWith("."))
                    continue;

                foreach (var seriesDir in Directory.GetDirectories(modDir))
                {
                    var folderName = Path.GetFileName(seriesDir);
                    var tomlPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
                    var csvPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);

                    // ── Step 1: Create missing series.toml / tracks.csv ──
                    bool wasScaffolded = false;
                    if (!File.Exists(tomlPath))
                    {
                        var tomlContent = BuildSeriesToml(folderName);
                        File.WriteAllText(tomlPath, tomlContent);
                        _logger.LogInformation("Created {Path}", tomlPath);
                        wasScaffolded = true;
                    }
                    if (!File.Exists(csvPath))
                    {
                        var csvContent = "filename,game,title,author,copyright,record_type,special_category,volume,info1,in_soundtest\n";
                        File.WriteAllText(csvPath, csvContent);
                        _logger.LogInformation("Created {Path}", csvPath);
                        wasScaffolded = true;
                    }
                    if (wasScaffolded)
                        totalScaffolded++;

                    // ── Step 2: Ensure [default-track-data] section exists ──
                    if (EnsureDefaultTrackDataSection(tomlPath))
                    {
                        _logger.LogInformation("Added [default-track-data] section to {Path}", tomlPath);
                        totalDefaultsAdded++;
                    }

                    // ── Step 2a: Ensure series-playlist field and songs = "*" on each [[playlists]] block ──
                    if (EnsureSeriesPlaylistField(tomlPath))
                        _logger.LogInformation("Added series-playlist field to {Path}", tomlPath);
                    var addedSongs = EnsureSongsOnPlaylistBlocks(tomlPath);
                    if (addedSongs > 0)
                        _logger.LogInformation("Added songs = \"*\" to {Count} [[playlists]] block(s) in {Path}", addedSongs, tomlPath);

                    // ── Step 3: Populate tracks.csv with any new music files ──
                    var tomlText = File.ReadAllText(tomlPath);
                    var tomlOptions = new TomlModelOptions { ConvertPropertyName = ToKebabCase };
                    FolderSeriesFileConfig seriesFile;
                    try
                    {
                        seriesFile = Toml.ToModel<FolderSeriesFileConfig>(tomlText, options: tomlOptions);
                    }
                    catch (System.Exception e)
                    {
                        _logger.LogError(e, "Failed to parse {Path}, skipping populate.", tomlPath);
                        continue;
                    }

                    var defaults = seriesFile.DefaultTrackData;

                    // Read existing CSV rows (dynamic — preserves all columns including "order")
                    var (existingRows, existingHeaders) = ReadCsvRows(csvPath);

                    var existingFilenames = new HashSet<string>(
                        existingRows
                            .Where(r => r.TryGetValue("filename", out var f) && !string.IsNullOrWhiteSpace(f))
                            .Select(r => r["filename"]),
                        StringComparer.OrdinalIgnoreCase);

                    // Find music files not already in CSV, sorted alphabetically
                    var newFiles = Directory.GetFiles(seriesDir)
                        .Where(f => validExtensions.Contains(Path.GetExtension(f)))
                        .Select(f => Path.GetFileName(f))
                        .Where(f => !existingFilenames.Contains(f))
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var currentHeaderSet = new HashSet<string>(existingHeaders, StringComparer.OrdinalIgnoreCase);
                    bool needsColumnUpdate = ExpectedCsvColumns.Any(h => !currentHeaderSet.Contains(h));
                    if (needsColumnUpdate)
                        existingHeaders = existingHeaders
                            .Concat(ExpectedCsvColumns.Where(h => !currentHeaderSet.Contains(h)))
                            .ToArray();

                    // Add new rows
                    foreach (var filename in newFiles)
                    {
                        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var h in existingHeaders)
                            row[h] = "";
                        row["filename"] = filename;
                        row["game"] = defaults?.Game ?? folderName;
                        row["title"] = Path.GetFileNameWithoutExtension(filename);
                        row["author"] = defaults?.Author ?? "";
                        row["copyright"] = defaults?.Copyright ?? "";
                        row["record_type"] = defaults?.RecordType ?? "original";
                        row["volume"] = (defaults?.Volume ?? 1.0f).ToString(CultureInfo.InvariantCulture);
                        existingRows.Add(row);
                    }

                    if (EnsureExistingSeriesSongOrderToml(seriesDir, seriesFile, existingRows))
                    {
                        _logger.LogInformation("Created {Path}",
                            Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_SONG_ORDER_TOML_FILE));
                        totalSongOrdersCreated++;
                    }

                    if (newFiles.Count == 0 && !needsColumnUpdate)
                        continue;

                    // Rewrite CSV with all rows (preserves every column)
                    WriteCsvRows(csvPath, existingRows, existingHeaders);

                    if (needsColumnUpdate)
                    {
                        var addedColumns = ExpectedCsvColumns.Where(h => !currentHeaderSet.Contains(h)).ToList();
                        _logger.LogInformation("Added column(s) {Columns} to {Path}", string.Join(", ", addedColumns), csvPath);
                        totalColumnsUpdated++;
                    }
                    if (newFiles.Count > 0)
                    {
                        _logger.LogInformation("Added {Count} track(s) to {Path}", newFiles.Count, csvPath);
                        totalAdded += newFiles.Count;
                    }
                }

                // ── Step 4: Append any new custom series to series-order.toml ──
                totalSeriesOrderAdded += SyncSeriesOrderToml(modDir);
            }

            if (totalScaffolded > 0)
                _logger.LogInformation("Scaffolded {Count} series folder(s).", totalScaffolded);
            if (totalDefaultsAdded > 0)
                _logger.LogInformation("Added [default-track-data] to {Count} series.toml file(s).", totalDefaultsAdded);
            if (totalAdded > 0)
                _logger.LogInformation("Populated {Count} new track(s) total.", totalAdded);
            if (totalColumnsUpdated > 0)
                _logger.LogInformation("Updated {Count} CSV file(s) with new columns.", totalColumnsUpdated);
            if (totalSongOrdersCreated > 0)
                _logger.LogInformation("Created {Count} song_order.toml file(s) for existing series.", totalSongOrdersCreated);
            if (totalSeriesOrderAdded > 0)
                _logger.LogInformation("Appended {Count} custom series to series-order.toml file(s).", totalSeriesOrderAdded);
            if (totalScaffolded == 0 && totalAdded == 0 && totalDefaultsAdded == 0 && totalColumnsUpdated == 0 && totalSongOrdersCreated == 0 && totalSeriesOrderAdded == 0)
                _logger.LogInformation("All series folders are up to date.");
        }

        /// <summary>
        /// Scans the mod's series folders for custom (non-existing, non-etc) series and appends any
        /// not already present in series-order.toml to the end of the order. Existing entries keep
        /// their relative position. Returns the number of newly appended IDs.
        /// </summary>
        private int SyncSeriesOrderToml(string modDir)
        {
            var customSeriesIds = new List<string>();
            foreach (var seriesDir in Directory.GetDirectories(modDir))
            {
                if (Path.GetFileName(seriesDir).StartsWith("."))
                    continue;

                var tomlPath = Path.Combine(seriesDir,
                    MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
                if (!File.Exists(tomlPath))
                    continue;

                FolderSeriesFileConfig config;
                try
                {
                    var tomlText = File.ReadAllText(tomlPath);
                    config = Toml.ToModel<FolderSeriesFileConfig>(tomlText,
                        options: new TomlModelOptions { ConvertPropertyName = ToKebabCase });
                }
                catch
                {
                    continue;
                }

                if (config.Series == null) continue;
                if (config.Series.ExistingSeries) continue;
                if (string.IsNullOrWhiteSpace(config.Series.Id)) continue;
                if (string.Equals(config.Series.Id, "etc", StringComparison.OrdinalIgnoreCase)) continue;

                customSeriesIds.Add(config.Series.Id);
            }

            var orderPath = Path.Combine(modDir,
                MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_ORDER_TOML_FILE);

            var existingOrder = new List<string>();
            if (File.Exists(orderPath))
            {
                try
                {
                    var toml = File.ReadAllText(orderPath);
                    var model = Toml.ToModel(toml);
                    if (model.TryGetValue("order", out var val) && val is TomlArray arr)
                        existingOrder = arr.OfType<string>().ToList();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse {Path}, will rewrite.", orderPath);
                }
            }

            var existingSet = new HashSet<string>(existingOrder, StringComparer.OrdinalIgnoreCase);
            var newEntries = customSeriesIds
                .Where(id => !existingSet.Contains(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (newEntries.Count == 0)
                return 0;

            var finalOrder = existingOrder.Concat(newEntries).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("# Custom series display order");
            sb.AppendLine("# Listed series appear after official series, before \"Other\"");
            sb.AppendLine("# Unlisted custom series will be placed after these");
            sb.AppendLine("order = [");
            foreach (var id in finalOrder)
                sb.AppendLine($"    \"{id}\",");
            sb.AppendLine("]");
            File.WriteAllText(orderPath, sb.ToString());

            _logger.LogInformation("Appended {Count} series to {Path}: {Names}",
                newEntries.Count, orderPath, string.Join(", ", newEntries));
            return newEntries.Count;
        }

        private bool EnsureExistingSeriesSongOrderToml(
            string seriesDir,
            FolderSeriesFileConfig seriesFile,
            IReadOnlyList<Dictionary<string, string>> trackRows)
        {
            if (seriesFile?.Series == null || !seriesFile.Series.ExistingSeries)
                return false;
            if (string.IsNullOrWhiteSpace(seriesFile.Series.Id))
                return false;

            var songOrderPath = Path.Combine(seriesDir,
                MusicConstants.MusicModFiles.FOLDER_MOD_SONG_ORDER_TOML_FILE);
            if (File.Exists(songOrderPath))
                return false;

            EnsureVanillaDataLoaded();

            var uiSeriesId = MusicConstants.InternalIds.SERIES_ID_PREFIX + seriesFile.Series.Id;
            var gameIdsInSeries = new HashSet<string>(
                (_vanillaGamesBySeriesId?[uiSeriesId] ?? Enumerable.Empty<GameTitleEntry>())
                    .Select(g => g.UiGameTitleId)
                    .Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);

            var vanillaSongIds = (_vanillaBgmEntries ?? new List<BgmDbRootEntry>())
                .Where(b => b.TestDispOrder >= 0
                    && !string.IsNullOrWhiteSpace(b.UiBgmId)
                    && !string.IsNullOrWhiteSpace(b.UiGameTitleId)
                    && gameIdsInSeries.Contains(b.UiGameTitleId))
                .OrderBy(b => b.TestDispOrder)
                .Select(b => b.UiBgmId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (vanillaSongIds.Count == 0)
            {
                _logger.LogWarning(
                    "Skipping {Path}: existing series {SeriesId} has no vanilla songs to seed ordering.",
                    songOrderPath,
                    seriesFile.Series.Id);
                return false;
            }

            var orderedIds = new List<string>(vanillaSongIds);
            var vanillaIdSet = new HashSet<string>(vanillaSongIds, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(vanillaSongIds, StringComparer.OrdinalIgnoreCase);
            foreach (var row in trackRows)
            {
                var filename = row?.GetValueOrDefault("filename", "");
                if (string.IsNullOrWhiteSpace(filename))
                    continue;

                var toneId = FolderMusicMod.DeriveToneId(filename);
                if (string.IsNullOrWhiteSpace(toneId))
                    continue;

                var uiBgmId = MusicConstants.InternalIds.UI_BGM_ID_PREFIX + toneId;
                if (seen.Add(uiBgmId))
                    orderedIds.Add(uiBgmId);
            }

            WriteSongOrderToml(songOrderPath, orderedIds, vanillaIdSet);
            return true;
        }

        private static void WriteSongOrderToml(
            string tomlPath,
            IReadOnlyList<string> orderedIds,
            ISet<string> vanillaIds)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Generated by UltimateMusicBuilder scaffold for an existing-series mod.");
            sb.AppendLine("# Vanilla songs keep their in-game order; mod songs are appended after them.");
            sb.AppendLine("song_order = [");
            for (int i = 0; i < orderedIds.Count; i++)
            {
                var uiBgmId = orderedIds[i];
                var tag = vanillaIds.Contains(uiBgmId) ? "vanilla" : "mod";
                var comma = i < orderedIds.Count - 1 ? "," : "";
                sb.AppendLine($"  \"{EscapeTomlString(uiBgmId)}\"{comma} # {tag}");
            }
            sb.AppendLine("]");
            File.WriteAllText(tomlPath, sb.ToString());
        }

        // ── series.toml generation ───────────────────────────────────────────

        private string BuildSeriesToml(string folderName)
        {
            EnsureVanillaDataLoaded();

            var uiSeriesId = MusicConstants.InternalIds.SERIES_ID_PREFIX + folderName;
            if (_vanillaSeriesById != null && _vanillaSeriesById.TryGetValue(uiSeriesId, out var vanillaSeries))
                return BuildExistingSeriesToml(folderName, vanillaSeries);

            return BuildNewSeriesToml(folderName);
        }

        private string BuildNewSeriesToml(string folderName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[series]");
            sb.AppendLine($"id = \"{EscapeTomlString(folderName)}\"");
            sb.AppendLine($"name = \"{EscapeTomlString(folderName)}\"");
            sb.AppendLine("playlist-incidence = 100");
            sb.AppendLine($"series-playlist = \"bgm_{EscapeTomlString(folderName)}\"");
            sb.AppendLine();
            sb.AppendLine("[[games]]");
            sb.AppendLine($"id = \"{EscapeTomlString(folderName)}\"");
            sb.AppendLine($"name = \"{EscapeTomlString(folderName)}\"");
            sb.AppendLine();
            AppendDefaultTrackData(sb, folderName);
            return sb.ToString();
        }

        private string BuildExistingSeriesToml(string folderName, SeriesEntry vanillaSeries)
        {
            var uiSeriesId = vanillaSeries.UiSeriesId;
            var seriesId = vanillaSeries.NameId ?? folderName;
            var seriesName = ResolveLocalizedName(vanillaSeries.MSBTTitle, seriesId);
            var games = (_vanillaGamesBySeriesId?[uiSeriesId] ?? Enumerable.Empty<GameTitleEntry>())
                .OrderBy(g => g.NameId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogInformation("Series '{Folder}' matches existing in-game series {SeriesId} — populating from vanilla data.", folderName, uiSeriesId);

            string seriesPlaylistId = null;
            if (_vanillaPlaylistBySeriesId != null
                && _vanillaPlaylistBySeriesId.TryGetValue(uiSeriesId, out var vanillaPlaylistId)
                && !string.IsNullOrWhiteSpace(vanillaPlaylistId))
            {
                seriesPlaylistId = vanillaPlaylistId;
            }

            var sb = new StringBuilder();
            sb.AppendLine("[series]");
            sb.AppendLine($"id = \"{EscapeTomlString(seriesId)}\"");
            sb.AppendLine($"name = \"{EscapeTomlString(seriesName)}\"");
            sb.AppendLine("existing-series = true");
            sb.AppendLine("playlist-incidence = 100");
            if (!string.IsNullOrEmpty(seriesPlaylistId))
                sb.AppendLine($"series-playlist = \"{EscapeTomlString(seriesPlaylistId)}\"");
            sb.AppendLine();

            if (games.Count == 0)
            {
                // Fallback: include at least one game block so downstream parsing succeeds.
                sb.AppendLine("[[games]]");
                sb.AppendLine($"id = \"{EscapeTomlString(seriesId)}\"");
                sb.AppendLine($"name = \"{EscapeTomlString(seriesName)}\"");
                sb.AppendLine();
            }
            else
            {
                foreach (var game in games)
                {
                    var gameId = game.NameId ?? game.UiGameTitleId;
                    var gameName = ResolveLocalizedName(game.MSBTTitle, gameId);
                    sb.AppendLine("[[games]]");
                    sb.AppendLine($"id = \"{EscapeTomlString(gameId)}\"");
                    sb.AppendLine($"name = \"{EscapeTomlString(gameName)}\"");
                    sb.AppendLine();
                }
            }

            var defaultGameId = games.Count > 0 ? (games[0].NameId ?? games[0].UiGameTitleId) : seriesId;
            AppendDefaultTrackData(sb, defaultGameId);
            return sb.ToString();
        }

        private static void AppendDefaultTrackData(StringBuilder sb, string defaultGameId)
        {
            sb.AppendLine("[default-track-data]");
            sb.AppendLine($"game = \"{EscapeTomlString(defaultGameId)}\"");
            sb.AppendLine("author = \"\"");
            sb.AppendLine("copyright = \"\"");
            sb.AppendLine("record-type = \"original\"");
            sb.AppendLine("volume = 1.0");
        }

        /// <summary>
        /// Appends a [default-track-data] section to series.toml if one isn't already present.
        /// Returns true if the section was added.
        /// </summary>
        private static bool EnsureDefaultTrackDataSection(string tomlPath)
        {
            var text = File.ReadAllText(tomlPath);
            if (Regex.IsMatch(text, @"^\s*\[default-track-data\]", RegexOptions.Multiline))
                return false;

            // Pick a sensible default game id by parsing the existing toml.
            string defaultGameId = null;
            try
            {
                var parsed = Toml.ToModel<FolderSeriesFileConfig>(text, options: new TomlModelOptions { ConvertPropertyName = ToKebabCase });
                if (parsed.Games != null && parsed.Games.Count > 0 && !string.IsNullOrWhiteSpace(parsed.Games[0].Id))
                    defaultGameId = parsed.Games[0].Id;
                else if (!string.IsNullOrWhiteSpace(parsed.Series?.Id))
                    defaultGameId = parsed.Series.Id;
            }
            catch
            {
                // Fall through to folder-name fallback below.
            }
            defaultGameId ??= Path.GetFileName(Path.GetDirectoryName(tomlPath)) ?? "";

            var sb = new StringBuilder();
            // Make sure the new section starts on its own line.
            if (text.Length > 0 && !text.EndsWith("\n"))
                sb.AppendLine();
            sb.AppendLine();
            AppendDefaultTrackData(sb, defaultGameId);

            File.AppendAllText(tomlPath, sb.ToString());
            return true;
        }

        /// <summary>
        /// Inserts `series-playlist = "..."` into the [series] block if absent. Returns true if changed.
        /// Default value: vanilla stage-playlist id for existing series, else `bgm_{series.id}`.
        /// </summary>
        private bool EnsureSeriesPlaylistField(string tomlPath)
        {
            var text = File.ReadAllText(tomlPath);

            // Already has it?
            if (Regex.IsMatch(text, @"^\s*series-playlist\s*=", RegexOptions.Multiline))
                return false;

            // Locate the [series] block.
            var seriesHeader = Regex.Match(text, @"^\s*\[series\]\s*$", RegexOptions.Multiline);
            if (!seriesHeader.Success)
                return false;

            FolderSeriesFileConfig parsed;
            try
            {
                parsed = Toml.ToModel<FolderSeriesFileConfig>(text, options: new TomlModelOptions { ConvertPropertyName = ToKebabCase });
            }
            catch
            {
                return false;
            }

            var seriesId = parsed.Series?.Id;
            if (string.IsNullOrWhiteSpace(seriesId))
                return false;

            string playlistId = null;
            if (parsed.Series.ExistingSeries)
            {
                EnsureVanillaDataLoaded();
                var uiSeriesId = MusicConstants.InternalIds.SERIES_ID_PREFIX + seriesId;
                if (_vanillaPlaylistBySeriesId != null
                    && _vanillaPlaylistBySeriesId.TryGetValue(uiSeriesId, out var vanillaPlaylistId)
                    && !string.IsNullOrWhiteSpace(vanillaPlaylistId))
                {
                    playlistId = vanillaPlaylistId;
                }
            }
            else
            {
                playlistId = "bgm_" + seriesId;
            }

            if (string.IsNullOrWhiteSpace(playlistId))
                return false;

            // Find the end of the [series] block — either the next [section] header or EOF.
            int afterHeader = seriesHeader.Index + seriesHeader.Length;
            var nextSection = Regex.Match(text.Substring(afterHeader), @"^\s*\[", RegexOptions.Multiline);
            int blockEnd = nextSection.Success ? afterHeader + nextSection.Index : text.Length;

            // Insert the new line just before blockEnd. Trim trailing whitespace so the
            // inserted line sits on its own line above the gap to the next section.
            var before = text.Substring(0, blockEnd).TrimEnd('\r', '\n', ' ', '\t');
            var after = text.Substring(blockEnd);
            var inserted = before
                + Environment.NewLine
                + $"series-playlist = \"{EscapeTomlString(playlistId)}\""
                + Environment.NewLine
                + Environment.NewLine
                + after.TrimStart('\r', '\n');

            File.WriteAllText(tomlPath, inserted);
            return true;
        }

        /// <summary>
        /// For each [[playlists]] block lacking a `songs =` line, appends `songs = "*"` at the
        /// end of the block. Returns the number of blocks modified.
        /// </summary>
        private static int EnsureSongsOnPlaylistBlocks(string tomlPath)
        {
            var text = File.ReadAllText(tomlPath);
            var headers = Regex.Matches(text, @"^\s*\[\[playlists\]\]\s*$", RegexOptions.Multiline);
            if (headers.Count == 0)
                return 0;

            // Walk blocks back-to-front so insertions don't shift earlier indices.
            int added = 0;
            for (int i = headers.Count - 1; i >= 0; i--)
            {
                var header = headers[i];
                int blockStart = header.Index + header.Length;
                int blockEnd;
                if (i + 1 < headers.Count)
                    blockEnd = headers[i + 1].Index;
                else
                {
                    var nextSection = Regex.Match(text.Substring(blockStart), @"^\s*\[", RegexOptions.Multiline);
                    blockEnd = nextSection.Success ? blockStart + nextSection.Index : text.Length;
                }

                var body = text.Substring(blockStart, blockEnd - blockStart);
                if (Regex.IsMatch(body, @"^\s*songs\s*=", RegexOptions.Multiline))
                    continue;

                // Insert just before blockEnd, after any trailing whitespace/newlines that
                // belong to the block body.
                var bodyTrimmed = body.TrimEnd('\r', '\n', ' ', '\t');
                var trailing = body.Substring(bodyTrimmed.Length);
                var insertion = Environment.NewLine + "songs = \"*\"";
                text = text.Substring(0, blockStart) + bodyTrimmed + insertion + trailing + text.Substring(blockEnd);
                added++;
            }

            if (added > 0)
                File.WriteAllText(tomlPath, text);
            return added;
        }

        // ── Vanilla data lookup ──────────────────────────────────────────────

        private void EnsureVanillaDataLoaded()
        {
            if (_vanillaLoadAttempted) return;
            _vanillaLoadAttempted = true;

            try
            {
                _audioStateService.InitBgmEntriesFromStateManager();
                _vanillaSeriesById = (_audioStateService.GetSeriesEntries() ?? Enumerable.Empty<SeriesEntry>())
                    .Where(s => s.Source == EntrySource.Core && !string.IsNullOrEmpty(s.UiSeriesId))
                    .GroupBy(s => s.UiSeriesId)
                    .ToDictionary(g => g.Key, g => g.First());
                _vanillaGamesBySeriesId = (_audioStateService.GetGameTitleEntries() ?? Enumerable.Empty<GameTitleEntry>())
                    .Where(g => g.Source == EntrySource.Core && !string.IsNullOrEmpty(g.UiSeriesId))
                    .ToLookup(g => g.UiSeriesId);
                _vanillaBgmEntries = (_audioStateService.GetBgmDbRootEntries() ?? Enumerable.Empty<BgmDbRootEntry>())
                    .Where(b => b.Source == EntrySource.Core && !string.IsNullOrEmpty(b.UiBgmId))
                    .ToList();

                var playlistsBySeriesId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var stage in _audioStateService.GetStagesEntries() ?? Enumerable.Empty<StageEntry>())
                {
                    if (string.IsNullOrEmpty(stage.UiSeriesId) || string.IsNullOrEmpty(stage.BgmSetId))
                        continue;
                    if (!playlistsBySeriesId.ContainsKey(stage.UiSeriesId))
                        playlistsBySeriesId[stage.UiSeriesId] = stage.BgmSetId;
                }
                _vanillaPlaylistBySeriesId = playlistsBySeriesId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load vanilla game data; existing-series detection disabled for this run.");
                _vanillaSeriesById = new Dictionary<string, SeriesEntry>();
                _vanillaGamesBySeriesId = Array.Empty<GameTitleEntry>().ToLookup(g => g.UiSeriesId ?? "");
                _vanillaBgmEntries = new List<BgmDbRootEntry>();
                _vanillaPlaylistBySeriesId = new Dictionary<string, string>();
            }
        }

        private static string ResolveLocalizedName(Dictionary<string, string> msbt, string fallback)
        {
            if (msbt != null)
            {
                if (msbt.TryGetValue(DefaultLocale, out var en) && !string.IsNullOrWhiteSpace(en))
                    return en;
                foreach (var kv in msbt)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Value))
                        return kv.Value;
                }
            }
            return fallback;
        }

        private static (List<Dictionary<string, string>> rows, string[] headers) ReadCsvRows(string csvPath)
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                TrimOptions = TrimOptions.Trim,
                MissingFieldFound = null,
                BadDataFound = null,
            };

            using var reader = new StreamReader(csvPath);
            using var csv = new CsvReader(reader, config);
            csv.Read();
            csv.ReadHeader();
            var headers = csv.HeaderRecord;

            var rows = new List<Dictionary<string, string>>();
            while (csv.Read())
            {
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var h in headers)
                    dict[h] = csv.GetField(h) ?? "";
                rows.Add(dict);
            }

            return (rows, headers);
        }

        private static void WriteCsvRows(string csvPath, List<Dictionary<string, string>> rows, string[] headers)
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
            };

            using var writer = new StreamWriter(csvPath);
            using var csv = new CsvWriter(writer, config);

            foreach (var h in headers)
                csv.WriteField(h);
            csv.NextRecord();

            foreach (var row in rows)
            {
                foreach (var h in headers)
                    csv.WriteField(row.GetValueOrDefault(h, ""));
                csv.NextRecord();
            }
        }

        private static string EscapeTomlString(string value)
        {
            return value?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
        }

        private static string ToKebabCase(string name)
        {
            var sb = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (char.IsUpper(c))
                {
                    if (i > 0) sb.Append('-');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
