using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UMB.CLI.Desktop;
using UMB.CLI.Views;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Tomlyn;

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
            ("in_soundtest", f => f.InSoundtest)
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
                    IsVanilla = item.IsLocked,
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

            var orderedIds = items.Select(i => i.Id).ToList();
            var itemById = data.Items.ToDictionary(i => i.Id);
            var finalItems = orderedIds.Where(itemById.ContainsKey).Select(id => itemById[id])
                .Concat(data.Items.Where(i => !orderedIds.Contains(i.Id)))
                .ToList();

            var fieldsById = items.Where(i => i.Fields != null).ToDictionary(i => i.Id, i => i.Fields);
            foreach (var item in finalItems)
            {
                if (item.OriginalIndex is not int index || !fieldsById.TryGetValue(item.Id, out var fields)) continue;
                foreach (var (column, value) in EditableColumns)
                    rows[index][column] = value(fields) ?? "";
                if (fields.Volume is float volume)
                {
                    if (!float.IsFinite(volume) || volume < 0) throw new DesktopApiException("Volume must be a finite, non-negative number.");
                    rows[index]["volume"] = volume.ToString(CultureInfo.InvariantCulture);
                }
            }

            var nextHeaders = headers.ToList();
            foreach (var column in EditableColumns.Select(c => c.Column).Append("order"))
            {
                if (!nextHeaders.Contains(column)) nextHeaders.Add(column);
            }

            if (items.Any(i => i.Fields?.Volume != null) && !nextHeaders.Contains("volume")) nextHeaders.Add("volume");

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
                lines.AddRange(ids.Select((item, i) => $"  \"{item.BgmId}\"{(i < ids.Count - 1 ? "," : "")} # {(item.IsLocked ? "vanilla" : "mod")}"));
                lines.Add("]");
                File.WriteAllText(files.SongOrder, string.Join("\n", lines) + "\n");
            }

            return Load(resolvedSeriesPath);
        }

        public TrackOrderData SavePresets(string seriesPath, DefaultTrackData seriesDefaults, List<DefaultTrackData> presets)
        {
            var resolved = ModPaths.ResolveUnderMods(_musicConfig, seriesPath, "Invalid series path.");
            var data = Load(resolved);
            if (seriesDefaults == null || presets == null) throw new DesktopApiException("Song defaults are required.");
            foreach (var defaults in presets.Append(seriesDefaults))
            {
                if (!float.IsFinite(defaults.Volume) || defaults.Volume < 0)
                    throw new DesktopApiException("Volume must be a finite, non-negative number.");
                if (defaults.RecordType is not ("original" or "arrange" or "new_arrange"))
                    throw new DesktopApiException("Unknown record type.");
                if (!string.IsNullOrEmpty(defaults.Game) && !data.Games.Any(g => g.Id == defaults.Game))
                    throw new DesktopApiException("Select a game from this series.");
            }
            if (presets.Any(p => string.IsNullOrEmpty(p.Game)) || presets.Select(p => p.Game).Distinct().Count() != presets.Count)
                throw new DesktopApiException("Each game can have one song preset.");

            var path = SeriesFiles.Of(resolved).SeriesToml;
            if (!File.Exists(path)) throw new DesktopApiException("Scaffold this series before editing song presets.");
            var lines = SeriesToml.UpsertTable(SeriesToml.ReadLines(path), "default-track-data",
                DefaultEntries(seriesDefaults), createIfMissing: true);
            var (kept, insertIndex) = SeriesToml.StripArrayTables(lines, "song-presets");
            var blocks = presets.SelectMany(p => new[] { "", "[[song-presets]]" }
                .Concat(DefaultEntries(p).Select(e => e.Line))).ToList();
            if (insertIndex < 0) insertIndex = kept.Count;
            lines = kept.Take(insertIndex).Concat(blocks).Concat(kept.Skip(insertIndex)).ToList();
            SeriesToml.WriteLines(path, lines);
            return Load(resolved);
        }

        public TrackFields ApplyPreset(string seriesPath, TrackFields fields, string game, bool useSeriesDefaults)
        {
            var resolved = ModPaths.ResolveUnderMods(_musicConfig, seriesPath, "Invalid series path.");
            if (fields == null) throw new DesktopApiException("Select an editable song.");
            var path = SeriesFiles.Of(resolved).SeriesToml;
            if (!File.Exists(path)) throw new DesktopApiException("Scaffold this series before applying song presets.");
            var config = ReadPresetConfig(File.ReadAllText(path));
            var defaults = config.ResolveTrackDefaults(useSeriesDefaults || string.IsNullOrEmpty(game) ? null : game, useSeriesDefaults);
            fields.Game = defaults.Game;
            fields.Author = defaults.Author;
            fields.Copyright = defaults.Copyright;
            fields.RecordType = defaults.RecordType;
            fields.Volume = defaults.Volume;
            return fields;
        }

        private static FolderSeriesFileConfig ReadPresetConfig(string text)
        {
            var options = CliUtil.KebabTomlOptions();
            options.IgnoreMissingProperties = true;
            return Toml.ToModel<FolderSeriesFileConfig>(text, options: options);
        }

        private static DefaultTrackData ToDefaults(FolderDefaultTrackDataConfig d) => new()
        {
            Game = d.Game ?? "", Author = d.Author ?? "", Copyright = d.Copyright ?? "",
            RecordType = d.RecordType, Volume = d.Volume
        };

        private static SeriesToml.Entry[] DefaultEntries(DefaultTrackData d) => new[]
        {
            new SeriesToml.Entry("game", $"game = \"{SeriesToml.Escape(d.Game)}\""),
            new SeriesToml.Entry("author", $"author = \"{SeriesToml.Escape(d.Author)}\""),
            new SeriesToml.Entry("copyright", $"copyright = \"{SeriesToml.Escape(d.Copyright)}\""),
            new SeriesToml.Entry("record-type", $"record-type = \"{SeriesToml.Escape(d.RecordType)}\""),
            new SeriesToml.Entry("volume", $"volume = {d.Volume.ToString(CultureInfo.InvariantCulture)}")
        };

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

            var items = OrderItems(BuildModItems(rows), rows, songOrder, ResolveVanillaTitle);
            var data = new TrackOrderData(Path.GetFileName(seriesPath), seriesPath, series.ExistingSeries, songOrder.Count > 0,
                games, vanillaSongs, series.DefaultTrackData, items, series.SongPresets);
            return (data, rows, headers);
        }

        private record SeriesInfo(string Id, bool ExistingSeries, List<SeriesGame> Games, DefaultTrackData DefaultTrackData, List<DefaultTrackData> SongPresets);

        private static SeriesInfo ReadSeriesToml(string path)
        {
            if (!File.Exists(path))
                return new SeriesInfo(null, false, new List<SeriesGame>(), null, new());

            var text = File.ReadAllText(path);
            var header = SeriesToml.ReadHeader(text);
            var config = ReadPresetConfig(text);
            return new SeriesInfo(header.Id, header.ExistingSeries, SeriesToml.Games(text),
                config.DefaultTrackData == null ? null : ToDefaults(config.DefaultTrackData),
                config.SongPresets.Select(ToDefaults).ToList());
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
                    bgmId, filename, false, index, BuildFields(row), filename.Length > 0 && referencedFilenames.Contains(filename));
            }).ToList();
        }

        private static TrackFields BuildFields(CsvRow row) => new()
        {
            Volume = float.TryParse(row.Get("volume"), NumberStyles.Float, CultureInfo.InvariantCulture, out var volume) ? volume : null,
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
        /// song_order.toml wins when present (unknown ids become locked vanilla items, unlisted
        /// mod tracks are appended); otherwise mod tracks sorted by their `order` column.
        /// </summary>
        private static List<TrackOrderItem> OrderItems(List<TrackOrderItem> modItems, List<CsvRow> rows,
            List<string> songOrder, Func<string, string> resolveVanillaTitle)
        {
            if (songOrder.Count == 0)
            {
                return modItems
                    .OrderBy(item => ParseOrder(rows[item.OriginalIndex ?? 0]) ?? int.MaxValue)
                    .ToList();
            }

            var modByBgmId = modItems.GroupBy(i => i.BgmId).ToDictionary(g => g.Key, g => g.Last());
            var seen = new HashSet<string>();
            var result = new List<TrackOrderItem>();
            var vanillaIndex = 0;
            foreach (var bgmId in songOrder)
            {
                if (modByBgmId.TryGetValue(bgmId, out var modItem))
                {
                    seen.Add(modItem.Id);
                    result.Add(modItem);
                }
                else
                {
                    result.Add(new TrackOrderItem($"vanilla:{vanillaIndex++}:{bgmId}", resolveVanillaTitle(bgmId),
                        "[vanilla] preserved from song_order.toml", bgmId, "", true, null, null, false));
                }
            }
            result.AddRange(modItems.Where(i => !seen.Contains(i.Id)));
            return result;
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
