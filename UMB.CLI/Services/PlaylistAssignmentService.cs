using Microsoft.Extensions.Options;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UMB.CLI.Desktop;

namespace UMB.CLI.Services
{
    /// <summary>
    /// Assigns a mod's songs to in-game playlists via the [[playlists]] blocks in each
    /// series.toml (missing or "*" songs = every song in the series).
    /// </summary>
    public class PlaylistAssignmentService
    {
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;

        public PlaylistAssignmentService(IOptionsMonitor<Sma5hMusicOptions> musicConfig)
        {
            _musicConfig = musicConfig;
        }

        public ManagePlaylistsData Load(string modPath)
        {
            var resolvedModPath = ModPaths.ResolveUnderMods(_musicConfig, modPath);
            var series = ScanModSeries(resolvedModPath);

            var songs = series.SelectMany(s => s.Tracks.Select(t => new ModSong(s.SeriesId, s.SeriesName, t.Filename, t.Title))).ToList();

            // Resolve [[playlists]] blocks into per-song assignments; the last block wins on a duplicate song + playlist.
            var assignmentsByPlaylist = new Dictionary<string, Dictionary<(string, string), PlaylistSongAssignment>>();
            foreach (var s in series)
            {
                var trackByStem = s.Tracks.GroupBy(t => Stem(t.Filename)).ToDictionary(g => g.Key, g => g.Last());
                foreach (var block in s.Blocks)
                {
                    var matched = block.Songs == null
                        ? s.Tracks
                        : block.Songs.Select(name => trackByStem.GetValueOrDefault(Stem(name))).Where(t => t != null).ToList();
                    if (!assignmentsByPlaylist.TryGetValue(block.Id, out var perPlaylist))
                        assignmentsByPlaylist[block.Id] = perPlaylist = new Dictionary<(string, string), PlaylistSongAssignment>();
                    foreach (var t in matched)
                        perPlaylist[(s.SeriesId, t.Filename)] = new PlaylistSongAssignment(s.SeriesId, s.SeriesName, t.Filename, t.Title, block.Incidence);
                }
            }

            // Targets = existing game playlists, then any other playlist ids the mod references.
            var playlistIds = DisplayNames.Playlists.Keys.ToList();
            playlistIds.AddRange(assignmentsByPlaylist.Keys.Where(id => !DisplayNames.Playlists.ContainsKey(id)));

            var playlists = playlistIds.Select(id =>
            {
                var assignments = assignmentsByPlaylist.TryGetValue(id, out var byKey) ? byKey.Values.ToList() : new List<PlaylistSongAssignment>();
                return new PlaylistTarget(id, DisplayNames.Playlists.GetValueOrDefault(id, id), assignments.Count, assignments);
            }).ToList();

            return new ManagePlaylistsData(Path.GetFileName(resolvedModPath), resolvedModPath, playlists, songs);
        }

        /// <summary>
        /// Rewrites every series' [[playlists]] blocks from the assignments (series left without
        /// any lose theirs). One block per playlist + incidence, so songs can differ in incidence.
        /// </summary>
        public ManagePlaylistsData Save(string modPath, List<PlaylistAssignmentInput> assignments)
        {
            var resolvedModPath = ModPaths.ResolveUnderMods(_musicConfig, modPath);
            var series = ScanModSeries(resolvedModPath);
            var bySeries = (assignments ?? new List<PlaylistAssignmentInput>())
                .GroupBy(a => a.SeriesId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var s in series)
            {
                var blocks = bySeries.GetValueOrDefault(s.SeriesId, new List<PlaylistAssignmentInput>())
                    .Select(a => (a.PlaylistId, a.Filename, Incidence: (int)Math.Clamp(Math.Truncate(a.Incidence), 0, 65535)))
                    .GroupBy(a => (a.PlaylistId, a.Incidence))
                    .OrderBy(g => g.Key.PlaylistId, StringComparer.InvariantCulture)
                    .ThenBy(g => g.Key.Incidence)
                    .Select(g => PlaylistBlock(g.Key.PlaylistId, g.Key.Incidence, g.Select(a => a.Filename).Distinct().ToList()))
                    .ToList();

                var tomlPath = Path.Combine(s.Dir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
                SeriesToml.WriteLines(tomlPath, SeriesToml.RewritePlaylists(SeriesToml.ReadLines(tomlPath), blocks));
            }

            return Load(resolvedModPath);
        }

        private static List<string> PlaylistBlock(string id, int incidence, List<string> filenames)
        {
            var block = new List<string> { "[[playlists]]", $"id = \"{SeriesToml.Escape(id)}\"", $"incidence = {incidence}" };
            if (filenames.Count == 1)
            {
                block.Add($"songs = [\"{SeriesToml.Escape(filenames[0])}\"]");
            }
            else
            {
                block.Add("songs = [");
                block.AddRange(filenames.Select(f => $"    \"{SeriesToml.Escape(f)}\","));
                block.Add("]");
            }
            return block;
        }

        private record Track(string Filename, string Title);
        private record PlaylistBlockConfig(string Id, int Incidence, List<string> Songs);
        private record ModSeries(string Dir, string SeriesId, string SeriesName, List<Track> Tracks, List<PlaylistBlockConfig> Blocks);

        private static List<ModSeries> ScanModSeries(string modPath)
        {
            var results = new List<ModSeries>();
            if (!Directory.Exists(modPath)) return results;

            foreach (var dir in Directory.GetDirectories(modPath))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith(".") || name == CliUtil.ValidateFolder) continue;
                var tomlPath = Path.Combine(dir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
                var csvPath = Path.Combine(dir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
                if (!File.Exists(tomlPath) || !File.Exists(csvPath)) continue;

                var text = File.ReadAllText(tomlPath);
                var header = SeriesToml.ReadHeader(text);
                if (header.Id == null) continue;

                var tracks = TracksCsv.ReadLenient(csvPath)
                    .Where(r => r.Get("filename").Length > 0)
                    .Select(r => new Track(r.Get("filename"),
                        r.Get("title").Length > 0 ? r.Get("title") : Path.GetFileNameWithoutExtension(r.Get("filename"))))
                    .ToList();

                results.Add(new ModSeries(dir, header.Id, header.Name ?? header.Id, tracks, ParsePlaylistBlocks(text)));
            }
            return results;
        }

        private static List<PlaylistBlockConfig> ParsePlaylistBlocks(string text)
        {
            var blocks = new List<PlaylistBlockConfig>();
            foreach (var block in SeriesToml.TableArrayBlocks(text, "playlists"))
            {
                var id = SeriesToml.NonEmptyString(block, "id");
                if (id == null) continue;
                var incidence = Regex.Match(block, @"^\s*incidence\s*=\s*(\d+)", RegexOptions.Multiline);
                var songs = Regex.Match(block, @"songs\s*=\s*(""\*""|'\*'|\[[\s\S]*?\])");
                List<string> songList = null;
                if (songs.Success && songs.Groups[1].Value.StartsWith("["))
                    songList = Regex.Matches(songs.Groups[1].Value, @"""([^""]*)""").Select(m => m.Groups[1].Value).ToList();
                blocks.Add(new PlaylistBlockConfig(id,
                    incidence.Success ? int.Parse(incidence.Groups[1].Value, CultureInfo.InvariantCulture) : 100, songList));
            }
            return blocks;
        }

        private static string Stem(string filename) => Path.GetFileNameWithoutExtension(filename).ToLowerInvariant();
    }
}
