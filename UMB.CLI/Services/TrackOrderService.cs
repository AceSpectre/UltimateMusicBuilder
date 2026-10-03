using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UMB.CLI.Desktop;
using UMB.CLI.Views;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using Sma5h.Mods.Music.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace UMB.CLI.Services
{
    /// <summary>
    /// Track order and per-track fields for a series (tracks.csv `order` column, and
    /// song_order.toml for existing-series mods). Shared by the desktop app and the CLI's
    /// track order window.
    /// </summary>
    public class TrackOrderService
    {
        // tracks.csv columns the desktop edits, and the field each one is written from.
        private static readonly (string Column, Func<TrackFields, string> Value)[] EditableColumns =
        {
            ("title", f => f.Title), ("game", f => f.Game), ("author", f => f.Author), ("copyright", f => f.Copyright),
            ("record_type", f => f.RecordType), ("special_category", f => f.SpecialCategory), ("info1", f => f.Info1),
            ("in_soundtest", f => f.InSoundtest), ("volume", f => f.Volume?.ToString("0.###", CultureInfo.InvariantCulture))
        };

        private readonly ILogger _logger;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;
        private readonly VanillaCatalogService _catalog;

        public TrackOrderService(
            IOptionsMonitor<Sma5hMusicOptions> musicConfig,
            VanillaCatalogService catalog,
            ILogger<TrackOrderService> logger)
        {
            _musicConfig = musicConfig;
            _catalog = catalog;
            _logger = logger;
        }

        public void Run()
        {
            Script.PrintBanner(_logger);

            var (modDir, seriesDir) = Script.PromptModAndSeries(_musicConfig, _logger);
            if (modDir == null || seriesDir == null)
                return;

            if (!File.Exists(Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE)))
            {
                _logger.LogWarning("No tracks.csv found in {SeriesDir}.", seriesDir);
                return;
            }

            var data = Load(seriesDir);
            if (data.Items.Count == 0)
            {
                _logger.LogWarning("No tracks found in {SeriesDir}.", seriesDir);
                return;
            }

            var itemIdByViewModel = new Dictionary<TrackViewModel, string>();
            var viewModels = data.Items.Select(item =>
            {
                var vm = new TrackViewModel
                {
                    OriginalIndex = item.OriginalIndex ?? -1,
                    Title = item.Title,
                    Subtitle = item.Subtitle,
                    IsVanilla = item.OriginalIndex == null,
                    BgmId = item.BgmId,
                };
                itemIdByViewModel[vm] = item.Id;
                return vm;
            }).ToList();

            List<TrackViewModel> result;
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

            Save(seriesDir, result.Select(vm => new SaveTrackItem(itemIdByViewModel[vm], null)).ToList());
            _logger.LogInformation("Track order saved for {SeriesDir}.", seriesDir);
        }

        public TrackOrderData Load(string seriesPath) =>
            LoadUnchecked(ModPaths.ResolveUnderMods(_musicConfig, seriesPath, "Invalid series path.")).data;

        /// <summary>
        /// Saves the order (listed items first, then any left out) and edited fields to
        /// tracks.csv, declares newly used vanilla games in series.toml, and rewrites
        /// song_order.toml for existing-series mods.
        /// </summary>
        public TrackOrderData Save(string seriesPath, List<SaveTrackItem> items)
        {
            var resolvedSeriesPath = ModPaths.ResolveUnderMods(_musicConfig, seriesPath, "Invalid series path.");
            var (data, rows, headers) = LoadUnchecked(resolvedSeriesPath);
            var files = SeriesFiles.Of(resolvedSeriesPath);

            var orderedIds = items.Select(i => i.Id).Distinct().ToList();
            var itemById = data.Items.ToDictionary(i => i.Id);
            var finalItems = orderedIds.Where(itemById.ContainsKey).Select(id => itemById[id])
                .Concat(data.Items.Where(i => !orderedIds.Contains(i.Id)))
                .ToList();

            var fieldsById = items.Where(i => i.Fields != null).ToDictionary(i => i.Id, i => i.Fields);
            foreach (var fields in fieldsById.Values)
                if (fields.Volume is float volume && (!float.IsFinite(volume) || volume < 0))
                    throw new DesktopApiException("Volume must be a finite non-negative multiplier.");
            SaveVanillaFields(resolvedSeriesPath, finalItems, fieldsById);
            foreach (var item in finalItems)
            {
                if (item.OriginalIndex is not int index || !fieldsById.TryGetValue(item.Id, out var fields)) continue;
                foreach (var (column, value) in EditableColumns)
                    if (column != "volume" || fields.Volume != null) rows[index][column] = value(fields) ?? "";
            }

            var nextHeaders = headers.ToList();
            foreach (var column in EditableColumns.Select(c => c.Column).Append("order"))
            {
                if (!nextHeaders.Contains(column)) nextHeaders.Add(column);
            }

            foreach (var row in rows)
                row["order"] = "";
            var reorderedRows = new List<CsvRow>();
            for (var position = 0; position < finalItems.Count; position++)
            {
                if (finalItems[position].OriginalIndex is not int index) continue;
                rows[index]["order"] = position.ToString(CultureInfo.InvariantCulture);
                reorderedRows.Add(rows[index]);
            }
            reorderedRows.AddRange(rows.Where(r => !reorderedRows.Contains(r)));

            TracksCsv.Write(files.Csv, reorderedRows, nextHeaders);

            // The build only accepts games declared under [[games]]; declare any newly used vanilla ones.
            EnsureSeriesGames(files.SeriesToml, rows, data.Games);

            if (data.IsExistingSeries)
            {
                var ids = finalItems.Where(i => !string.IsNullOrEmpty(i.BgmId)).ToList();
                var lines = new List<string>
                {
                    "# Generated by UltimateMusicBuilder — ordering for an existing-series mod.",
                    "# Listed in the order they will appear in the in-game Sound Test / My Music view.",
                    "song_order = ["
                };
                lines.AddRange(ids.Select((item, i) => $"  \"{item.BgmId}\"{(i < ids.Count - 1 ? "," : "")} # {(item.OriginalIndex == null ? "vanilla" : "mod")}"));
                lines.Add("]");
                File.WriteAllText(files.SongOrder, string.Join("\n", lines) + "\n");
            }

            return Load(resolvedSeriesPath);
        }

        private record SeriesFiles(string Csv, string SeriesToml, string SongOrder)
        {
            public static SeriesFiles Of(string seriesPath) => new(
                Path.Combine(seriesPath, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE),
                Path.Combine(seriesPath, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE),
                Path.Combine(seriesPath, MusicConstants.MusicModFiles.FOLDER_MOD_SONG_ORDER_TOML_FILE));
        }

        private (TrackOrderData data, List<CsvRow> rows, string[] headers) LoadUnchecked(string seriesPath)
        {
            var files = SeriesFiles.Of(seriesPath);
            var (rows, headers) = TracksCsv.Read(files.Csv);
            var series = ReadSeriesToml(files.SeriesToml);
            var songOrder = SeriesToml.ReadIdList(files.SongOrder);
            var uiSeriesId = series.Id != null ? MusicConstants.InternalIds.SERIES_ID_PREFIX + series.Id : null;

            var catalog = _catalog.Get();
            var vanillaGames = catalog?.GameTitles.Where(g => g.SeriesId == uiSeriesId).Select(g => new SeriesGame(g.Id, g.Name)).ToList()
                ?? new List<SeriesGame>();
            var vanillaSongs = catalog?.Songs.Where(s => s.SeriesId == uiSeriesId).Select(s => new VanillaSongOption(s.InfoId, s.Name)).ToList()
                ?? new List<VanillaSongOption>();
            string ResolveVanillaTitle(string bgmId) =>
                catalog != null && catalog.BgmTitles.TryGetValue(bgmId, out var title) ? title : FormatVanillaTitle(bgmId);

            // Custom (series.toml) games first, then vanilla series games not already declared.
            var declared = series.Games.Select(g => g.Id).ToHashSet();
            var games = series.Games.Concat(vanillaGames.Where(g => !declared.Contains(g.Id))).ToList();

            var overrides = VanillaSongOverride.Read(seriesPath).ToDictionary(o => o.BgmId);
            var vanillaItems = series.ExistingSeries ? (catalog?.Songs ?? new()).Where(s => s.SeriesId == uiSeriesId)
                .Select(s => BuildVanillaItem(s, overrides.GetValueOrDefault(s.BgmId))).ToList() : new();
            var items = OrderItems(BuildModItems(rows), rows, songOrder, ResolveVanillaTitle, vanillaItems);
            var references = items.Select(i => i.Fields?.Info1).Where(id => !string.IsNullOrEmpty(id)).ToHashSet();
            items = items.Select(i => i with { IsPinchTarget = references.Contains(i.OriginalIndex == null ? i.InfoId : i.Filename) }).ToList();
            var data = new TrackOrderData(Path.GetFileName(seriesPath), seriesPath, series.ExistingSeries, songOrder.Count > 0,
                games, vanillaSongs, series.DefaultTrackData, items);
            return (data, rows, headers);
        }

        private record SeriesInfo(string Id, bool ExistingSeries, List<SeriesGame> Games, DefaultTrackData DefaultTrackData);

        private static SeriesInfo ReadSeriesToml(string path)
        {
            if (!File.Exists(path))
                return new SeriesInfo(null, false, new List<SeriesGame>(), null);

            var text = File.ReadAllText(path);
            var header = SeriesToml.ReadHeader(text);
            var defaults = SeriesToml.ReadDefaults(text) is { } d
                ? new DefaultTrackData { Game = d.Game, Author = d.Author, Copyright = d.Copyright, RecordType = d.RecordType }
                : null;
            return new SeriesInfo(header.Id, header.ExistingSeries, SeriesToml.Games(text), defaults);
        }

        /// <summary>Appends [[games]] blocks for known game ids used by rows but not yet declared.</summary>
        private static void EnsureSeriesGames(string seriesTomlPath, List<CsvRow> rows, List<SeriesGame> knownGames)
        {
            if (!File.Exists(seriesTomlPath)) return;

            var text = File.ReadAllText(seriesTomlPath);
            var declared = SeriesToml.Games(text).Select(g => g.Id).ToHashSet();
            var nameById = knownGames.GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First().Name);
            var toAdd = rows.Select(r => r.Get("game").Trim()).Where(id => id.Length > 0).Distinct()
                .Where(id => !declared.Contains(id) && nameById.ContainsKey(id))
                .ToList();
            if (toAdd.Count == 0) return;

            var blocks = string.Concat(toAdd.Select(id =>
                $"\n[[games]]\nid = \"{SeriesToml.Escape(id)}\"\nname = \"{SeriesToml.Escape(nameById[id])}\"\n"));
            File.WriteAllText(seriesTomlPath, $"{text.TrimEnd()}\n{blocks}");
        }

        private static List<TrackOrderItem> BuildModItems(List<CsvRow> rows)
        {
            // A track is a "pinch target" when another row's info1 references its filename.
            var referencedFilenames = rows.Select(r => r.Get("info1").Trim())
                .Where(info1 => info1.Length > 0 && !info1.StartsWith("info_"))
                .ToHashSet();

            return rows.Select((row, index) =>
            {
                var filename = row.Get("filename");
                var title = CliUtil.FirstNonEmpty(row.Get("title"), filename, $"Track {index + 1}");
                var game = row.Get("game");
                var bgmId = filename.Length > 0
                    ? MusicConstants.InternalIds.UI_BGM_ID_PREFIX + FolderMusicMod.DeriveToneId(filename)
                    : $"{MusicConstants.InternalIds.UI_BGM_ID_PREFIX}track_{index}";
                return new TrackOrderItem($"mod:{index}", title, game.Length > 0 ? $"{game} - {filename}" : filename,
                    bgmId, filename, false, index, BuildFields(row), filename.Length > 0 && referencedFilenames.Contains(filename),
                    MusicConstants.InternalIds.INFO_ID_PREFIX + FolderMusicMod.DeriveToneId(filename));
            }).ToList();
        }

        private static TrackFields BuildFields(CsvRow row) => new()
        {
            Volume = string.IsNullOrWhiteSpace(row.Get("volume")) ? null : VolumeConfigService.ParseVolume(row.Get("volume")),
            Title = row.Get("title"),
            Game = row.Get("game"),
            Author = row.Get("author"),
            Copyright = row.Get("copyright"),
            RecordType = CliUtil.FirstNonEmpty(row.Get("record_type"), "original"),
            SpecialCategory = row.Get("special_category"),
            Info1 = row.Get("info1"),
            InSoundtest = CliUtil.FirstNonEmpty(row.Get("in_soundtest"), "True")
        };

        /// <summary>
        /// song_order.toml wins when present. Unknown ids are preserved, and unlisted songs
        /// are appended; otherwise mod tracks use the CSV order followed by vanilla songs.
        /// </summary>
        private static List<TrackOrderItem> OrderItems(List<TrackOrderItem> modItems, List<CsvRow> rows,
            List<string> songOrder, Func<string, string> resolveVanillaTitle, List<TrackOrderItem> vanillaItems)
        {
            if (songOrder.Count == 0)
            {
                return modItems
                    .OrderBy(item => ParseOrder(rows[item.OriginalIndex ?? 0]) ?? int.MaxValue)
                    .Concat(vanillaItems).ToList();
            }

            var modByBgmId = modItems.GroupBy(i => i.BgmId).ToDictionary(g => g.Key, g => g.Last());
            var seen = new HashSet<string>();
            var result = new List<TrackOrderItem>();
            var vanillaByBgmId = vanillaItems.ToDictionary(i => i.BgmId);
            var vanillaIndex = 0;
            foreach (var bgmId in songOrder.Distinct())
            {
                if (modByBgmId.TryGetValue(bgmId, out var modItem))
                {
                    seen.Add(modItem.Id);
                    result.Add(modItem);
                }
                else if (vanillaByBgmId.TryGetValue(bgmId, out var vanillaItem))
                {
                    seen.Add(vanillaItem.Id);
                    result.Add(vanillaItem);
                }
                else
                {
                    result.Add(new TrackOrderItem($"vanilla:{vanillaIndex++}:{bgmId}", resolveVanillaTitle(bgmId),
                        "[vanilla] preserved from song_order.toml", bgmId, "", true, null, null, false));
                }
            }
            result.AddRange(modItems.Concat(vanillaItems).Where(i => !seen.Contains(i.Id)));
            return result;
        }

        private static TrackFields VanillaFields(VanillaCatalogService.Song song, VanillaSongOverride saved = null) => new()
        {
            Title = saved?.Title ?? song.Name, Author = saved?.Author ?? song.Author,
            Copyright = saved?.Copyright ?? song.Copyright, Volume = saved?.Volume ?? 1,
            Game = song.Game, RecordType = song.RecordType, InSoundtest = song.InSoundtest.ToString(),
            Info1 = saved?.Info1 ?? song.Info1, SpecialCategory = saved?.SpecialCategory ?? song.SpecialCategory
        };

        private static TrackOrderItem BuildVanillaItem(VanillaCatalogService.Song song, VanillaSongOverride saved)
        {
            var fields = VanillaFields(song, saved);
            return new TrackOrderItem("vanilla:" + song.BgmId, fields.Title, "[vanilla] " + song.BgmId,
                song.BgmId, "", false, null, fields, false, song.InfoId);
        }

        private void SaveVanillaFields(string seriesPath, List<TrackOrderItem> items, Dictionary<string, TrackFields> fieldsById)
        {
            var overrides = VanillaSongOverride.Read(seriesPath).ToDictionary(o => o.BgmId);
            var catalog = _catalog.Get();
            foreach (var item in items.Where(i => i.OriginalIndex == null && i.Fields != null))
            {
                if (!fieldsById.TryGetValue(item.Id, out var fields)) continue;
                var song = catalog?.Songs.SingleOrDefault(s => s.BgmId == item.BgmId);
                if (song == null) continue;
                overrides[item.BgmId] = new VanillaSongOverride
                {
                    BgmId = item.BgmId,
                    Title = fields.Title == song.Name ? null : fields.Title,
                    Author = fields.Author == song.Author ? null : fields.Author,
                    Copyright = fields.Copyright == song.Copyright ? null : fields.Copyright,
                    Volume = fields.Volume == 1 && overrides.GetValueOrDefault(item.BgmId)?.Volume == null ? null : fields.Volume,
                    Info1 = fields.Info1 == song.Info1 ? null : fields.Info1,
                    SpecialCategory = fields.SpecialCategory == song.SpecialCategory ? null : fields.SpecialCategory
                };
            }
            if (overrides.Count > 0 || File.Exists(Path.Combine(seriesPath, VanillaSongOverride.FileName)))
                VanillaSongOverride.Write(seriesPath, overrides.Values);
        }

        internal static int? ParseOrder(Dictionary<string, string> row) =>
            row.TryGetValue("order", out var value)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var order)
                ? order : null;

        private static string FormatVanillaTitle(string bgmId) =>
            string.Join(" ", Regex.Replace(bgmId, "^ui_bgm_", "")
                .Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
}
