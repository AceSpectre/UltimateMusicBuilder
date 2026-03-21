using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Sma5h.CLI.Services
{
    public class ConvertService
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;

        public ConvertService(IOptionsMonitor<Sma5hMusicOptions> musicConfig, ILogger<ConvertService> logger)
        {
            _musicConfig = musicConfig;
            _logger = logger;
        }

        public void Run()
        {
            Script.PrintBanner(_logger);

            var oldModPath = AnsiConsole.Prompt(
                new TextPrompt<string>("Enter path to old Sma5h mod folder:")
                    .Validate(path =>
                    {
                        if (!Directory.Exists(path))
                            return ValidationResult.Error("Directory does not exist.");
                        if (!File.Exists(Path.Combine(path, MusicConstants.MusicModFiles.MUSIC_MOD_METADATA_JSON_FILE)))
                            return ValidationResult.Error("No metadata_mod.json found in that folder.");
                        return ValidationResult.Success();
                    }));

            var jsonPath = Path.Combine(oldModPath, MusicConstants.MusicModFiles.MUSIC_MOD_METADATA_JSON_FILE);
            JObject json;
            try
            {
                json = JObject.Parse(File.ReadAllText(jsonPath));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to parse {Path}.", jsonPath);
                return;
            }

            // Load existing series IDs from ParamLabels.csv
            var existingSeriesIds = LoadExistingSeriesIds();

            // Load base game tone IDs from nusbank_ids.csv
            var baseGameToneIds = LoadBaseGameToneIds();

            // Prompt for output mod name
            var modName = json["name"]?.ToString() ?? Path.GetFileName(oldModPath);
            var outputModName = AnsiConsole.Prompt(
                new TextPrompt<string>("Name for the new UMB mod folder:")
                    .DefaultValue(SanitizeFolderName(modName)));

            var modPath = _musicConfig.CurrentValue.Sma5hMusic.ModPath;
            var outputModDir = Path.Combine(modPath, outputModName);

            if (Directory.Exists(outputModDir))
            {
                _logger.LogError("Output folder already exists: {Path}. Delete it first or choose a different name.", outputModDir);
                return;
            }

            Directory.CreateDirectory(outputModDir);

            var seriesArray = json["series"] as JArray;
            if (seriesArray == null || seriesArray.Count == 0)
            {
                _logger.LogWarning("No series found in metadata_mod.json.");
                return;
            }

            int totalTracks = 0;
            int totalSeries = 0;

            foreach (var series in seriesArray)
            {
                var seriesNameId = series["name_id"]?.ToString();
                var uiSeriesId = series["ui_series_id"]?.ToString();
                var seriesName = series["msbt_title"]?["us_en"]?.ToString() ?? seriesNameId;
                bool isExisting = existingSeriesIds.Contains(uiSeriesId);

                if (string.IsNullOrWhiteSpace(seriesNameId))
                {
                    _logger.LogWarning("Skipping series with empty name_id.");
                    continue;
                }

                var seriesDir = Path.Combine(outputModDir, seriesNameId);
                Directory.CreateDirectory(seriesDir);

                // Collect all games and tracks from this series
                var games = series["games"] as JArray;
                if (games == null || games.Count == 0)
                {
                    _logger.LogWarning("Series '{SeriesId}' has no games, skipping.", seriesNameId);
                    continue;
                }

                var gameInfos = new List<(string id, string name)>();
                var trackRows = new List<ConvertTrackRow>();

                foreach (var game in games)
                {
                    var gameNameId = game["name_id"]?.ToString();
                    var gameName = game["msbt_title"]?["us_en"]?.ToString() ?? gameNameId;

                    if (string.IsNullOrWhiteSpace(gameNameId))
                        continue;

                    if (!gameInfos.Any(g => g.id == gameNameId))
                        gameInfos.Add((gameNameId, gameName));

                    var bgms = game["bgms"] as JArray;
                    if (bgms == null) continue;

                    foreach (var bgm in bgms)
                    {
                        var filename = bgm["filename"]?.ToString();
                        if (string.IsNullOrWhiteSpace(filename))
                            continue;

                        var dbRoot = bgm["db_root"];
                        var title = dbRoot?["msbt_title"]?["us_en"]?.ToString()
                                    ?? Path.GetFileNameWithoutExtension(filename);
                        var author = dbRoot?["msbt_author"]?["us_en"]?.ToString() ?? "";
                        var copyright = dbRoot?["msbt_copyright"]?["us_en"]?.ToString() ?? "";
                        var recordType = dbRoot?["record_type"]?.ToString() ?? "record_original";
                        if (recordType.StartsWith("record_"))
                            recordType = recordType.Substring(7);
                        var volume = bgm["nus3bank_config"]?["volume"]?.Value<float>() ?? 2.7f;
                        var testDispOrder = dbRoot?["test_disp_order"]?.Value<int>() ?? 0;
                        var specialCategory = bgm["stream_set"]?["special_category"]?.ToString();

                        // Resolve info1 to a filename (strip "info_" prefix → tone_id → find matching filename)
                        var info1Raw = bgm["stream_set"]?["info1"]?.ToString();
                        string info1Filename = null;
                        if (!string.IsNullOrEmpty(info1Raw))
                        {
                            var info1ToneId = info1Raw.StartsWith("info_") ? info1Raw.Substring(5) : info1Raw;
                            // Will resolve after all tracks are collected
                            info1Filename = info1ToneId; // placeholder — resolved below
                        }

                        trackRows.Add(new ConvertTrackRow
                        {
                            Filename = filename,
                            Game = gameNameId,
                            Title = title,
                            Author = author,
                            Copyright = copyright,
                            RecordType = recordType,
                            SpecialCategory = specialCategory,
                            Volume = volume,
                            OriginalOrder = testDispOrder,
                            Info1 = info1Filename
                        });

                        // Copy audio file
                        var srcFile = Path.Combine(oldModPath, filename);
                        var destFile = Path.Combine(seriesDir, filename);
                        if (File.Exists(srcFile))
                        {
                            File.Copy(srcFile, destFile, overwrite: false);
                        }
                        else
                        {
                            _logger.LogWarning("Audio file not found: {File}", srcFile);
                        }
                    }
                }

                if (trackRows.Count == 0)
                {
                    _logger.LogWarning("Series '{SeriesId}' has no tracks, skipping.", seriesNameId);
                    continue;
                }

                // Sort tracks by original display order, then by filename as tiebreaker
                trackRows = trackRows
                    .OrderBy(t => t.OriginalOrder)
                    .ThenBy(t => t.Filename, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Resolve info1 tone_id placeholders to actual filenames
                var toneIdToFilename = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in trackRows)
                {
                    var toneId = Path.GetFileNameWithoutExtension(t.Filename).ToLowerInvariant();
                    toneIdToFilename[toneId] = t.Filename;
                }
                foreach (var t in trackRows)
                {
                    if (t.Info1 == null) continue;
                    if (toneIdToFilename.TryGetValue(t.Info1, out var resolvedFilename))
                    {
                        t.Info1 = resolvedFilename;
                    }
                    else if (baseGameToneIds.Contains(t.Info1))
                    {
                        // Reference to a base game song — store as info_ ID for direct use
                        t.Info1 = "info_" + t.Info1;
                    }
                    else
                    {
                        _logger.LogWarning("Track '{Filename}': info1 references tone_id '{ToneId}' which was not found in this series or the base game.", t.Filename, t.Info1);
                        t.Info1 = null;
                    }
                }

                // Write series.toml
                WriteConvertedSeriesToml(seriesDir, seriesNameId, seriesName, isExisting, gameInfos);

                // Write tracks.csv
                WriteConvertedTracksCsv(seriesDir, trackRows);

                totalTracks += trackRows.Count;
                totalSeries++;
                _logger.LogInformation("Converted series '{SeriesName}' ({SeriesId}): {TrackCount} tracks{Existing}",
                    seriesName, seriesNameId, trackRows.Count, isExisting ? " [existing series]" : "");
            }

            _logger.LogInformation("--------------------");
            _logger.LogInformation("Conversion complete: {SeriesCount} series, {TrackCount} tracks → {OutputDir}",
                totalSeries, totalTracks, outputModDir);
        }

        private HashSet<string> LoadExistingSeriesIds()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paramLabelsPath = Path.Combine("Resources", "ParamLabels.csv");
            if (!File.Exists(paramLabelsPath))
            {
                _logger.LogWarning("ParamLabels.csv not found at {Path}. Existing series detection disabled.", paramLabelsPath);
                return ids;
            }
            foreach (var line in File.ReadLines(paramLabelsPath))
            {
                var commaIdx = line.IndexOf(',');
                if (commaIdx < 0) continue;
                var label = line.Substring(commaIdx + 1).Trim();
                if (label.StartsWith("ui_series_") && label != "ui_series_none"
                    && label != "ui_series_all" && label != "ui_series_random"
                    && label != "ui_series_mymusic")
                {
                    ids.Add(label);
                }
            }
            return ids;
        }

        private HashSet<string> LoadBaseGameToneIds()
        {
            var toneIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nusBankPath = Path.Combine(_musicConfig.CurrentValue.ResourcesPath, MusicConstants.Resources.NUS3BANK_IDS_FILE);
            if (!File.Exists(nusBankPath))
            {
                _logger.LogWarning("nusbank_ids.csv not found at {Path}. Base game tone_id detection disabled.", nusBankPath);
                return toneIds;
            }
            foreach (var line in File.ReadLines(nusBankPath).Skip(1)) // skip header
            {
                var parts = line.Split(',');
                if (parts.Length < 2) continue;
                var name = parts[1].Trim();
                if (name.StartsWith("bgm_"))
                    toneIds.Add(name.Substring(4)); // strip "bgm_" prefix to get tone_id
            }
            return toneIds;
        }

        private void WriteConvertedSeriesToml(string seriesDir, string seriesId, string seriesName,
            bool isExisting, List<(string id, string name)> games)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[series]");
            sb.AppendLine($"id = \"{EscapeTomlString(seriesId)}\"");
            sb.AppendLine($"name = \"{EscapeTomlString(seriesName)}\"");
            if (isExisting)
                sb.AppendLine("existing-series = true");
            sb.AppendLine();

            foreach (var (id, name) in games)
            {
                sb.AppendLine("[[games]]");
                sb.AppendLine($"id = \"{EscapeTomlString(id)}\"");
                sb.AppendLine($"name = \"{EscapeTomlString(name)}\"");
                sb.AppendLine();
            }

            if (!isExisting)
            {
                sb.AppendLine("[[playlists]]");
                sb.AppendLine($"id = \"bgm_{EscapeTomlString(seriesId)}\"");
                sb.AppendLine("incidence = 100");
                sb.AppendLine();
            }

            var tomlPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
            File.WriteAllText(tomlPath, sb.ToString());
        }

        private void WriteConvertedTracksCsv(string seriesDir, List<ConvertTrackRow> tracks)
        {
            var csvPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
            using var writer = new StreamWriter(csvPath);
            using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true
            });

            // Write header
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

            // Write rows in order
            for (int i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                csv.WriteField(t.Filename);
                csv.WriteField(t.Game);
                csv.WriteField(t.Title);
                csv.WriteField(t.Author);
                csv.WriteField(t.Copyright);
                csv.WriteField(t.RecordType);
                csv.WriteField(t.SpecialCategory ?? "");
                csv.WriteField(t.Volume);
                csv.WriteField(t.Info1 ?? "");
                csv.WriteField(i);
                csv.NextRecord();
            }
        }

        private static string EscapeTomlString(string value)
        {
            return value?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
        }

        private static string SanitizeFolderName(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                if (Path.GetInvalidFileNameChars().Contains(c))
                    sb.Append('_');
                else
                    sb.Append(c);
            }
            return sb.ToString().Trim().ToLowerInvariant().Replace(' ', '-');
        }

        private class ConvertTrackRow
        {
            public string Filename { get; set; }
            public string Game { get; set; }
            public string Title { get; set; }
            public string Author { get; set; }
            public string Copyright { get; set; }
            public string RecordType { get; set; }
            public string SpecialCategory { get; set; }
            public float Volume { get; set; }
            public int OriginalOrder { get; set; }
            public string Info1 { get; set; }
        }
    }
}
