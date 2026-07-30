using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UMB.CLI.Views;
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
using Tomlyn;
using Tomlyn.Model;

namespace UMB.CLI.Services
{
    public class TrackOrderService
    {
        private const string DefaultLocale = "en_us";

        private readonly ILogger _logger;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;
        private readonly IAudioStateService _audioStateService;

        public TrackOrderService(
            IOptionsMonitor<Sma5hMusicOptions> musicConfig,
            IAudioStateService audioStateService,
            ILogger<TrackOrderService> logger)
        {
            _musicConfig = musicConfig;
            _audioStateService = audioStateService;
            _logger = logger;
        }

        public void Run()
        {
            Script.PrintBanner(_logger);

            var (modDir, seriesDir) = Script.PromptModAndSeries(_musicConfig, _logger);
            if (modDir == null || seriesDir == null)
                return;

            var csvPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
            var seriesTomlPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
            var songOrderPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_SONG_ORDER_TOML_FILE);

            if (!File.Exists(csvPath))
            {
                _logger.LogWarning("No tracks.csv found in {SeriesDir}.", seriesDir);
                return;
            }

            // dynamic columns: every value preserved
            List<Dictionary<string, string>> rows;
            string[] headers;
            try
            {
                (rows, headers) = ReadCsvRows(csvPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse {Path}.", csvPath);
                return;
            }

            if (rows.Count == 0)
            {
                _logger.LogWarning("No tracks found in {Path}.", csvPath);
                return;
            }

            // Determine if this is an existing-series mod and resolve its ui_series_id.
            string uiSeriesId = null;
            bool isExistingSeries = false;
            if (File.Exists(seriesTomlPath))
            {
                try
                {
                    var (idValue, existingValue) = ReadSeriesToml(seriesTomlPath);
                    if (!string.IsNullOrWhiteSpace(idValue))
                    {
                        uiSeriesId = MusicConstants.InternalIds.SERIES_ID_PREFIX + idValue;
                        isExistingSeries = existingValue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse series.toml in {SeriesDir}; falling back to mod-only ordering.", seriesDir);
                }
            }

            List<TrackViewModel> vanillaRows = new();
            if (isExistingSeries && uiSeriesId != null)
            {
                try
                {
                    _audioStateService.InitBgmEntriesFromStateManager();
                    vanillaRows = BuildVanillaRows(uiSeriesId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load vanilla song data for {SeriesId}; falling back to mod-only ordering.", uiSeriesId);
                    vanillaRows = new();
                }
            }

            var modRows = BuildModRows(rows);

            var viewModels = ComposeMergedList(vanillaRows, modRows, rows, headers, songOrderPath);

            List<TrackViewModel> result = null;
            try
            {
                result = AvaloniaHost.ShowWindow(
                    () => new TrackOrderWindow(viewModels)
                    {
                        Title = $"Order Tracks — {Path.GetFileName(seriesDir)}"
                    },
                    w => w.Result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to launch track order window.");
                return;
            }

            if (result == null)
            {
                _logger.LogInformation("Track ordering cancelled.");
                return;
            }

            // Persist: tracks.csv `order` column reflects each modded song's merged-list position
            if (!headers.Contains("order"))
                headers = headers.Append("order").ToArray();

            // Clear stale order values for rows that are no longer present (shouldn't happen
            // unless rows were modified outside the GUI, but be defensive).
            foreach (var row in rows)
                row["order"] = "";

            var reorderedRows = new List<Dictionary<string, string>>();
            for (int i = 0; i < result.Count; i++)
            {
                var vm = result[i];
                if (vm.IsVanilla) continue;
                if (vm.OriginalIndex < 0 || vm.OriginalIndex >= rows.Count) continue;
                var row = rows[vm.OriginalIndex];
                row["order"] = i.ToString(CultureInfo.InvariantCulture);
                reorderedRows.Add(row);
            }

            // Append any modded rows that somehow weren't present in the GUI result
            foreach (var row in rows)
            {
                if (!reorderedRows.Contains(row))
                    reorderedRows.Add(row);
            }

            WriteCsvRows(csvPath, reorderedRows, headers);
            _logger.LogInformation("Track order saved to {Path}.", csvPath);

            if (isExistingSeries && uiSeriesId != null)
            {
                WriteSongOrderToml(songOrderPath, result);
                _logger.LogInformation("Full series ordering saved to {Path}.", songOrderPath);
            }
        }

        private List<TrackViewModel> BuildVanillaRows(string uiSeriesId)
        {
            var gameTitlesInSeries = _audioStateService.GetGameTitleEntries()
                .Where(g => g.UiSeriesId == uiSeriesId)
                .ToDictionary(g => g.UiGameTitleId, g => g);
            if (gameTitlesInSeries.Count == 0)
                return new List<TrackViewModel>();

            return _audioStateService.GetBgmDbRootEntries()
                .Where(b => b.Source == EntrySource.Core
                            && b.TestDispOrder >= 0
                            && !string.IsNullOrEmpty(b.UiGameTitleId)
                            && gameTitlesInSeries.ContainsKey(b.UiGameTitleId))
                .OrderBy(b => b.TestDispOrder)
                .Select(b =>
                {
                    var title = b.Title.TryGetValue(DefaultLocale, out var t) && !string.IsNullOrEmpty(t)
                        ? t : b.UiBgmId;
                    var gameTitle = gameTitlesInSeries[b.UiGameTitleId];
                    var gameName = gameTitle.MSBTTitle.TryGetValue(DefaultLocale, out var g) && !string.IsNullOrEmpty(g)
                        ? g : gameTitle.UiGameTitleId;
                    return new TrackViewModel
                    {
                        OriginalIndex = -1,
                        Title = title,
                        Subtitle = $"[vanilla] {gameName}",
                        IsVanilla = true,
                        BgmId = b.UiBgmId,
                    };
                })
                .ToList();
        }

        private List<TrackViewModel> BuildModRows(List<Dictionary<string, string>> rows)
        {
            var result = new List<TrackViewModel>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var title = row.GetValueOrDefault("title", row.GetValueOrDefault("filename", $"Track {i}"));
                var game = row.GetValueOrDefault("game", "");
                var filename = row.GetValueOrDefault("filename", "");
                var subtitle = string.IsNullOrEmpty(game) ? filename : $"{game} — {filename}";
                var toneId = string.IsNullOrEmpty(filename) ? "" : FolderMusicMod.DeriveToneId(filename);
                var bgmId = string.IsNullOrEmpty(toneId) ? "" : MusicConstants.InternalIds.UI_BGM_ID_PREFIX + toneId;
                result.Add(new TrackViewModel
                {
                    OriginalIndex = i,
                    Title = title,
                    Subtitle = subtitle,
                    IsVanilla = false,
                    BgmId = bgmId,
                });
            }
            return result;
        }

        private List<TrackViewModel> ComposeMergedList(
            List<TrackViewModel> vanillaRows,
            List<TrackViewModel> modRows,
            List<Dictionary<string, string>> rawRows,
            string[] headers,
            string songOrderPath)
        {
            // Priority 1: existing song_order.toml — use it verbatim, append anything missing.
            if (vanillaRows.Count > 0 && File.Exists(songOrderPath))
            {
                try
                {
                    var savedOrder = ReadSongOrderToml(songOrderPath);
                    if (savedOrder.Count > 0)
                    {
                        var byBgmId = vanillaRows.Concat(modRows).ToDictionary(v => v.BgmId, v => v);
                        var seen = new HashSet<TrackViewModel>();
                        var result = new List<TrackViewModel>();
                        foreach (var id in savedOrder)
                        {
                            if (byBgmId.TryGetValue(id, out var vm) && seen.Add(vm))
                                result.Add(vm);
                        }
                        // Append anything that wasn't listed
                        foreach (var vm in vanillaRows.Concat(modRows))
                            if (seen.Add(vm))
                                result.Add(vm);
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse {Path}; falling back to default ordering.", songOrderPath);
                }
            }

            // Priority 2: existing-series with tracks.csv `order` column — sequential insertion.
            if (vanillaRows.Count > 0 && headers.Contains("order"))
            {
                var modsByOrder = modRows
                    .Select((vm, idx) => new { vm, order = ParseOrder(rawRows[idx]) })
                    .Where(x => x.order.HasValue)
                    .OrderBy(x => x.order.Value)
                    .Select(x => (x.vm, x.order.Value))
                    .ToList();
                var modsWithoutOrder = modRows
                    .Where((vm, idx) => !ParseOrder(rawRows[idx]).HasValue)
                    .ToList();

                var merged = new List<TrackViewModel>(vanillaRows);
                foreach (var (vm, order) in modsByOrder)
                {
                    var idx = Math.Clamp(order, 0, merged.Count);
                    merged.Insert(idx, vm);
                }
                merged.AddRange(modsWithoutOrder);
                return merged;
            }

            // Priority 3: existing-series with no prior ordering — vanilla first, then mods.
            if (vanillaRows.Count > 0)
            {
                var merged = new List<TrackViewModel>(vanillaRows);
                merged.AddRange(modRows);
                return merged;
            }

            // Fallback: mod-only mode (non-existing-series or vanilla data unavailable).
            if (headers.Contains("order"))
            {
                return modRows
                    .Select((vm, idx) => new { vm, order = ParseOrder(rawRows[idx]) ?? int.MaxValue })
                    .OrderBy(x => x.order)
                    .Select(x => x.vm)
                    .ToList();
            }
            return modRows;
        }

        private static int? ParseOrder(Dictionary<string, string> row)
        {
            return row.TryGetValue("order", out var val)
                   && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var o)
                ? o : (int?)null;
        }

        private static (string id, bool existingSeries) ReadSeriesToml(string tomlPath)
        {
            var text = File.ReadAllText(tomlPath);
            var table = Toml.ToModel(text);
            if (!table.TryGetValue("series", out var raw) || raw is not TomlTable series)
                return (null, false);
            string id = series.TryGetValue("id", out var idVal) ? idVal as string : null;
            bool existing = series.TryGetValue("existing-series", out var existingVal) && existingVal is bool b && b;
            return (id, existing);
        }

        private static List<string> ReadSongOrderToml(string tomlPath)
        {
            var text = File.ReadAllText(tomlPath);
            var table = Toml.ToModel(text);
            var result = new List<string>();
            if (table.TryGetValue("song_order", out var raw) && raw is TomlArray array)
            {
                foreach (var item in array)
                {
                    if (item is string s && !string.IsNullOrWhiteSpace(s))
                        result.Add(s.Trim());
                }
            }
            return result;
        }

        private static void WriteSongOrderToml(string tomlPath, List<TrackViewModel> ordered)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Generated by UltimateMusicBuilder — full ordering for an existing-series mod.");
            sb.AppendLine("# Listed in the order they will appear in the in-game Sound Test / My Music view.");
            sb.AppendLine("song_order = [");
            for (int i = 0; i < ordered.Count; i++)
            {
                var vm = ordered[i];
                if (string.IsNullOrEmpty(vm.BgmId)) continue;
                var tag = vm.IsVanilla ? "vanilla" : "mod";
                var comma = i < ordered.Count - 1 ? "," : "";
                sb.AppendLine($"  \"{vm.BgmId}\"{comma} # {tag}");
            }
            sb.AppendLine("]");
            File.WriteAllText(tomlPath, sb.ToString());
        }

        private (List<Dictionary<string, string>> rows, string[] headers) ReadCsvRows(string csvPath)
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

        private void WriteCsvRows(string csvPath, List<Dictionary<string, string>> rows, string[] headers)
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
    }
}
