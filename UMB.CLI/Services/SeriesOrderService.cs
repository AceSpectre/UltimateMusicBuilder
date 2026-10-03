using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UMB.CLI.Desktop;
using UMB.CLI.Views;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace UMB.CLI.Services
{
    /// <summary>
    /// Custom series display order (series-order.toml), series.toml field editing and series
    /// creation. Shared by the desktop app and the CLI's series order window.
    /// </summary>
    public class SeriesOrderService
    {
        private const string SeriesOrderHeader =
            "# Custom series display order\n" +
            "# Listed series appear after official series, before Other\n" +
            "# Unlisted custom series will be placed after these\n";

        // Header-only tracks.csv: the series exists but has no songs yet.
        private const string TracksCsvHeader =
            "filename,game,title,author,copyright,record_type,special_category,volume,info1,in_soundtest\n";

        private static readonly Regex SeriesIdPattern = new("^[a-z0-9_]+$");
        private static readonly Regex PngDataUrl = new("^data:image/png;base64,([A-Za-z0-9+/=]+)$");

        private readonly ILogger _logger;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;
        private readonly VanillaCatalogService _catalog;

        public SeriesOrderService(IOptionsMonitor<Sma5hMusicOptions> musicConfig,
            ILogger<SeriesOrderService> logger, VanillaCatalogService catalog = null)
        {
            _musicConfig = musicConfig;
            _logger = logger;
            _catalog = catalog;
        }

        public void Run()
        {
            Script.PrintBanner(_logger);

            var modPath = _musicConfig.CurrentValue.Sma5hMusic.ModPath;
            Directory.CreateDirectory(modPath);

            var modDirs = Directory.GetDirectories(modPath, "*", SearchOption.TopDirectoryOnly)
                .Where(d => !Path.GetFileName(d).StartsWith("."))
                .ToList();

            if (modDirs.Count == 0)
            {
                _logger.LogWarning("No mod folders found in {ModPath}.", modPath);
                return;
            }

            string selectedModDir;
            if (modDirs.Count == 1)
            {
                selectedModDir = modDirs[0];
                _logger.LogInformation("Using mod: {ModName}", Path.GetFileName(selectedModDir));
            }
            else
            {
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .WrapAround()
                        .Title("Select a mod:")
                        .HighlightStyle(new Style(Color.Cyan1))
                        .AddChoices(modDirs.Select(d => Path.GetFileName(d))));
                selectedModDir = modDirs.First(d => Path.GetFileName(d) == choice);
            }

            var series = SortedCustomSeries(selectedModDir);
            if (series.Count == 0)
            {
                _logger.LogWarning("No custom series found in {ModDir}.", selectedModDir);
                return;
            }

            var viewModels = series.Select(s => new SeriesViewModel
            {
                Id = s.Id,
                Name = s.Name,
                IconPath = s.IconPath,
            }).ToList();

            // Show Avalonia window via the shared host (Avalonia can only be Setup once per process)
            List<string> result = null;
            try
            {
                result = AvaloniaHost.ShowWindow(
                    () => new SeriesOrderWindow(viewModels),
                    w => w.Result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to launch series order window.");
            }

            if (result != null)
            {
                var orderFile = WriteSeriesOrder(selectedModDir, result);
                _logger.LogInformation("Series order saved to {Path}.", orderFile);
            }
            else
            {
                _logger.LogInformation("Series ordering cancelled.");
            }
        }

        public SeriesOrderData Load(string modPath) => Load(ModPaths.ResolveUnderMods(_musicConfig, modPath), out _);

        /// <param name="series">The scanned series, indexed like the returned items.</param>
        private SeriesOrderData Load(string resolvedModPath, out List<CustomSeries> series)
        {
            series = ManagedSeries(resolvedModPath);
            var items = series
                .Select((s, index) => new SeriesOrderItem(s.ExistingSeries ? $"vanilla:{s.Id}" : $"series:{index}", s.Name, s.Id, s.IconDataUrl, index, s.Fields, s.ExistingSeries))
                .ToList();
            var hasSeriesOrder = SeriesToml.ReadIdList(SeriesOrderPath(resolvedModPath)).Count > 0;
            return new SeriesOrderData(Path.GetFileName(resolvedModPath), resolvedModPath, hasSeriesOrder, items);
        }

        /// <summary>
        /// Writes each item's edited series.toml fields, then the display order (listed items
        /// first, then any the caller left out).
        /// </summary>
        public SeriesOrderData Save(string modPath, List<SaveSeriesItem> items)
        {
            var resolvedModPath = ModPaths.ResolveUnderMods(_musicConfig, modPath);
            var data = Load(resolvedModPath, out var series);
            var itemById = data.Items.ToDictionary(i => i.Id);
            var orderedIds = items.Select(i => i.Id).ToList();
            var finalItems = orderedIds.Where(itemById.ContainsKey).Select(id => itemById[id])
                .Concat(data.Items.Where(i => !orderedIds.Contains(i.Id)))
                .ToList();

            var fieldsById = items.Where(i => i.Fields != null).ToDictionary(i => i.Id, i => i.Fields);
            foreach (var item in finalItems)
            {
                if (!fieldsById.TryGetValue(item.Id, out var fields)) continue;
                var entry = series[item.OriginalIndex];
                if (entry.ExistingSeries && JsonSerializer.Serialize(fields) == JsonSerializer.Serialize(item.Fields)) continue;
                EnsureSeriesFiles(entry);
                WriteSeriesTomlFields(Path.Combine(entry.Dir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE), fields);
            }

            WriteSeriesOrder(resolvedModPath, finalItems.Where(i => !i.IsExistingSeries).Select(i => i.SeriesId));
            return Load(resolvedModPath);
        }

        /// <summary>
        /// Creates a custom series folder (series.toml + header-only tracks.csv). The first game
        /// becomes the [default-track-data] game.
        /// </summary>
        public SeriesOrderData Create(string modPath, CreateSeriesInput input)
        {
            var resolvedModPath = ModPaths.ResolveUnderMods(_musicConfig, modPath);

            var seriesId = (input.SeriesId ?? "").Trim();
            if (!SeriesIdPattern.IsMatch(seriesId))
                throw new DesktopApiException("Series ID must contain only lowercase letters, numbers, and underscores.");
            if (seriesId == "etc" || IsVanillaId(seriesId))
                throw new DesktopApiException("That is a reserved vanilla series ID.");

            var name = (input.Name ?? "").Trim();
            if (name.Length == 0)
                throw new DesktopApiException("Series name is required.");

            var games = (input.Games ?? new List<SeriesGame>())
                .Select(g => new SeriesGame((g.Id ?? "").Trim(), (g.Name ?? "").Trim()))
                .Where(g => g.Id.Length > 0)
                .ToList();
            if (games.Count == 0)
                throw new DesktopApiException("At least one game is required.");

            var seriesDir = Path.Combine(resolvedModPath, seriesId);
            if (Directory.Exists(seriesDir) || ScanCustomSeries(resolvedModPath).Any(s => s.Id == seriesId))
                throw new DesktopApiException("A series with that ID already exists.");

            Directory.CreateDirectory(seriesDir);

            var lines = new List<string> { "[series]", $"id = \"{SeriesToml.Escape(seriesId)}\"", $"name = \"{SeriesToml.Escape(name)}\"", "playlist-incidence = 100" };
            var playlist = (input.SeriesPlaylist ?? "").Trim();
            if (playlist.Length > 0)
                lines.Add($"series-playlist = \"{SeriesToml.Escape(playlist)}\"");
            lines.Add("");
            foreach (var game in games)
                lines.AddRange(new[] { "[[games]]", $"id = \"{SeriesToml.Escape(game.Id)}\"", $"name = \"{SeriesToml.Escape(game.Name)}\"", "" });
            lines.AddRange(new[] { "[default-track-data]", $"game = \"{SeriesToml.Escape(games[0].Id)}\"", "author = \"\"", "copyright = \"\"", "record-type = \"original\"", "volume = 1.0", "" });

            SeriesToml.WriteLines(Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE), lines);
            File.WriteAllText(Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE), TracksCsvHeader);
            if (!string.IsNullOrEmpty(input.IconDataUrl))
                WriteSeriesIcon(seriesDir, input.IconDataUrl);

            return Load(resolvedModPath);
        }

        /// <summary>Writes (or replaces) a custom series' icon.png and returns it as a data URL.</summary>
        public string SetIcon(string modPath, string seriesId, string iconDataUrl)
        {
            var resolvedModPath = ModPaths.ResolveUnderMods(_musicConfig, modPath);
            var series = ManagedSeries(resolvedModPath).FirstOrDefault(s => s.Id == seriesId)
                ?? throw new DesktopApiException("Series not found.");
            ValidateIcon(iconDataUrl);
            EnsureSeriesFiles(series);
            WriteSeriesIcon(series.Dir, iconDataUrl);
            return PngDataUrlOf(Path.Combine(series.Dir, MusicConstants.MusicModFiles.FOLDER_MOD_ICON_PNG_FILE));
        }

        private record CustomSeries(string Dir, string Id, string Name, string IconPath, SeriesFields Fields, bool ExistingSeries = false)
        {
            public string IconDataUrl => IconPath == null ? null : PngDataUrlOf(IconPath);
        }

        /// <summary>Custom (non-existing, non-"etc") series, in saved order then by name.</summary>
        private List<CustomSeries> SortedCustomSeries(string modPath)
        {
            var order = SeriesToml.ReadIdList(SeriesOrderPath(modPath));
            int OrderIndex(string id) => order.Contains(id) ? order.IndexOf(id) : int.MaxValue;
            return ScanCustomSeries(modPath)
                .OrderBy(s => OrderIndex(s.Id))
                .ThenBy(s => s.Name, StringComparer.InvariantCulture)
                .ToList();
        }

        private static List<CustomSeries> ScanCustomSeries(string modPath, bool includeExisting = false)
        {
            var results = new List<CustomSeries>();
            if (!Directory.Exists(modPath)) return results;

            foreach (var seriesDir in Directory.GetDirectories(modPath))
            {
                if (Path.GetFileName(seriesDir).StartsWith(".")) continue;
                var tomlPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
                if (!File.Exists(tomlPath)) continue;

                var text = File.ReadAllText(tomlPath);
                var header = SeriesToml.ReadHeader(text);
                if (header.Id == null || ((header.ExistingSeries || IsVanillaId(header.Id)) && !includeExisting)) continue;
                if (!includeExisting && string.Equals(header.Id, "etc", StringComparison.OrdinalIgnoreCase)) continue;

                var iconPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_ICON_PNG_FILE);
                results.Add(new CustomSeries(seriesDir, header.Id, header.Name ?? header.Id,
                    File.Exists(iconPath) ? iconPath : null, ReadFields(text, header), header.ExistingSeries || IsVanillaId(header.Id)));
            }
            return results;
        }

        private static bool IsVanillaId(string id) =>
            DisplayNames.Series.ContainsKey(MusicConstants.InternalIds.SERIES_ID_PREFIX + id);

        private List<CustomSeries> ManagedSeries(string modPath)
        {
            var stored = ScanCustomSeries(modPath, includeExisting: true);
            var catalog = _catalog?.Get();
            var vanilla = DisplayNames.Series
                .Where(s => s.Key != "ui_series_none")
                .Select(s =>
                {
                    var id = s.Key[MusicConstants.InternalIds.SERIES_ID_PREFIX.Length..];
                    return stored.FirstOrDefault(e => e.Id == id) is { } existing ? existing with { ExistingSeries = true } : new CustomSeries(
                        Path.Combine(modPath, id), id, s.Value, null, new SeriesFields { Name = s.Value }, true);
                })
                .Concat(stored.Where(s => s.ExistingSeries && !IsVanillaId(s.Id)))
                .ToList();
            foreach (var entry in vanilla)
            {
                var declared = entry.Fields.Games.Select(g => g.Id).ToHashSet();
                entry.Fields.Games.AddRange(catalog?.GameTitles
                    .Where(g => g.SeriesId == MusicConstants.InternalIds.SERIES_ID_PREFIX + entry.Id && !declared.Contains(g.Id))
                    .Select(g => new SeriesGame(g.Id, g.Name)) ?? Enumerable.Empty<SeriesGame>());
            }
            return SortedCustomSeries(modPath).Where(s => !IsVanillaId(s.Id))
                .Concat(vanilla.OrderBy(s => s.Name, StringComparer.InvariantCulture)).ToList();
        }

        private static void EnsureSeriesFiles(CustomSeries series)
        {
            var tomlPath = Path.Combine(series.Dir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
            if (File.Exists(tomlPath) && SeriesToml.ReadHeader(File.ReadAllText(tomlPath)).Id != series.Id)
                throw new DesktopApiException("The series folder contains settings for another series.");
            Directory.CreateDirectory(series.Dir);
            if (!File.Exists(tomlPath))
                File.WriteAllText(tomlPath, $"[series]\nid = \"{SeriesToml.Escape(series.Id)}\"\nexisting-series = true\nname = \"{SeriesToml.Escape(series.Name)}\"\n");
            else if (series.ExistingSeries)
                SeriesToml.WriteLines(tomlPath, SeriesToml.UpsertTable(SeriesToml.ReadLines(tomlPath), "series",
                    new[] { new SeriesToml.Entry("existing-series", "existing-series = true") }, createIfMissing: false));
            var csvPath = Path.Combine(series.Dir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
            if (!File.Exists(csvPath)) File.WriteAllText(csvPath, TracksCsvHeader);
        }

        private static SeriesFields ReadFields(string text, SeriesToml.SeriesHeader header)
        {
            var series = SeriesToml.TableSection(text, "series") ?? text;
            var incidence = Regex.Match(series, @"^\s*playlist-incidence\s*=\s*(\d+)", RegexOptions.Multiline);
            var defaults = SeriesToml.ReadDefaults(text);
            return new SeriesFields
            {
                Name = header.Name ?? header.Id ?? "",
                SeriesPlaylist = SeriesToml.String(series, "series-playlist"),
                PlaylistIncidence = incidence.Success ? int.Parse(incidence.Groups[1].Value, CultureInfo.InvariantCulture) : 100,
                Games = SeriesToml.Games(text),
                DefaultGame = defaults?.Game ?? "",
                DefaultAuthor = defaults?.Author ?? "",
                DefaultCopyright = defaults?.Copyright ?? "",
                DefaultRecordType = defaults?.RecordType ?? "original",
                DefaultVolume = defaults?.Volume ?? 1
            };
        }

        /// <summary>
        /// Writes the editable [series], [[games]] and [default-track-data] fields, keeping id,
        /// existing-series, comments and anything else.
        /// </summary>
        private static void WriteSeriesTomlFields(string seriesTomlPath, SeriesFields fields)
        {
            if (!File.Exists(seriesTomlPath)) return;

            var lines = SeriesToml.UpsertTable(SeriesToml.ReadLines(seriesTomlPath), "series", new[]
            {
                new SeriesToml.Entry("name", $"name = \"{SeriesToml.Escape(fields.Name)}\""),
                new SeriesToml.Entry("series-playlist", string.IsNullOrEmpty(fields.SeriesPlaylist) ? null : $"series-playlist = \"{SeriesToml.Escape(fields.SeriesPlaylist)}\""),
                new SeriesToml.Entry("playlist-incidence", $"playlist-incidence = {fields.PlaylistIncidence}")
            }, createIfMissing: false);

            lines = SeriesToml.RewriteGames(lines, fields.Games ?? new List<SeriesGame>());

            var recordType = string.IsNullOrEmpty(fields.DefaultRecordType) ? "original" : fields.DefaultRecordType;
            var volume = fields.DefaultVolume == Math.Floor(fields.DefaultVolume)
                ? fields.DefaultVolume.ToString("0.0", CultureInfo.InvariantCulture)
                : fields.DefaultVolume.ToString(CultureInfo.InvariantCulture);
            // Only create [default-track-data] when it already exists or holds a non-default value.
            var defaultsHaveContent = !string.IsNullOrEmpty(fields.DefaultGame) || !string.IsNullOrEmpty(fields.DefaultAuthor)
                || !string.IsNullOrEmpty(fields.DefaultCopyright) || recordType != "original" || fields.DefaultVolume != 1;
            if (lines.Any(l => l.Trim() == "[default-track-data]") || defaultsHaveContent)
            {
                lines = SeriesToml.UpsertTable(lines, "default-track-data", new[]
                {
                    new SeriesToml.Entry("game", $"game = \"{SeriesToml.Escape(fields.DefaultGame)}\""),
                    new SeriesToml.Entry("author", $"author = \"{SeriesToml.Escape(fields.DefaultAuthor)}\""),
                    new SeriesToml.Entry("copyright", $"copyright = \"{SeriesToml.Escape(fields.DefaultCopyright)}\""),
                    new SeriesToml.Entry("record-type", $"record-type = \"{SeriesToml.Escape(recordType)}\""),
                    new SeriesToml.Entry("volume", $"volume = {volume}")
                }, createIfMissing: true);
            }

            SeriesToml.WriteLines(seriesTomlPath, lines);
        }

        private static string SeriesOrderPath(string modPath) =>
            Path.Combine(modPath, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_ORDER_TOML_FILE);

        internal static string WriteSeriesOrder(string modPath, IEnumerable<string> seriesIds)
        {
            var path = SeriesOrderPath(modPath);
            var body = string.Concat(seriesIds.Select(id => $"    \"{SeriesToml.Escape(id)}\",\n"));
            File.WriteAllText(path, $"{SeriesOrderHeader}order = [\n{body}]\n");
            return path;
        }

        // Series icons are stored verbatim as icon.png; there is no transcoder, so only PNG is accepted.
        private static void WriteSeriesIcon(string seriesDir, string dataUrl)
        {
            var bytes = ValidateIcon(dataUrl);
            File.WriteAllBytes(Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_ICON_PNG_FILE),
                bytes);
        }

        private static byte[] ValidateIcon(string dataUrl)
        {
            var match = PngDataUrl.Match((dataUrl ?? "").Trim());
            if (!match.Success) throw new DesktopApiException("Icon must be a PNG image.");
            try { return Convert.FromBase64String(match.Groups[1].Value); }
            catch (FormatException) { throw new DesktopApiException("Icon must be a PNG image."); }
        }

        private static string PngDataUrlOf(string path) =>
            "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(path));
    }
}
