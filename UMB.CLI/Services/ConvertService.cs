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

namespace UMB.CLI.Services
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

        // sourcePath / outputName are supplied by the desktop app for a non-interactive run.
        // When null, falls back to the interactive OldMods picker + name prompt.
        public void Run(string sourcePath = null, string outputName = null)
        {
            Script.PrintBanner(_logger);

            var modPath = _musicConfig.CurrentValue.Sma5hMusic.ModPath;

            string oldModPath;
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                oldModPath = sourcePath;
                if (!Directory.Exists(oldModPath))
                {
                    _logger.LogError("Source mod folder not found: {Path}", oldModPath);
                    return;
                }
                if (!File.Exists(Path.Combine(oldModPath, MusicConstants.MusicModFiles.MUSIC_MOD_METADATA_JSON_FILE)))
                {
                    _logger.LogError("Source folder does not contain a {File}: {Path}",
                        MusicConstants.MusicModFiles.MUSIC_MOD_METADATA_JSON_FILE, oldModPath);
                    return;
                }
            }
            else
            {
                // Look for old mods in Mods/OldMods (sibling of the MusicMods folder)
                var oldModsRoot = Path.Combine(Path.GetDirectoryName(modPath), "OldMods");

                if (!Directory.Exists(oldModsRoot))
                {
                    _logger.LogError("OldMods directory not found at {Path}. Create it and place old Sma5h mod folders inside.", oldModsRoot);
                    return;
                }

                var candidates = Directory.GetDirectories(oldModsRoot)
                    .Where(d => File.Exists(Path.Combine(d, MusicConstants.MusicModFiles.MUSIC_MOD_METADATA_JSON_FILE)))
                    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (candidates.Count == 0)
                {
                    _logger.LogError("No valid mods found in {Path}. Each subfolder must contain a {File}.",
                        oldModsRoot, MusicConstants.MusicModFiles.MUSIC_MOD_METADATA_JSON_FILE);
                    return;
                }

                var choices = candidates.Select(Path.GetFileName).ToList();
                var selected = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .WrapAround()
                        .Title("Select an old Sma5h mod to convert:")
                        .HighlightStyle(new Style(Color.Cyan1))
                        .AddChoices(choices));

                oldModPath = Path.Combine(oldModsRoot, selected);
            }

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

            var existingSeriesIds = LoadExistingSeriesIds();

            var baseGameToneIds = LoadBaseGameToneIds();

            var modName = json["name"]?.ToString() ?? Path.GetFileName(oldModPath);
            var outputModName = !string.IsNullOrWhiteSpace(outputName)
                ? SanitizeFolderName(outputName)
                : AnsiConsole.Prompt(
                    new TextPrompt<string>("Name for the new UMB mod folder:")
                        .DefaultValue(SanitizeFolderName(modName)));

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
            var customSeriesOrder = new List<string>();

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

                if (!isExisting)
                    customSeriesOrder.Add(seriesNameId);

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
                            info1Filename = info1ToneId; // placeholder — resolved below
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
                            Info1 = info1Filename,
                            InSoundtest = testDispOrder >= 0
                        });

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
                        // Reference to a base game song — store as info_ ID for direct use
                        t.Info1 = "info_" + t.Info1;
                    }
                    else
                    {
                        _logger.LogWarning("Track '{Filename}': info1 references tone_id '{ToneId}' which was not found in this series or the base game.", t.Filename, t.Info1);
                        t.Info1 = null;
                    }
                }

                WriteConvertedSeriesToml(seriesDir, seriesNameId, seriesName, isExisting, gameInfos);

                WriteConvertedTracksCsv(seriesDir, trackRows);

                totalTracks += trackRows.Count;
                totalSeries++;
                _logger.LogInformation("Converted series '{SeriesName}' ({SeriesId}): {TrackCount} tracks{Existing}",
                    seriesName, seriesNameId, trackRows.Count, isExisting ? " [existing series]" : "");
            }

            if (customSeriesOrder.Count >= 2)
            {
                var orderSb = new StringBuilder();
                orderSb.AppendLine("# Custom series display order");
                orderSb.AppendLine("# Listed series appear after official series, before \"Other\"");
                orderSb.AppendLine("# Unlisted custom series will be placed after these");
                orderSb.Append("order = [");
                foreach (var id in customSeriesOrder)
                {
                    orderSb.AppendLine();
                    orderSb.Append($"    \"{EscapeTomlString(id)}\",");
                }
                orderSb.AppendLine();
                orderSb.AppendLine("]");
                File.WriteAllText(
                    Path.Combine(outputModDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_ORDER_TOML_FILE),
                    orderSb.ToString());
                _logger.LogInformation("Wrote series-order.toml with {Count} custom series.", customSeriesOrder.Count);
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
            foreach (var line in File.ReadLines(nusBankPath).Skip(1))
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
            // For existing series the scaffold maintenance pass will fill in `series-playlist`
            // from vanilla data after conversion finishes.
            if (!isExisting)
                sb.AppendLine($"series-playlist = \"bgm_{EscapeTomlString(seriesId)}\"");
            sb.AppendLine();

            foreach (var (id, name) in games)
            {
                sb.AppendLine("[[games]]");
                sb.AppendLine($"id = \"{EscapeTomlString(id)}\"");
                sb.AppendLine($"name = \"{EscapeTomlString(name)}\"");
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

            csv.WriteField("filename");
            csv.WriteField("game");
            csv.WriteField("title");
            csv.WriteField("author");
            csv.WriteField("copyright");
            csv.WriteField("record_type");
            csv.WriteField("special_category");
            csv.WriteField("volume");
            csv.WriteField("info1");
            csv.WriteField("in_soundtest");
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
                csv.WriteField(t.SpecialCategory ?? "");
                csv.WriteField(t.Volume);
                csv.WriteField(t.Info1 ?? "");
                csv.WriteField(t.InSoundtest);
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
            public bool InSoundtest { get; set; }
        }
    }
}
