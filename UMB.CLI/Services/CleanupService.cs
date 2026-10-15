using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UMB.CLI.Desktop;

namespace UMB.CLI.Services
{
    /// <summary>Removes mod entries that point at missing audio: tracks.csv rows, then the series.toml and playlists.toml songs left dangling.</summary>
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

        /// <summary>CLI: cleans every mod.</summary>
        public void Run()
        {
            Script.PrintBanner(_logger);

            var modPath = _musicConfig.CurrentValue.Sma5hMusic.ModPath;
            Directory.CreateDirectory(modPath);
            var modDirs = Directory.GetDirectories(modPath).Where(d => !Path.GetFileName(d).StartsWith(".")).ToList();
            if (modDirs.Count == 0)
            {
                _logger.LogWarning("No mod folders found in {ModPath}.", modPath);
                return;
            }

            var removed = modDirs.Sum(modDir => Clean(modDir, apply: true).Count);
            _logger.LogInformation("--------------------");
            _logger.LogInformation("Cleanup complete: removed {Count} entries.", removed);
        }

        /// <summary>Every entry cleaning the mod would remove, one per row, song reference or playlist song.</summary>
        public List<CleanupItem> Preview(string modPath) => Clean(Resolve(modPath), apply: false);

        /// <summary>Cleans the mod and returns what it removed.</summary>
        public List<CleanupItem> Execute(string modPath)
        {
            var removed = Clean(Resolve(modPath), apply: true);
            _logger.LogInformation("Cleanup complete: removed {Count} entries.", removed.Count);
            return removed;
        }

        private string Resolve(string modPath) => ModPaths.ResolveUnderMods(_musicConfig, modPath);

        private List<CleanupItem> Clean(string modDir, bool apply)
        {
            var items = new List<CleanupItem>();
            var seriesDirs = Directory.GetDirectories(modDir)
                .Where(d => !Path.GetFileName(d).StartsWith(".") && Path.GetFileName(d) != CliUtil.ValidateFolder);

            foreach (var seriesDir in seriesDirs)
            {
                var series = Path.GetFileName(seriesDir);
                var csvPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
                if (!File.Exists(csvPath))
                    continue;

                var (rows, headers) = TracksCsv.Read(csvPath);
                var missing = rows.Where(r => TracksCsv.IsMissingFile(seriesDir, r.Get("filename").Trim())).ToList();
                items.AddRange(missing.Select(r => new CleanupItem($"{series}/tracks.csv", r.Get("filename"))));
                if (apply && missing.Count > 0)
                    TracksCsv.Write(csvPath, rows.Except(missing), headers);

                var tomlPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
                if (File.Exists(tomlPath))
                {
                    var validStems = rows.Except(missing).Select(r => Path.GetFileNameWithoutExtension(r.Get("filename").Trim()))
                        .Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    items.AddRange(PruneDeadSongRefsInToml(tomlPath, validStems, apply).Select(s => new CleanupItem($"{series}/series.toml", s)));
                }
            }

            items.AddRange(PruneStalePlaylistSongs(modDir, apply));

            if (apply)
            {
                foreach (var item in items)
                    _logger.LogInformation("Removed '{Entry}' from {Mod}/{File}", item.Entry, Path.GetFileName(modDir), item.File);
            }
            return items;
        }

        /// <summary>playlists.toml songs that are neither vanilla nor in the mod (FolderMusicMod skips rows with missing audio).</summary>
        private List<CleanupItem> PruneStalePlaylistSongs(string modDir, bool apply)
        {
            if (!File.Exists(PlaylistsToml.PathIn(modDir))) return new List<CleanupItem>();
            var catalog = _catalog.Get();
            if (catalog == null)
            {
                _logger.LogWarning("Skipping {Mod}/playlists.toml: game resources are needed to tell vanilla songs from stale ones.", Path.GetFileName(modDir));
                return new List<CleanupItem>();
            }

            var modSongs = new FolderMusicMod(NullLogger<IMusicMod>.Instance, null, modDir).GetMusicModEntries().BgmDbRootEntries;
            var known = catalog.BgmTitles.Keys.Concat(modSongs.Select(b => b.UiBgmId)).ToHashSet();
            return PlaylistsToml.RemoveSongs(modDir, known.Contains, apply)
                .Select(s => new CleanupItem(MusicConstants.MusicModFiles.FOLDER_MOD_PLAYLISTS_TOML_FILE, $"{s.Playlist}: {s.Song}"))
                .ToList();
        }

        /// <summary>
        /// Drops `songs = [ ... ]` entries in series.toml whose stem is not in <paramref name="validStems"/>, so users can
        /// list either "Destroyer" or "Destroyer.nus3audio". Wildcard (`songs = "*"`) is left untouched. Returns the dropped entries.
        /// </summary>
        private static List<string> PruneDeadSongRefsInToml(string tomlPath, HashSet<string> validStems, bool apply)
        {
            var text = File.ReadAllText(tomlPath);
            var arrayPattern = new Regex(@"songs\s*=\s*\[(?<body>[^\]]*)\]", RegexOptions.Singleline);
            var stringEntryPattern = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"");

            var allDropped = new List<string>();
            var rewritten = arrayPattern.Replace(text, match =>
            {
                var entries = stringEntryPattern.Matches(match.Groups["body"].Value).Select(e => Regex.Unescape(e.Groups[1].Value)).ToList();
                var kept = entries.Where(e => validStems.Contains(Path.GetFileNameWithoutExtension(e))).ToList();
                if (kept.Count == entries.Count)
                    return match.Value;

                allDropped.AddRange(entries.Except(kept));
                if (kept.Count == 0)
                    return "songs = \"*\"";

                var sb = new StringBuilder();
                sb.AppendLine("songs = [");
                for (int i = 0; i < kept.Count; i++)
                    sb.AppendLine($"    \"{CliUtil.EscapeToml(kept[i])}\"{(i < kept.Count - 1 ? "," : "")}");
                sb.Append("]");
                return sb.ToString();
            });

            if (apply && allDropped.Count > 0)
                File.WriteAllText(tomlPath, rewritten);
            return allDropped;
        }
    }
}
