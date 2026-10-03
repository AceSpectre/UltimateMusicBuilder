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
using UMB.CLI.Desktop;

namespace UMB.CLI.Services
{
    /// <summary>
    /// Merges two or more UMB mods into a new mod. Series found in several mods are merged, with
    /// the priority mod's series.toml, tracks and files winning. Shared by the desktop app and
    /// the interactive CLI.
    /// </summary>
    public class MergeService
    {
        private static readonly string[] TrackColumns =
            { "filename", "game", "title", "author", "copyright", "record_type", "special_category", "volume", "info1", "in_soundtest", "order" };

        // Values for columns a source tracks.csv doesn't have (others default to "").
        private static readonly Dictionary<string, string> ColumnDefaults = new()
        {
            ["record_type"] = "original",
            ["volume"] = "1",
            ["in_soundtest"] = "True"
        };

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

            var selectedNames = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .WrapAround()
                    .Title("Select mods to merge (space to toggle, enter to confirm):")
                    .Required()
                    .HighlightStyle(new Style(Color.Cyan1))
                    .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
                    .AddChoices(modDirs.Select(Path.GetFileName)));

            if (selectedNames.Count < 2)
            {
                _logger.LogError("Must select at least 2 mods to merge.");
                return;
            }

            var selectedDirs = selectedNames.Select(n => modDirs.First(d => Path.GetFileName(d) == n)).ToList();

            var outputModName = AnsiConsole.Prompt(
                new TextPrompt<string>("Name for the merged mod folder:")
                    .Validate(name => ValidateOutputName(name) is string error
                        ? ValidationResult.Error(error)
                        : ValidationResult.Success()));

            var analysis = Analyze(selectedDirs);
            string priorityModPath = null;
            if (analysis.Conflicts.Count > 0)
            {
                _logger.LogInformation("Merge conflicts detected in {Count} series:", analysis.Conflicts.Count);
                foreach (var conflict in analysis.Conflicts)
                    _logger.LogInformation("  {Series}: found in {Mods}", conflict.SeriesName, string.Join(", ", conflict.Mods));

                var priorityMod = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .WrapAround()
                        .Title("Which mod should take priority for conflicts?")
                        .HighlightStyle(new Style(Color.Cyan1))
                        .AddChoices(selectedNames));
                priorityModPath = selectedDirs.First(d => Path.GetFileName(d) == priorityMod);
            }

            var result = Execute(selectedDirs, outputModName, priorityModPath);
            _logger.LogInformation("Output: {OutputDir}", result.OutputPath);
        }

        public MergeAnalysis Analyze(List<string> modPaths)
        {
            var resolvedPaths = (modPaths ?? new List<string>()).Select(Path.GetFullPath).ToList();
            foreach (var path in resolvedPaths)
            {
                if (!ModPaths.IsUnderMods(_musicConfig, path)) throw new DesktopApiException($"Invalid mod path: {path}");
                if (!Directory.Exists(path)) throw new DesktopApiException($"Mod not found: {path}");
            }

            var sourcesBySeries = new Dictionary<string, List<MergeSeriesSource>>();
            foreach (var modPath in resolvedPaths)
            {
                foreach (var seriesDir in Directory.GetDirectories(modPath))
                {
                    var name = Path.GetFileName(seriesDir);
                    if (name.StartsWith(".") || !File.Exists(Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE)))
                        continue;
                    if (!sourcesBySeries.TryGetValue(name, out var sources))
                        sourcesBySeries[name] = sources = new List<MergeSeriesSource>();
                    sources.Add(new MergeSeriesSource(Path.GetFileName(modPath), modPath, seriesDir));
                }
            }

            var series = sourcesBySeries
                .OrderBy(s => s.Key, StringComparer.InvariantCultureIgnoreCase)
                .Select(s => new MergeSeries(s.Key, s.Value))
                .ToList();
            var conflicts = series
                .Where(s => s.Sources.Count > 1)
                .Select(s => new MergeConflict(s.Name, s.Sources.Select(src => src.ModName).ToList()))
                .ToList();

            return new MergeAnalysis(resolvedPaths.Select(Path.GetFileName).ToList(), resolvedPaths, series, conflicts, series.Count);
        }

        /// <summary>The problem with a merged mod name, or null when it can be used.</summary>
        public string ValidateOutputName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Name cannot be empty.";
            var trimmed = name.Trim();
            if (trimmed.IndexOfAny("<>:\"/\\|?*".ToCharArray()) >= 0) return "Name contains invalid characters.";
            if (Directory.Exists(Path.Combine(ModPaths.Root(_musicConfig), trimmed))) return "A mod with that name already exists.";
            return null;
        }

        public MergeResult Execute(List<string> modPaths, string outputName, string priorityModPath)
        {
            if (ValidateOutputName(outputName) is string nameError)
                throw new DesktopApiException(nameError);

            var analysis = Analyze(modPaths);
            if (analysis.TotalSeries == 0)
                throw new DesktopApiException("No series folders found in the selected mods.");

            var priority = string.IsNullOrEmpty(priorityModPath) ? null : Path.GetFullPath(priorityModPath);
            var outputDir = Path.Combine(ModPaths.Root(_musicConfig), outputName.Trim());
            Directory.CreateDirectory(outputDir);

            int totalTracks = 0, conflictsResolved = 0;
            foreach (var series in analysis.Series)
            {
                var outputSeriesDir = Path.Combine(outputDir, series.Name);
                Directory.CreateDirectory(outputSeriesDir);

                if (series.Sources.Count == 1)
                {
                    var source = series.Sources[0];
                    foreach (var file in Directory.GetFiles(source.SeriesPath))
                        File.Copy(file, Path.Combine(outputSeriesDir, Path.GetFileName(file)), overwrite: true);
                    var trackCount = TracksCsv.CountDataRows(Path.Combine(outputSeriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE));
                    totalTracks += trackCount;
                    _logger.LogInformation("Copied series '{Series}' from {Mod} ({Tracks} tracks)", series.Name, source.ModName, trackCount);
                }
                else
                {
                    var orderedDirs = series.Sources
                        .OrderByDescending(s => s.ModPath == priority)
                        .Select(s => s.SeriesPath)
                        .ToList();
                    var trackCount = MergeSeriesFolders(orderedDirs, outputSeriesDir, series.Name);
                    totalTracks += trackCount;
                    conflictsResolved++;
                    _logger.LogInformation("Merged series '{Series}' from {Count} mods ({Tracks} tracks)", series.Name, series.Sources.Count, trackCount);
                }
            }

            MergeSeriesOrder(analysis.ModPaths, priority, outputDir);

            _logger.LogInformation("Merge complete: {SeriesCount} series, {TrackCount} tracks", analysis.TotalSeries, totalTracks);
            return new MergeResult(outputDir, outputName.Trim(), analysis.TotalSeries, totalTracks, conflictsResolved);
        }

        private int MergeSeriesFolders(List<string> orderedSourceDirs, string outputDir, string seriesName)
        {
            var tomlOptions = CliUtil.KebabTomlOptions();
            FolderSeriesFileConfig priorityConfig = null;
            var mergedGames = new List<FolderGameConfig>();
            var mergedPlaylists = new List<FolderPlaylistOverrideConfig>();

            foreach (var srcDir in orderedSourceDirs)
            {
                var tomlPath = Path.Combine(srcDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
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

                priorityConfig ??= config;
                foreach (var game in config.Games.Where(g => !string.IsNullOrEmpty(g.Id)))
                {
                    if (!mergedGames.Any(g => string.Equals(g.Id, game.Id, StringComparison.OrdinalIgnoreCase)))
                        mergedGames.Add(game);
                }
                foreach (var playlist in config.Playlists.Where(p => !string.IsNullOrEmpty(p.Id)))
                {
                    if (!mergedPlaylists.Any(p => string.Equals(p.Id, playlist.Id, StringComparison.OrdinalIgnoreCase)))
                        mergedPlaylists.Add(playlist);
                }
            }

            if (priorityConfig == null)
            {
                _logger.LogError("No valid series.toml found for series '{Series}'.", seriesName);
                return 0;
            }

            WriteMergedSeriesToml(outputDir, priorityConfig, mergedGames, mergedPlaylists);

            var allTracks = new List<CsvRow>();
            var seenFilenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var srcDir in orderedSourceDirs)
            {
                foreach (var track in TracksCsv.ReadLenient(Path.Combine(srcDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE)))
                {
                    var filename = track.Get("filename");
                    if (filename.Length > 0 && seenFilenames.Add(filename))
                        allTracks.Add(track);
                }
            }
            WriteMergedTracksCsv(outputDir, allTracks);

            // Priority mod wins on duplicate filenames
            var copiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var srcDir in orderedSourceDirs)
            {
                foreach (var file in Directory.GetFiles(srcDir))
                {
                    var fileName = Path.GetFileName(file);
                    if (string.Equals(fileName, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(fileName, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (copiedFiles.Add(fileName))
                        File.Copy(file, Path.Combine(outputDir, fileName), overwrite: true);
                }
            }

            return allTracks.Count;
        }

        private static void WriteMergedSeriesToml(string outputDir, FolderSeriesFileConfig priorityConfig,
            List<FolderGameConfig> games, List<FolderPlaylistOverrideConfig> playlists)
        {
            var sb = new StringBuilder();
            var series = priorityConfig.Series;
            CliUtil.AppendSeriesHeader(sb, series.Id, series.Name,
                existingSeries: series.ExistingSeries,
                playlistIncidence: series.PlaylistIncidence != 100 ? series.PlaylistIncidence : null,
                seriesPlaylist: string.IsNullOrWhiteSpace(series.SeriesPlaylist) ? null : series.SeriesPlaylist);

            foreach (var game in games)
                CliUtil.AppendGameBlock(sb, game.Id, game.Name ?? "");

            foreach (var playlist in playlists)
            {
                sb.AppendLine("[[playlists]]");
                sb.AppendLine($"id = \"{CliUtil.EscapeToml(playlist.Id)}\"");
                sb.AppendLine($"incidence = {playlist.Incidence}");
                AppendSongsField(sb, playlist.Songs);
                sb.AppendLine();
            }

            if (priorityConfig.DefaultTrackData is { } d)
            {
                sb.AppendLine("[default-track-data]");
                if (!string.IsNullOrEmpty(d.Game))
                    sb.AppendLine($"game = \"{CliUtil.EscapeToml(d.Game)}\"");
                if (!string.IsNullOrEmpty(d.Author))
                    sb.AppendLine($"author = \"{CliUtil.EscapeToml(d.Author)}\"");
                if (!string.IsNullOrEmpty(d.Copyright))
                    sb.AppendLine($"copyright = \"{CliUtil.EscapeToml(d.Copyright)}\"");
                sb.AppendLine($"record-type = \"{CliUtil.EscapeToml(d.RecordType ?? "original")}\"");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "volume = {0}", d.Volume));
                sb.AppendLine();
            }

            File.WriteAllText(Path.Combine(outputDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE), sb.ToString());
        }

        internal static void AppendSongsField(StringBuilder sb, object songs)
        {
            var explicitSongs = songs == null || FolderMusicMod.IsWildcardSongs(songs)
                ? new List<string>()
                : FolderMusicMod.ExplicitSongs(songs);
            if (explicitSongs.Count == 0)
            {
                sb.AppendLine("songs = \"*\"");
                return;
            }
            sb.AppendLine("songs = [");
            for (var i = 0; i < explicitSongs.Count; i++)
                sb.AppendLine($"    \"{CliUtil.EscapeToml(explicitSongs[i])}\"{(i < explicitSongs.Count - 1 ? "," : "")}");
            sb.AppendLine("]");
        }

        private static void WriteMergedTracksCsv(string outputDir, List<CsvRow> tracks)
        {
            var rows = tracks.Select((t, i) =>
            {
                var row = new CsvRow();
                foreach (var column in TrackColumns)
                    row[column] = t.TryGetValue(column, out var value) ? value : ColumnDefaults.GetValueOrDefault(column, "");
                row["order"] = i.ToString(CultureInfo.InvariantCulture);
                return row;
            });
            TracksCsv.Write(Path.Combine(outputDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE), rows, TrackColumns);
        }

        /// <summary>Combines the mods' series-order.toml lists, priority mod first, without duplicates.</summary>
        private void MergeSeriesOrder(List<string> modPaths, string priorityModPath, string outputDir)
        {
            var orderedDirs = modPaths.OrderByDescending(d => d == priorityModPath).ToList();
            var mergedOrder = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var modDir in orderedDirs)
            {
                var orderFile = Path.Combine(modDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_ORDER_TOML_FILE);
                if (!File.Exists(orderFile)) continue;
                try
                {
                    var model = Toml.ToModel(File.ReadAllText(orderFile));
                    if (model.TryGetValue("order", out var value) && value is Tomlyn.Model.TomlArray order)
                        mergedOrder.AddRange(order.OfType<string>().Where(id => id.Length > 0 && seen.Add(id)));
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Failed to parse {Path}, skipping.", orderFile);
                }
            }

            if (mergedOrder.Count == 0)
                return;

            SeriesOrderService.WriteSeriesOrder(outputDir, mergedOrder);
            _logger.LogInformation("Merged series-order.toml with {Count} series.", mergedOrder.Count);
        }
    }
}
