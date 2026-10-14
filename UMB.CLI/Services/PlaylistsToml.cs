using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UMB.CLI.Desktop;

namespace UMB.CLI.Services
{
    /// <summary>
    /// Reads, writes and edits a mod's playlists.toml (built by FolderMusicMod). A playlist without tracks (null) only
    /// carries its name and series flag: the build leaves it as is, plus every song of those series.
    /// </summary>
    public static class PlaylistsToml
    {
        private static readonly Regex SongLine = new(@"^\s*\{\s*song\s*=\s*""([^""]*)""");

        public static string PathIn(string modPath) => Path.Combine(modPath, MusicConstants.MusicModFiles.FOLDER_MOD_PLAYLISTS_TOML_FILE);

        /// <summary>The playlists and stage playlists. A playlist with a name reads as custom (only those get one written).</summary>
        public static (List<ManagedPlaylist> Playlists, List<StageAssignmentInput> Stages) Read(string modPath)
        {
            if (!File.Exists(PathIn(modPath))) return (new(), new());
            var file = FolderMusicMod.ParsePlaylistsFile(PathIn(modPath));
            var playlists = file.Playlists.Where(p => !string.IsNullOrWhiteSpace(p.Id)).Select(p => new ManagedPlaylist(
                p.Id, p.Name ?? p.Id, p.Name != null ? "custom" : "vanilla",
                p.Series.Select(id => new SeriesRef(id, id)).ToList(),
                p.Tracks?.Select(t => new PlaylistTrack(t.Song, FolderMusicMod.Slots<short>(t.Order, 0), FolderMusicMod.Slots<ushort>(t.Incidence, 100))).ToList(),
                null)).ToList();
            var stages = file.Stages.Select(s => new StageAssignmentInput(s.Id, s.Playlist, s.Order)).ToList();
            return (playlists, stages);
        }

        /// <summary>Writes the playlists and stage playlists, removing the file when both are empty.</summary>
        public static void Write(string modPath, IReadOnlyList<ManagedPlaylist> playlists, IReadOnlyList<StageAssignmentInput> stages)
        {
            if (playlists.Count + stages.Count == 0)
            {
                File.Delete(PathIn(modPath));
                return;
            }

            var lines = new List<string>();
            foreach (var playlist in playlists)
            {
                lines.Add("[[playlists]]");
                lines.Add($"id = \"{SeriesToml.Escape(playlist.Id)}\"");
                if (playlist.Kind != "vanilla" && !string.IsNullOrEmpty(playlist.Name) && playlist.Name != playlist.Id)
                    lines.Add($"name = \"{SeriesToml.Escape(playlist.Name)}\"");
                if (playlist.Series?.Count > 0)
                    lines.Add($"series = [{string.Join(", ", playlist.Series.Select(s => $"\"{SeriesToml.Escape(s.Id)}\""))}]");
                if (playlist.Tracks?.Count == 0)
                    lines.Add("tracks = []");
                else if (playlist.Tracks != null)
                {
                    lines.Add("tracks = [");
                    lines.AddRange(playlist.Tracks.Select(t =>
                        $"    {{ song = \"{SeriesToml.Escape(t.BgmId)}\", order = {Slots(t.Orders)}, incidence = {Slots(t.Incidences)} }},"));
                    lines.Add("]");
                }
                lines.Add("");
            }
            foreach (var stage in stages)
            {
                lines.Add("[[stages]]");
                lines.Add($"id = \"{SeriesToml.Escape(stage.Id)}\"");
                lines.Add($"playlist = \"{SeriesToml.Escape(stage.PlaylistId)}\"");
                lines.Add($"order = {Math.Clamp(stage.Order, 0, PlaylistHelper.OrderCount - 1)}");
                lines.Add("");
            }
            SeriesToml.WriteLines(PathIn(modPath), lines);
        }

        /// <summary>The playlist flagged with each series (the first, when several are).</summary>
        public static Dictionary<string, string> SeriesPlaylists(string modPath) =>
            Read(modPath).Playlists
                .SelectMany(p => p.Series.Select(s => (Series: s.Id, Playlist: p.Id)))
                .GroupBy(f => f.Series)
                .ToDictionary(g => g.Key, g => g.First().Playlist);

        /// <summary>
        /// Makes <paramref name="playlistId"/> the series' playlist ("" for none). Its only, non-game playlist is renamed
        /// (with the stages playing it) when the new id is free; otherwise the series moves its flag to the new playlist.
        /// </summary>
        public static void SetSeriesPlaylist(string modPath, string seriesId, string playlistId, string name, ISet<string> gamePlaylists)
        {
            playlistId = (playlistId ?? "").Trim();
            if (PlaylistHelper.IsGameTitlePlaylist(playlistId))
                throw new DesktopApiException($"{PlaylistHelper.GameTitlePlaylistPrefix} playlists are reserved for the build.");

            var (playlists, stages) = Read(modPath);
            bool Flags(ManagedPlaylist p) => p.Series.Any(s => s.Id == seriesId);
            var current = playlists.Where(Flags).ToList();
            if (current.Count == 1 && current[0].Id == playlistId) return;

            var old = current.Count == 1 ? current[0] : null;
            if (old != null && old.Series.Count == 1 && playlistId.Length > 0 && !gamePlaylists.Contains(old.Id)
                && !gamePlaylists.Contains(playlistId) && playlists.All(p => p.Id != playlistId))
            {
                playlists = playlists.Select(p => p == old ? p with { Id = playlistId, Name = string.IsNullOrEmpty(name) ? p.Name : name } : p).ToList();
                stages = stages.Select(s => s.PlaylistId == old.Id ? s with { PlaylistId = playlistId } : s).ToList();
            }
            else
            {
                playlists = playlists
                    .Select(p => Flags(p) ? p with { Series = p.Series.Where(s => s.Id != seriesId).ToList() } : p)
                    .Where(p => p.Tracks != null || p.Series.Count > 0)
                    .ToList();
                var target = playlists.FirstOrDefault(p => p.Id == playlistId);
                if (target != null)
                    playlists[playlists.IndexOf(target)] = target with { Series = target.Series.Append(new SeriesRef(seriesId, seriesId)).ToList() };
                else if (playlistId.Length > 0)
                    playlists.Add(new ManagedPlaylist(playlistId, gamePlaylists.Contains(playlistId) ? playlistId : name, gamePlaylists.Contains(playlistId) ? "vanilla" : "custom",
                        new() { new SeriesRef(seriesId, seriesId) }, null, null));
            }
            Write(modPath, playlists, stages);
        }

        /// <summary>Drops playlist tracks whose song is not kept. Returns the dropped song ids.</summary>
        public static List<string> RemoveSongs(string modPath, Func<string, bool> keep)
        {
            var path = PathIn(modPath);
            if (!File.Exists(path)) return new List<string>();
            var lines = SeriesToml.ReadLines(path);
            var dropped = lines.Select(l => SongLine.Match(l)).Where(m => m.Success && !keep(m.Groups[1].Value)).Select(m => m.Groups[1].Value).ToList();
            if (dropped.Count > 0)
                SeriesToml.WriteLines(path, lines.Where(l => SongLine.Match(l) is not { Success: true } m || keep(m.Groups[1].Value)));
            return dropped;
        }

        /// <summary>One value when every order slot shares it, else the 16 values.</summary>
        private static string Slots<T>(IReadOnlyList<T> values) =>
            values.Distinct().Count() == 1 ? $"{values[0]}" : $"[{string.Join(", ", values)}]";
    }
}
