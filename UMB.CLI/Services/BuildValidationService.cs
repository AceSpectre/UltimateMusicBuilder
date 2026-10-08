using CsvHelper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tomlyn;
using Tomlyn.Model;
using UMB.CLI.Desktop;

namespace UMB.CLI.Services
{
    public record BuildValidation(List<string> Warnings, List<SuspiciousVolumeTrack> SuspiciousVolumes);

    /// <summary>Pre-build checks of the mods' series.toml / tracks.csv files.</summary>
    public class BuildValidationService
    {
        /// <summary>Volume multipliers at or above this are rarely intentional (2x is about +6 dB).</summary>
        public const float SuspiciousVolume = 2.0f;

        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;
        private readonly ILogger _logger;

        public BuildValidationService(IOptionsMonitor<Sma5hMusicOptions> musicConfig, ILogger<BuildValidationService> logger)
        {
            _musicConfig = musicConfig;
            _logger = logger;
        }

        /// <summary>Validates the named mod, or every mod when <paramref name="modName"/> is empty.</summary>
        public BuildValidation Validate(string modName) =>
            Validate(ModPaths.ModDirs(_musicConfig.CurrentValue.Sma5hMusic.ModPath)
                .Where(d => string.IsNullOrWhiteSpace(modName) || Path.GetFileName(d).Equals(modName, StringComparison.OrdinalIgnoreCase))
                .ToList());

        public BuildValidation Validate(List<string> activeMods, Dictionary<string, HashSet<string>> seriesFilters = null)
        {
            var warnings = new List<string>();
            var volumes = new List<SuspiciousVolumeTrack>();

            foreach (var modDir in activeMods)
            {
                var seriesDirs = Directory.GetDirectories(modDir)
                    .Where(d => !Path.GetFileName(d).StartsWith("."))
                    .ToList();

                if (seriesFilters != null && seriesFilters.TryGetValue(modDir, out var filter))
                    seriesDirs = seriesDirs.Where(d => filter.Contains(Path.GetFileName(d))).ToList();

                var modName = Path.GetFileName(modDir);

                // Load this mod's series-order.toml so we can warn about custom series
                // that have no explicit position (their in-game order is otherwise unpredictable).
                var modOrderedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var modOrderPath = Path.Combine(modDir,
                    MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_ORDER_TOML_FILE);
                if (File.Exists(modOrderPath))
                {
                    try
                    {
                        var orderTomlText = File.ReadAllText(modOrderPath);
                        var orderModel = Toml.ToModel(orderTomlText);
                        if (orderModel.TryGetValue("order", out var orderVal) && orderVal is TomlArray arr)
                            foreach (var id in arr.OfType<string>())
                                modOrderedIds.Add(id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse {Path} for validation.", modOrderPath);
                    }
                }

                foreach (var seriesDir in seriesDirs)
                {
                    var seriesName = Path.GetFileName(seriesDir);
                    var prefix = $"{modName}/{seriesName}";
                    var csvPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
                    var tomlPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);

                    if (!File.Exists(csvPath))
                        continue;

                    var validGameIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var playlistSongRefs = new List<(string playlistId, string songRef)>();
                    FolderSeriesFileConfig seriesConfig = null;
                    if (File.Exists(tomlPath))
                    {
                        try
                        {
                            var tomlText = File.ReadAllText(tomlPath);
                            seriesConfig = Toml.ToModel<FolderSeriesFileConfig>(tomlText,
                                options: CliUtil.KebabTomlOptions());
                            foreach (var game in seriesConfig.Games ?? new List<FolderGameConfig>())
                            {
                                if (!string.IsNullOrWhiteSpace(game.Id))
                                    validGameIds.Add(game.Id);
                            }
                            foreach (var pl in seriesConfig.Playlists ?? new List<FolderPlaylistOverrideConfig>())
                            {
                                if (string.IsNullOrWhiteSpace(pl.Id)) continue;
                                if (pl.Songs == null || FolderMusicMod.IsWildcardSongs(pl.Songs)) continue;
                                foreach (var s in FolderMusicMod.ExplicitSongs(pl.Songs))
                                    playlistSongRefs.Add((pl.Id, s));
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to parse {Path} for validation.", tomlPath);
                        }
                    }

                    if (seriesConfig?.Series != null
                        && !seriesConfig.Series.ExistingSeries
                        && !string.IsNullOrWhiteSpace(seriesConfig.Series.Id)
                        && !string.Equals(seriesConfig.Series.Id, "etc", StringComparison.OrdinalIgnoreCase)
                        && !modOrderedIds.Contains(seriesConfig.Series.Id))
                    {
                        warnings.Add($"  {prefix}: custom series \"{seriesConfig.Series.Id}\" is not listed in series-order.toml. Its in-game position will be unpredictable. Run 'Scaffold' to append it, or use 'Order Series' to place it manually.");
                    }

                    var csvFilenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        var csvConfig = CliUtil.CsvReadLenient();
                        using var reader = new StreamReader(csvPath);
                        using var csv = new CsvReader(reader, csvConfig);
                        csv.Read();
                        csv.ReadHeader();
                        var headers = csv.HeaderRecord;
                        bool hasOrderColumn = headers.Contains("order");
                        bool hasVolumeColumn = headers.Contains("volume");

                        int rowNum = 0;
                        while (csv.Read())
                        {
                            rowNum++;
                            var filename = csv.GetField("filename")?.Trim() ?? "";
                            var game = csv.GetField("game")?.Trim() ?? "";
                            var title = csv.GetField("title")?.Trim() ?? "";

                            if (string.IsNullOrWhiteSpace(filename))
                                continue;

                            csvFilenames.Add(filename);

                            var volume = hasVolumeColumn ? VolumeConfigService.ParseVolume(csv.GetField("volume")) : 1f;
                            if (volume >= SuspiciousVolume)
                            {
                                volumes.Add(new SuspiciousVolumeTrack(modName, seriesName, filename, title, volume));
                                warnings.Add($"  {prefix}: \"{title}\" ({filename}) has volume {volume}, likely a legacy dB value from an older Convert. Set it to 1 unless the boost is intended");
                            }

                            if (validGameIds.Count > 0 && !string.IsNullOrWhiteSpace(game)
                                && !validGameIds.Contains(game))
                            {
                                warnings.Add($"  {prefix}: \"{title}\" ({filename}) has game \"{game}\" not found in series.toml");
                            }

                            if (!hasOrderColumn)
                            {
                                if (rowNum == 1) // only warn once per file
                                    warnings.Add($"  {prefix}: tracks.csv is missing the \"order\" column");
                            }
                            else
                            {
                                var orderVal = csv.GetField("order")?.Trim() ?? "";
                                if (string.IsNullOrWhiteSpace(orderVal) || !int.TryParse(orderVal, out _))
                                    warnings.Add($"  {prefix}: \"{title}\" ({filename}) is missing a valid order number");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to validate {Path}.", csvPath);
                        continue;
                    }

                    var orphanedNus3 = Directory.GetFiles(seriesDir, "*.nus3audio")
                        .Select(Path.GetFileName)
                        .Where(f => !csvFilenames.Contains(f))
                        .OrderBy(f => f)
                        .ToList();

                    foreach (var file in orphanedNus3)
                        warnings.Add($"  {prefix}: {file} is not listed in tracks.csv");

                    // Matches by stem so "Destroyer" and "Destroyer.nus3audio" are both accepted.
                    if (playlistSongRefs.Count > 0)
                    {
                        var csvStems = new HashSet<string>(
                            csvFilenames.Select(f => Path.GetFileNameWithoutExtension(f)),
                            StringComparer.OrdinalIgnoreCase);
                        foreach (var (playlistId, songRef) in playlistSongRefs)
                        {
                            var stem = Path.GetFileNameWithoutExtension(songRef);
                            if (!csvStems.Contains(stem))
                                warnings.Add($"  {prefix}: [[playlists]] \"{playlistId}\" lists song \"{songRef}\" which doesn't match any track in tracks.csv");
                        }
                    }
                }
            }

            return new BuildValidation(warnings, volumes);
        }
    }
}
