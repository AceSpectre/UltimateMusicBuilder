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
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
            "# Listed series appear after official series, before \"Other\"\n" +
            "# Unlisted custom series will be placed after these\n";

        private const string VanillaItemPrefix = "vanilla:";
        private static readonly Regex SeriesIdPattern = new("^[a-z0-9_]+$");
        private static readonly Regex PngDataUrl = new("^data:image/png;base64,([A-Za-z0-9+/=]+)$");

        private readonly ILogger _logger;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;
        private readonly VanillaCatalogService _catalog;

        public SeriesOrderService(IOptionsMonitor<Sma5hMusicOptions> musicConfig, VanillaCatalogService catalog,
            ILogger<SeriesOrderService> logger)
        {
            _musicConfig = musicConfig;
            _catalog = catalog;
            _logger = logger;
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

        /// <param name="series">The scanned custom series, indexed like the returned items.</param>
        private SeriesOrderData Load(string resolvedModPath, out List<SeriesFolder> series)
        {
            var folders = ScanSeries(resolvedModPath);
            series = SortedCustomSeries(resolvedModPath, folders);
            var items = series
                .Select((s, index) => new SeriesOrderItem($"series:{index}", s.Name, s.Id, s.Dir, s.VolumeDb, s.IconDataUrl, index, s.Fields, false, new List<string>(), ""))
                .ToList();
            var hasSeriesOrder = SeriesToml.ReadIdList(SeriesOrderPath(resolvedModPath)).Count > 0;
            var vanillaItems = VanillaItems(folders);
            var seriesPlaylists = PlaylistsToml.SeriesPlaylists(resolvedModPath);
            foreach (var item in items.Concat(vanillaItems))
                item.Fields.SeriesPlaylist = seriesPlaylists.GetValueOrDefault(item.SeriesId, item.Fields.SeriesPlaylist);
            return new SeriesOrderData(Path.GetFileName(resolvedModPath), resolvedModPath, hasSeriesOrder, items, vanillaItems);
        }

        /// <summary>
        /// Every vanilla series, in sound test order. One without a folder in the mod yet has an
        /// empty SeriesPath and the vanilla fields; the folder is created on its first edit.
        /// </summary>
        private List<SeriesOrderItem> VanillaItems(List<SeriesFolder> folders)
        {
            if (_catalog.Get() is not { } catalog) return new List<SeriesOrderItem>();
            var existing = folders.Where(f => f.ExistingSeries).GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First());
            return catalog.Series.Select(s =>
                {
                    var gameIds = VanillaTracks.Games(catalog, s.Id).Select(g => g.Id).ToList();
                    return existing.TryGetValue(s.Id, out var folder)
                        ? new SeriesOrderItem(VanillaItemPrefix + s.Id, s.Name, s.Id, folder.Dir, folder.VolumeDb, folder.IconDataUrl, -1, folder.Fields, true, gameIds, s.Playlist)
                        : new SeriesOrderItem(VanillaItemPrefix + s.Id, s.Name, s.Id, "", 0, null, -1, VanillaFields(catalog, s), true, gameIds, s.Playlist);
                })
                .ToList();
        }

        private static SeriesFields VanillaFields(VanillaCatalogService.Catalog catalog, VanillaCatalogService.Series series)
        {
            var games = VanillaTracks.Games(catalog, series.Id);
            return new SeriesFields { Name = series.Name, SeriesPlaylist = series.Playlist, Games = games };
        }

        /// <summary>The folder of a vanilla series in the mod, creating it with every vanilla song as a row.</summary>
        public string EnsureVanillaSeries(string modPath, string seriesId) =>
            EnsureVanillaFolder(ModPaths.ResolveUnderMods(_musicConfig, modPath), seriesId);

        private string EnsureVanillaFolder(string resolvedModPath, string seriesId)
        {
            if (ScanSeries(resolvedModPath).FirstOrDefault(s => s.ExistingSeries && s.Id == seriesId) is { } folder)
                return folder.Dir;

            var catalog = _catalog.Get();
            var series = catalog?.Series.FirstOrDefault(s => s.Id == seriesId) ?? throw new DesktopApiException("Series not found.");
            var dir = Path.Combine(resolvedModPath, seriesId);
            if (Directory.Exists(dir))
                throw new DesktopApiException($"A folder named \"{seriesId}\" already exists in this mod.");

            var fields = VanillaFields(catalog, series);
            WriteNewSeries(dir, seriesId, fields, existingSeries: true);
            if (!PlaylistsToml.SeriesPlaylists(resolvedModPath).ContainsKey(seriesId))
                SetSeriesPlaylist(resolvedModPath, seriesId, fields);
            VanillaTracks.Backfill(dir, catalog);
            _logger.LogInformation("Added vanilla series {SeriesId} to {ModPath}.", seriesId, resolvedModPath);
            return dir;
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
                WriteSeriesTomlFields(Path.Combine(series[item.OriginalIndex].Dir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE), fields);
                SetSeriesPlaylist(resolvedModPath, item.SeriesId, fields);
            }

            // A vanilla series only gets a folder once its fields differ from the vanilla ones.
            foreach (var item in data.VanillaItems)
            {
                if (!fieldsById.TryGetValue(item.Id, out var fields)
                    || (item.SeriesPath == "" && JsonSerializer.Serialize(fields) == JsonSerializer.Serialize(item.Fields))) continue;
                var dir = item.SeriesPath == "" ? EnsureVanillaFolder(resolvedModPath, item.SeriesId) : item.SeriesPath;
                WriteSeriesTomlFields(Path.Combine(dir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE), fields);
                SetSeriesPlaylist(resolvedModPath, item.SeriesId, fields);
            }

            WriteSeriesOrder(resolvedModPath, finalItems.Select(i => i.SeriesId));
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
            if (seriesId == "etc")
                throw new DesktopApiException("\"etc\" is a reserved series ID.");

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
            if (Directory.Exists(seriesDir) || ScanSeries(resolvedModPath).Any(s => s.Id == seriesId)
                || _catalog.Get()?.Series.Any(s => s.Id == seriesId) == true)
                throw new DesktopApiException("A series with that ID already exists.");

            var fields = new SeriesFields { Name = name, SeriesPlaylist = (input.SeriesPlaylist ?? "").Trim(), Games = games };
            WriteNewSeries(seriesDir, seriesId, fields, existingSeries: false);
            SetSeriesPlaylist(resolvedModPath, seriesId, fields);
            if (!string.IsNullOrEmpty(input.IconDataUrl))
                WriteSeriesIcon(seriesDir, input.IconDataUrl);

            return Load(resolvedModPath);
        }

        /// <summary>Creates a series folder: series.toml and a header-only tracks.csv.</summary>
        private static void WriteNewSeries(string seriesDir, string seriesId, SeriesFields fields, bool existingSeries)
        {
            var toml = new StringBuilder();
            CliUtil.AppendSeriesHeader(toml, seriesId, fields.Name, existingSeries, fields.PlaylistIncidence);
            foreach (var game in fields.Games)
                CliUtil.AppendGameBlock(toml, game.Id, game.Name);
            CliUtil.AppendDefaultTrackData(toml, fields.Games.FirstOrDefault()?.Id ?? "");

            Directory.CreateDirectory(seriesDir);
            File.WriteAllText(Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE), toml.ToString().ReplaceLineEndings("\n"));
            File.WriteAllText(Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE), TracksCsv.Header);
        }

        /// <summary>Writes (or replaces) a series' icon.png and returns it as a data URL; a vanilla series gets its folder first.</summary>
        public string SetIcon(string modPath, string seriesId, string iconDataUrl)
        {
            var resolvedModPath = ModPaths.ResolveUnderMods(_musicConfig, modPath);
            var dir = ScanSeries(resolvedModPath).FirstOrDefault(s => s.Id == seriesId)?.Dir ?? EnsureVanillaFolder(resolvedModPath, seriesId);
            WriteSeriesIcon(dir, iconDataUrl);
            return PngDataUrlOf(Path.Combine(dir, MusicConstants.MusicModFiles.FOLDER_MOD_ICON_PNG_FILE));
        }

        private record SeriesFolder(string Dir, string Id, string Name, bool ExistingSeries, string IconPath, SeriesFields Fields, double VolumeDb)
        {
            public string IconDataUrl => IconPath == null ? null : PngDataUrlOf(IconPath);
        }

        /// <summary>Custom (non-existing, non-"etc") series, in saved order then by name.</summary>
        private static List<SeriesFolder> SortedCustomSeries(string modPath, List<SeriesFolder> folders = null)
        {
            var order = SeriesToml.ReadIdList(SeriesOrderPath(modPath));
            int OrderIndex(string id) => order.Contains(id) ? order.IndexOf(id) : int.MaxValue;
            return (folders ?? ScanSeries(modPath))
                .Where(s => !s.ExistingSeries && !string.Equals(s.Id, "etc", StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => OrderIndex(s.Id))
                .ThenBy(s => s.Name, StringComparer.InvariantCulture)
                .ToList();
        }

        /// <summary>Every series folder with a series.toml id, custom and existing.</summary>
        private static List<SeriesFolder> ScanSeries(string modPath)
        {
            var results = new List<SeriesFolder>();
            foreach (var seriesDir in ModPaths.VisibleDirs(modPath))
            {
                var tomlPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
                if (!File.Exists(tomlPath)) continue;

                var text = File.ReadAllText(tomlPath);
                var header = SeriesToml.ReadHeader(text);
                if (header.Id == null) continue;

                var iconPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_ICON_PNG_FILE);
                results.Add(new SeriesFolder(seriesDir, header.Id, header.Name ?? header.Id, header.ExistingSeries,
                    File.Exists(iconPath) ? iconPath : null, ReadFields(text, header), SeriesToml.VolumeDb(text)));
            }
            return results;
        }

        private static SeriesFields ReadFields(string text, SeriesToml.SeriesHeader header)
        {
            var series = SeriesToml.TableSection(text, "series") ?? text;
            var incidence = Regex.Match(series, @"^\s*playlist-incidence\s*=\s*(\d+)", RegexOptions.Multiline);
            return new SeriesFields
            {
                Name = header.Name ?? header.Id ?? "",
                SeriesPlaylist = SeriesToml.String(series, "series-playlist"),
                PlaylistIncidence = incidence.Success ? int.Parse(incidence.Groups[1].Value, CultureInfo.InvariantCulture) : 100,
                Games = SeriesToml.Games(text)
            };
        }

        /// <summary>
        /// Writes the editable [series] and [[games]] fields, keeping id, existing-series, comments and anything else.
        /// </summary>
        private static void WriteSeriesTomlFields(string seriesTomlPath, SeriesFields fields)
        {
            if (!File.Exists(seriesTomlPath)) return;

            var lines = SeriesToml.UpsertTable(SeriesToml.ReadLines(seriesTomlPath), "series", new[]
            {
                new SeriesToml.Entry("name", $"name = \"{SeriesToml.Escape(fields.Name)}\""),
                new SeriesToml.Entry("series-playlist", null),
                new SeriesToml.Entry("playlist-incidence", $"playlist-incidence = {fields.PlaylistIncidence}")
            }, createIfMissing: false);

            lines = SeriesToml.RewriteGames(lines, fields.Games ?? new List<SeriesGame>());

            SeriesToml.WriteLines(seriesTomlPath, lines);
        }

        /// <summary>A series' playlist lives in playlists.toml, flagged with the series (see <see cref="PlaylistsToml.SetSeriesPlaylist"/>).</summary>
        private void SetSeriesPlaylist(string modPath, string seriesId, SeriesFields fields) =>
            PlaylistsToml.SetSeriesPlaylist(modPath, seriesId, fields.SeriesPlaylist, fields.Name,
                _catalog.Get()?.Playlists.Select(p => p.Id).ToHashSet() ?? new HashSet<string>());

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
            var match = PngDataUrl.Match((dataUrl ?? "").Trim());
            if (!match.Success)
                throw new DesktopApiException("Icon must be a PNG image.");
            File.WriteAllBytes(Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_ICON_PNG_FILE),
                Convert.FromBase64String(match.Groups[1].Value));
        }

        private static string PngDataUrlOf(string path) =>
            "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(path));
    }
}
