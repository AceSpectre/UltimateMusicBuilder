using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UMB.CLI.Services
{
    public class CleanupService
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;
        private readonly VanillaCatalogService _catalog;

        public CleanupService(IOptionsMonitor<Sma5hMusicOptions> musicConfig, VanillaCatalogService catalog, ILogger<CleanupService> logger)
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

            int totalRemoved = 0;
            int totalFiles = 0;
            int totalDeadSongs = 0;
            int totalStaleSongs = 0;

            foreach (var modDir in modDirs)
            {
                var seriesDirs = Directory.GetDirectories(modDir)
                    .Where(d => !Path.GetFileName(d).StartsWith(".") && Path.GetFileName(d) != CliUtil.ValidateFolder)
                    .ToList();

                foreach (var seriesDir in seriesDirs)
                {
                    var csvPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
                    if (!File.Exists(csvPath))
                        continue;

                    totalFiles++;

                    var csvConfig = CliUtil.CsvRead();
                    List<FolderTrackCsvRow> rows;
                    using (var reader = new StreamReader(csvPath))
                    using (var csv = new CsvReader(reader, csvConfig))
                    {
                        csv.Context.RegisterClassMap<FolderTrackCsvRowMap>();
                        rows = csv.GetRecords<FolderTrackCsvRow>().ToList();
                    }

                    var removedRows = new List<FolderTrackCsvRow>();
                    foreach (var row in rows)
                    {
                        var audioFile = Path.Combine(seriesDir, row.Filename);
                        if (!FolderMusicMod.IsVanillaBgmId(row.Filename) && !File.Exists(audioFile))
                            removedRows.Add(row);
                    }

                    var relativeCsv = Path.Combine(Path.GetFileName(modDir), Path.GetFileName(seriesDir), "tracks.csv");
                    if (removedRows.Count > 0)
                    {
                        foreach (var row in removedRows)
                        {
                            _logger.LogInformation("Removing '{Filename}' from {CsvPath} (file not found)", row.Filename, relativeCsv);
                            rows.Remove(row);
                            totalRemoved++;
                        }

                        using var writer = new StreamWriter(csvPath);
                        using var csvWriter = new CsvWriter(writer, CliUtil.CsvWrite());
                        csvWriter.Context.RegisterClassMap<FolderTrackCsvRowMap>();
                        csvWriter.WriteRecords(rows);
                    }

                    var tomlPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
                    if (File.Exists(tomlPath))
                    {
                        var validFilenames = new HashSet<string>(
                            rows.Where(r => !string.IsNullOrWhiteSpace(r.Filename)).Select(r => r.Filename),
                            StringComparer.OrdinalIgnoreCase);
                        var validStems = new HashSet<string>(
                            validFilenames.Select(f => Path.GetFileNameWithoutExtension(f)),
                            StringComparer.OrdinalIgnoreCase);
                        var relativeToml = Path.Combine(Path.GetFileName(modDir), Path.GetFileName(seriesDir), "series.toml");
                        var deadInSeries = PruneDeadSongRefsInToml(tomlPath, validStems, relativeToml);
                        totalDeadSongs += deadInSeries;
                    }
                }

                totalStaleSongs += PruneStalePlaylistSongs(modDir);
            }

            _logger.LogInformation("--------------------");
            _logger.LogInformation("Cleanup complete: removed {Removed} entries from {Files} tracks.csv file(s).", totalRemoved, totalFiles);
            if (totalDeadSongs > 0)
                _logger.LogInformation("Pruned {Count} dead song reference(s) from series.toml playlist arrays.", totalDeadSongs);
            if (totalStaleSongs > 0)
                _logger.LogInformation("Pruned {Count} stale song(s) from playlists.toml.", totalStaleSongs);
        }

        /// <summary>Drops playlists.toml songs that are neither vanilla nor in the mod. Returns the songs dropped.</summary>
        private int PruneStalePlaylistSongs(string modDir)
        {
            var relativeToml = Path.Combine(Path.GetFileName(modDir), MusicConstants.MusicModFiles.FOLDER_MOD_PLAYLISTS_TOML_FILE);
            if (!File.Exists(PlaylistsToml.PathIn(modDir))) return 0;
            var catalog = _catalog.Get();
            if (catalog == null)
            {
                _logger.LogWarning("Skipping {TomlPath}: game resources are needed to tell vanilla songs from stale ones.", relativeToml);
                return 0;
            }

            var modSongs = new FolderMusicMod(NullLogger<IMusicMod>.Instance, null, modDir).GetMusicModEntries().BgmDbRootEntries;
            var known = catalog.BgmTitles.Keys.Concat(modSongs.Select(b => b.UiBgmId)).ToHashSet();
            var dropped = PlaylistsToml.RemoveSongs(modDir, known.Contains);
            foreach (var song in dropped)
                _logger.LogInformation("Removing stale song '{Song}' from {TomlPath}", song, relativeToml);
            return dropped.Count;
        }

        /// <summary>
        /// Rewrites `songs = [ ... ]` arrays in series.toml, dropping any entries whose stem
        /// (filename without extension) is not present in <paramref name="validStems"/>.
        /// Comparison is stem-based so users can list either "Destroyer" or "Destroyer.nus3audio".
        /// Wildcard (`songs = "*"`) and missing fields are left untouched. Returns the number of dropped entries.
        /// </summary>
        private int PruneDeadSongRefsInToml(string tomlPath, HashSet<string> validStems, string relativeToml)
        {
            var text = File.ReadAllText(tomlPath);
            var arrayPattern = new Regex(@"songs\s*=\s*\[(?<body>[^\]]*)\]", RegexOptions.Singleline);
            var stringEntryPattern = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"");

            int totalDropped = 0;
            var rewritten = arrayPattern.Replace(text, match =>
            {
                var entries = stringEntryPattern.Matches(match.Groups["body"].Value);
                var kept = new List<string>();
                var dropped = new List<string>();
                foreach (Match e in entries)
                {
                    var raw = Regex.Unescape(e.Groups[1].Value);
                    var stem = Path.GetFileNameWithoutExtension(raw);
                    if (validStems.Contains(stem))
                        kept.Add(raw);
                    else
                        dropped.Add(raw);
                }

                if (dropped.Count == 0)
                    return match.Value;

                foreach (var d in dropped)
                {
                    _logger.LogInformation("Removing dead song reference '{Filename}' from {TomlPath}", d, relativeToml);
                    totalDropped++;
                }

                if (kept.Count == 0)
                    return "songs = \"*\"";

                var sb = new StringBuilder();
                sb.Append("songs = [");
                sb.AppendLine();
                for (int i = 0; i < kept.Count; i++)
                {
                    var comma = i < kept.Count - 1 ? "," : "";
                    sb.AppendLine($"    \"{CliUtil.EscapeToml(kept[i])}\"{comma}");
                }
                sb.Append("]");
                return sb.ToString();
            });

            if (totalDropped > 0)
                File.WriteAllText(tomlPath, rewritten);

            return totalDropped;
        }
    }
}
