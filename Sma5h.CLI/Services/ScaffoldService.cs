using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

namespace Sma5h.CLI.Services
{
    public class ScaffoldService
    {
        private const string DefaultLocale = "en_us";

        private readonly ILogger _logger;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;
        private readonly IAudioStateService _audioStateService;

        private bool _vanillaLoadAttempted;
        private Dictionary<string, SeriesEntry> _vanillaSeriesById;
        private ILookup<string, GameTitleEntry> _vanillaGamesBySeriesId;
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
                        var csvContent = "filename,game,title,author,copyright,record_type,special_category,volume,info1\n";
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

                    // Read existing CSV rows
                    var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        HasHeaderRecord = true,
                        TrimOptions = TrimOptions.Trim,
                        MissingFieldFound = null
                    };
                    List<FolderTrackCsvRow> existingRows;
                    using (var reader = new StreamReader(csvPath))
                    using (var csv = new CsvReader(reader, csvConfig))
                    {
                        csv.Context.RegisterClassMap<FolderTrackCsvRowMap>();
                        existingRows = csv.GetRecords<FolderTrackCsvRow>().ToList();
                    }

                    var existingFilenames = new HashSet<string>(
                        existingRows.Where(r => !string.IsNullOrWhiteSpace(r.Filename)).Select(r => r.Filename),
                        StringComparer.OrdinalIgnoreCase);

                    // Find music files not already in CSV, sorted alphabetically
                    var newFiles = Directory.GetFiles(seriesDir)
                        .Where(f => validExtensions.Contains(Path.GetExtension(f)))
                        .Select(f => Path.GetFileName(f))
                        .Where(f => !existingFilenames.Contains(f))
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (newFiles.Count == 0)
                        continue;

                    // Add new rows
                    foreach (var filename in newFiles)
                    {
                        existingRows.Add(new FolderTrackCsvRow
                        {
                            Filename = filename,
                            Game = defaults?.Game ?? folderName,
                            Title = Path.GetFileNameWithoutExtension(filename),
                            Author = defaults?.Author ?? "",
                            Copyright = defaults?.Copyright ?? "",
                            RecordType = defaults?.RecordType ?? "original",
                            Volume = defaults?.Volume ?? 1.0f
                        });
                    }

                    // Rewrite CSV with all rows
                    using (var writer = new StreamWriter(csvPath))
                    using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        HasHeaderRecord = true
                    }))
                    {
                        csv.Context.RegisterClassMap<FolderTrackCsvRowMap>();
                        csv.WriteRecords(existingRows);
                    }

                    _logger.LogInformation("Added {Count} track(s) to {Path}", newFiles.Count, csvPath);
                    totalAdded += newFiles.Count;
                }
            }

            if (totalScaffolded > 0)
                _logger.LogInformation("Scaffolded {Count} series folder(s).", totalScaffolded);
            if (totalDefaultsAdded > 0)
                _logger.LogInformation("Added [default-track-data] to {Count} series.toml file(s).", totalDefaultsAdded);
            if (totalAdded > 0)
                _logger.LogInformation("Populated {Count} new track(s) total.", totalAdded);
            if (totalScaffolded == 0 && totalAdded == 0 && totalDefaultsAdded == 0)
                _logger.LogInformation("All series folders are up to date.");
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
                _vanillaSeriesById = _audioStateService.GetSeriesEntries()
                    .Where(s => s.Source == EntrySource.Core && !string.IsNullOrEmpty(s.UiSeriesId))
                    .GroupBy(s => s.UiSeriesId)
                    .ToDictionary(g => g.Key, g => g.First());
                _vanillaGamesBySeriesId = _audioStateService.GetGameTitleEntries()
                    .Where(g => g.Source == EntrySource.Core && !string.IsNullOrEmpty(g.UiSeriesId))
                    .ToLookup(g => g.UiSeriesId);

                var playlistsBySeriesId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var stage in _audioStateService.GetStagesEntries())
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
