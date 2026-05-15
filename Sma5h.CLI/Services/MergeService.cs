using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Tomlyn;

namespace Sma5h.CLI.Services
{
    public class MergeService
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;

        public MergeService(IOptionsMonitor<Sma5hMusicOptions> musicConfig, ILogger<MergeService> logger)
        {
            _musicConfig = musicConfig;
            _logger = logger;
        }

        public void Run()
        {
            Script.PrintBanner(_logger);

            var modPath = _musicConfig.CurrentValue.Sma5hMusic.ModPath;
            Directory.CreateDirectory(modPath);

            var modDirs = Directory.GetDirectories(modPath, "*", SearchOption.TopDirectoryOnly)
                .Where(d => !Path.GetFileName(d).StartsWith("."))
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (modDirs.Count < 2)
            {
                _logger.LogError("Need at least 2 mods to merge. Found {Count} in {Path}.", modDirs.Count, modPath);
                return;
            }

            // Multi-select mods
            var modNames = modDirs.Select(Path.GetFileName).ToList();
            var selectedNames = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title("Select mods to merge (space to toggle, enter to confirm):")
                    .Required()
                    .HighlightStyle(new Style(Color.Cyan1))
                    .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
                    .AddChoices(modNames));

            if (selectedNames.Count < 2)
            {
                _logger.LogError("Must select at least 2 mods to merge.");
                return;
            }

            var selectedDirs = selectedNames.Select(n => modDirs.First(d => Path.GetFileName(d) == n)).ToList();

            // Prompt for output mod name
            var outputModName = AnsiConsole.Prompt(
                new TextPrompt<string>("Name for the merged mod folder:")
                    .Validate(name =>
                    {
                        if (string.IsNullOrWhiteSpace(name))
                            return ValidationResult.Error("Name cannot be empty.");
                        if (Directory.Exists(Path.Combine(modPath, name)))
                            return ValidationResult.Error("A mod with that name already exists.");
                        return ValidationResult.Success();
                    }));

            var outputModDir = Path.Combine(modPath, outputModName);

            // Collect all series folders across selected mods
            var seriesMap = new Dictionary<string, List<(string modDir, string seriesDir)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var modDir in selectedDirs)
            {
                foreach (var seriesDir in Directory.GetDirectories(modDir))
                {
                    var folderName = Path.GetFileName(seriesDir);
                    if (folderName.StartsWith(".")) continue;
                    if (!File.Exists(Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE)))
                        continue;

                    if (!seriesMap.ContainsKey(folderName))
                        seriesMap[folderName] = new List<(string, string)>();
                    seriesMap[folderName].Add((modDir, seriesDir));
                }
            }

            if (seriesMap.Count == 0)
            {
                _logger.LogError("No series folders found in the selected mods.");
                return;
            }

            // Check for conflicts (same series in multiple mods)
            var conflicts = seriesMap.Where(kv => kv.Value.Count > 1).ToList();
            string priorityMod = null;
            if (conflicts.Count > 0)
            {
                _logger.LogInformation("Merge conflicts detected in {Count} series:", conflicts.Count);
                foreach (var conflict in conflicts)
                {
                    var mods = string.Join(", ", conflict.Value.Select(v => Path.GetFileName(v.modDir)));
                    _logger.LogInformation("  {Series}: found in {Mods}", conflict.Key, mods);
                }

                priorityMod = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Which mod should take priority for conflicts?")
                        .HighlightStyle(new Style(Color.Cyan1))
                        .AddChoices(selectedNames));
            }

            Directory.CreateDirectory(outputModDir);

            int totalSeries = 0;
            int totalTracks = 0;

            foreach (var (seriesName, sources) in seriesMap)
            {
                var outputSeriesDir = Path.Combine(outputModDir, seriesName);
                Directory.CreateDirectory(outputSeriesDir);

                if (sources.Count == 1)
                {
                    // No conflict — just copy
                    CopySeriesFolder(sources[0].seriesDir, outputSeriesDir);
                    var trackCount = CountTracksInCsv(
                        Path.Combine(outputSeriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE));
                    totalTracks += trackCount;
                    _logger.LogInformation("Copied series '{Series}' from {Mod} ({Tracks} tracks)",
                        seriesName, Path.GetFileName(sources[0].modDir), trackCount);
                }
                else
                {
                    // Conflict — merge with priority mod first
                    var orderedSources = sources
                        .OrderByDescending(s => Path.GetFileName(s.modDir) == priorityMod)
                        .Select(s => s.seriesDir)
                        .ToList();

                    var trackCount = MergeSeriesFolders(orderedSources, outputSeriesDir, seriesName);
                    totalTracks += trackCount;
                    _logger.LogInformation("Merged series '{Series}' from {Count} mods ({Tracks} tracks)",
                        seriesName, sources.Count, trackCount);
                }

                totalSeries++;
            }

            // Copy series-order.toml from priority mod if it exists, otherwise from first mod that has one
            MergeSeriesOrderToml(selectedDirs, priorityMod, outputModDir);

            _logger.LogInformation("--------------------");
            _logger.LogInformation("Merge complete: {SeriesCount} series, {TrackCount} tracks → {OutputDir}",
                totalSeries, totalTracks, outputModDir);
        }

        private void CopySeriesFolder(string sourceDir, string outputDir)
        {
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(outputDir, Path.GetFileName(file)), overwrite: false);
            }
        }

        private int MergeSeriesFolders(List<string> orderedSourceDirs, string outputDir, string seriesName)
        {
            // orderedSourceDirs[0] is the priority mod's series dir
            var tomlOptions = new TomlModelOptions { ConvertPropertyName = ToKebabCase };

            // ── Merge series.toml ──
            FolderSeriesFileConfig priorityConfig = null;
            var mergedGames = new List<(string id, string name)>();
            var mergedPlaylists = new List<(string id, int incidence, object songs)>();

            foreach (var srcDir in orderedSourceDirs)
            {
                var tomlPath = Path.Combine(srcDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
                if (!File.Exists(tomlPath)) continue;

                FolderSeriesFileConfig config;
                try
                {
                    config = Toml.ToModel<FolderSeriesFileConfig>(File.ReadAllText(tomlPath), options: tomlOptions);
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Failed to parse {Path}, skipping.", tomlPath);
                    continue;
                }

                if (priorityConfig == null)
                    priorityConfig = config;

                foreach (var game in config.Games)
                {
                    if (!mergedGames.Any(g => string.Equals(g.id, game.Id, StringComparison.OrdinalIgnoreCase)))
                        mergedGames.Add((game.Id, game.Name));
                }

                foreach (var pl in config.Playlists)
                {
                    if (!mergedPlaylists.Any(p => string.Equals(p.id, pl.Id, StringComparison.OrdinalIgnoreCase)))
                        mergedPlaylists.Add((pl.Id, pl.Incidence, pl.Songs));
                }
            }

            if (priorityConfig == null)
            {
                _logger.LogError("No valid series.toml found for series '{Series}'.", seriesName);
                return 0;
            }

            WriteMergedSeriesToml(outputDir, priorityConfig, mergedGames, mergedPlaylists);

            // ── Merge tracks.csv ──
            var allTracks = new List<MergeTrackRow>();
            var seenFilenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var srcDir in orderedSourceDirs)
            {
                var csvPath = Path.Combine(srcDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
                if (!File.Exists(csvPath)) continue;

                var tracks = ReadTracksCsv(csvPath);
                foreach (var track in tracks)
                {
                    if (seenFilenames.Add(track.Filename))
                        allTracks.Add(track);
                }
            }

            WriteMergedTracksCsv(outputDir, allTracks);

            // ── Copy audio and other files (priority first so it wins on duplicates) ──
            var copiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var srcDir in orderedSourceDirs)
            {
                foreach (var file in Directory.GetFiles(srcDir))
                {
                    var fileName = Path.GetFileName(file);
                    if (string.Equals(fileName, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (string.Equals(fileName, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (copiedFiles.Add(fileName))
                        File.Copy(file, Path.Combine(outputDir, fileName), overwrite: false);
                }
            }

            return allTracks.Count;
        }

        private List<MergeTrackRow> ReadTracksCsv(string csvPath)
        {
            var rows = new List<MergeTrackRow>();
            var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                TrimOptions = TrimOptions.Trim,
                MissingFieldFound = null
            };

            using var reader = new StreamReader(csvPath);
            using var csv = new CsvReader(reader, csvConfig);

            csv.Read();
            csv.ReadHeader();
            var headers = csv.HeaderRecord;

            while (csv.Read())
            {
                var row = new MergeTrackRow
                {
                    Filename = csv.GetField("filename") ?? "",
                    Game = csv.GetField("game") ?? "",
                    Title = csv.GetField("title") ?? "",
                    Author = GetOptionalField(csv, headers, "author"),
                    Copyright = GetOptionalField(csv, headers, "copyright"),
                    RecordType = GetOptionalField(csv, headers, "record_type", "original"),
                    SpecialCategory = GetOptionalField(csv, headers, "special_category"),
                    Volume = float.TryParse(GetOptionalField(csv, headers, "volume", "1.0"),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 1.0f,
                    Info1 = GetOptionalField(csv, headers, "info1")
                };

                if (!string.IsNullOrWhiteSpace(row.Filename))
                    rows.Add(row);
            }

            return rows;
        }

        private static string GetOptionalField(CsvReader csv, string[] headers, string name, string defaultValue = "")
        {
            if (headers.Contains(name, StringComparer.OrdinalIgnoreCase))
                return csv.GetField(name) ?? defaultValue;
            return defaultValue;
        }

        private void WriteMergedSeriesToml(string outputDir, FolderSeriesFileConfig priorityConfig,
            List<(string id, string name)> games, List<(string id, int incidence, object songs)> playlists)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[series]");
            sb.AppendLine($"id = \"{EscapeToml(priorityConfig.Series.Id)}\"");
            sb.AppendLine($"name = \"{EscapeToml(priorityConfig.Series.Name)}\"");
            if (priorityConfig.Series.ExistingSeries)
                sb.AppendLine("existing-series = true");
            if (priorityConfig.Series.PlaylistIncidence != 100)
                sb.AppendLine($"playlist-incidence = {priorityConfig.Series.PlaylistIncidence}");
            if (!string.IsNullOrWhiteSpace(priorityConfig.Series.SeriesPlaylist))
                sb.AppendLine($"series-playlist = \"{EscapeToml(priorityConfig.Series.SeriesPlaylist)}\"");
            sb.AppendLine();

            foreach (var (id, name) in games)
            {
                sb.AppendLine("[[games]]");
                sb.AppendLine($"id = \"{EscapeToml(id)}\"");
                sb.AppendLine($"name = \"{EscapeToml(name)}\"");
                sb.AppendLine();
            }

            foreach (var (id, incidence, songs) in playlists)
            {
                sb.AppendLine("[[playlists]]");
                sb.AppendLine($"id = \"{EscapeToml(id)}\"");
                sb.AppendLine($"incidence = {incidence}");
                AppendSongsField(sb, songs);
                sb.AppendLine();
            }

            if (priorityConfig.DefaultTrackData != null)
            {
                var d = priorityConfig.DefaultTrackData;
                sb.AppendLine("[default-track-data]");
                if (!string.IsNullOrEmpty(d.Game))
                    sb.AppendLine($"game = \"{EscapeToml(d.Game)}\"");
                if (!string.IsNullOrEmpty(d.Author))
                    sb.AppendLine($"author = \"{EscapeToml(d.Author)}\"");
                if (!string.IsNullOrEmpty(d.Copyright))
                    sb.AppendLine($"copyright = \"{EscapeToml(d.Copyright)}\"");
                sb.AppendLine($"record-type = \"{EscapeToml(d.RecordType ?? "original")}\"");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "volume = {0}", d.Volume));
                sb.AppendLine();
            }

            File.WriteAllText(
                Path.Combine(outputDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE),
                sb.ToString());
        }

        private static void AppendSongsField(StringBuilder sb, object songs)
        {
            if (songs == null || FolderMusicMod.IsWildcardSongs(songs))
            {
                sb.AppendLine("songs = \"*\"");
                return;
            }

            var explicitSongs = FolderMusicMod.ExplicitSongs(songs);
            if (explicitSongs.Count == 0)
            {
                sb.AppendLine("songs = \"*\"");
                return;
            }

            sb.AppendLine("songs = [");
            for (int i = 0; i < explicitSongs.Count; i++)
            {
                var comma = i < explicitSongs.Count - 1 ? "," : "";
                sb.AppendLine($"    \"{EscapeToml(explicitSongs[i])}\"{comma}");
            }
            sb.AppendLine("]");
        }

        private void WriteMergedTracksCsv(string outputDir, List<MergeTrackRow> tracks)
        {
            var csvPath = Path.Combine(outputDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
            using var writer = new StreamWriter(csvPath);
            using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true
            });

            csv.WriteField("filename");
            csv.WriteField("game");
            csv.WriteField("title");
            csv.WriteField("author");
            csv.WriteField("copyright");
            csv.WriteField("record_type");
            csv.WriteField("special_category");
            csv.WriteField("volume");
            csv.WriteField("info1");
            csv.WriteField("order");
            csv.NextRecord();

            for (int i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                csv.WriteField(t.Filename);
                csv.WriteField(t.Game);
                csv.WriteField(t.Title);
                csv.WriteField(t.Author);
                csv.WriteField(t.Copyright);
                csv.WriteField(t.RecordType);
                csv.WriteField(t.SpecialCategory);
                csv.WriteField(t.Volume);
                csv.WriteField(t.Info1);
                csv.WriteField(i);
                csv.NextRecord();
            }
        }

        private void MergeSeriesOrderToml(List<string> selectedDirs, string priorityMod, string outputDir)
        {
            var orderedDirs = priorityMod != null
                ? selectedDirs.OrderByDescending(d => Path.GetFileName(d) == priorityMod).ToList()
                : selectedDirs;

            var mergedOrder = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var modDir in orderedDirs)
            {
                var orderFile = Path.Combine(modDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_ORDER_TOML_FILE);
                if (!File.Exists(orderFile)) continue;

                try
                {
                    var model = Toml.ToModel(File.ReadAllText(orderFile));
                    if (model.TryGetValue("order", out var val) && val is Tomlyn.Model.TomlArray arr)
                    {
                        foreach (var item in arr.OfType<string>())
                        {
                            if (seen.Add(item))
                                mergedOrder.Add(item);
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Failed to parse {Path}, skipping.", orderFile);
                }
            }

            if (mergedOrder.Count == 0)
                return;

            var sb = new StringBuilder();
            sb.AppendLine("# Custom series display order");
            sb.AppendLine("# Listed series appear after official series, before \"Other\"");
            sb.AppendLine("# Unlisted custom series will be placed after these");
            sb.Append("order = [");
            foreach (var id in mergedOrder)
            {
                sb.AppendLine();
                sb.Append($"    \"{EscapeToml(id)}\",");
            }
            sb.AppendLine();
            sb.AppendLine("]");

            File.WriteAllText(
                Path.Combine(outputDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_ORDER_TOML_FILE),
                sb.ToString());
            _logger.LogInformation("Merged series-order.toml with {Count} series from {ModCount} mod(s).",
                mergedOrder.Count, orderedDirs.Count(d => File.Exists(
                    Path.Combine(d, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_ORDER_TOML_FILE))));
        }

        private int CountTracksInCsv(string csvPath)
        {
            if (!File.Exists(csvPath)) return 0;
            return Math.Max(0, File.ReadLines(csvPath).Count() - 1);
        }

        private static string EscapeToml(string value)
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

        private class MergeTrackRow
        {
            public string Filename { get; set; }
            public string Game { get; set; }
            public string Title { get; set; }
            public string Author { get; set; }
            public string Copyright { get; set; }
            public string RecordType { get; set; }
            public string SpecialCategory { get; set; }
            public float Volume { get; set; }
            public string Info1 { get; set; }
        }
    }
}
