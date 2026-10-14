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
        /// <summary>The legacy series.toml key that playlists.toml series flags replace.</summary>
        public const string LegacySeriesPlaylistKey = "series-playlist";

        private static readonly Regex SongLine = new(@"^\s*\{\s*song\s*=\s*""([^""]*)""");

        public static string PathIn(string modPath) => Path.Combine(modPath, MusicConstants.MusicModFiles.FOLDER_MOD_PLAYLISTS_TOML_FILE);

        /// <summary>The playlists (those with an id) and stage playlists.</summary>
        public static FolderPlaylistsFileConfig Read(string modPath)
        {
            if (!File.Exists(PathIn(modPath))) return new();
            var file = FolderMusicMod.ParsePlaylistsFile(PathIn(modPath));
            file.Playlists.RemoveAll(p => string.IsNullOrWhiteSpace(p.Id));
            return file;
        }

        /// <summary>Writes the playlists and stage playlists, removing the file when both are empty.</summary>
        public static void Write(string modPath, FolderPlaylistsFileConfig file)
        {
            if (file.Playlists.Count + file.Stages.Count == 0)
            {
                File.Delete(PathIn(modPath));
                return;
            }

            var lines = new List<string>();
            foreach (var playlist in file.Playlists)
            {
                lines.Add("[[playlists]]");
                lines.Add($"id = \"{SeriesToml.Escape(playlist.Id)}\"");
                if (!string.IsNullOrEmpty(playlist.Name) && playlist.Name != playlist.Id)
                    lines.Add($"name = \"{SeriesToml.Escape(playlist.Name)}\"");
                if (playlist.Series.Count > 0)
                    lines.Add($"series = [{string.Join(", ", playlist.Series.Select(s => $"\"{SeriesToml.Escape(s)}\""))}]");
                if (playlist.Tracks?.Count == 0)
                    lines.Add("tracks = []");
                else if (playlist.Tracks != null)
                {
                    lines.Add("tracks = [");
                    lines.AddRange(playlist.Tracks.Select(t =>
                        $"    {{ song = \"{SeriesToml.Escape(t.Song)}\", order = {SlotText(FolderMusicMod.Slots<short>(t.Order, 0))}, " +
                        $"incidence = {SlotText(FolderMusicMod.Slots<ushort>(t.Incidence, 100))} }},"));
                    lines.Add("]");
                }
                lines.Add("");
            }
            foreach (var stage in file.Stages)
            {
                lines.Add("[[stages]]");
                lines.Add($"id = \"{SeriesToml.Escape(stage.Id)}\"");
                lines.Add($"playlist = \"{SeriesToml.Escape(stage.Playlist)}\"");
                lines.Add($"order = {Math.Clamp(stage.Order, 0, PlaylistHelper.OrderCount - 1)}");
                lines.Add("");
            }
            SeriesToml.WriteLines(PathIn(modPath), lines);
        }

        /// <summary>The playlist flagged with each series (the first, when several are).</summary>
        public static Dictionary<string, string> SeriesPlaylists(string modPath) =>
            Read(modPath).Playlists
                .SelectMany(p => p.Series.Select(s => (Series: s, Playlist: p.Id)))
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

            var file = Read(modPath);
            var current = file.Playlists.Where(p => p.Series.Contains(seriesId)).ToList();
            if (current.Count == 1 && current[0].Id == playlistId) return;

            var old = current.Count == 1 ? current[0] : null;
            var canRename = old != null && old.Series.Count == 1 && playlistId.Length > 0 && !gamePlaylists.Contains(old.Id)
                && !gamePlaylists.Contains(playlistId) && file.Playlists.All(p => p.Id != playlistId);
            if (canRename)
            {
                foreach (var stage in file.Stages.Where(s => s.Playlist == old.Id))
                    stage.Playlist = playlistId;
                old.Id = playlistId;
                if (old.Name != null && !string.IsNullOrEmpty(name))
                    old.Name = name;
            }
            else
            {
                current.ForEach(p => p.Series.RemoveAll(s => s == seriesId));
                file.Playlists.RemoveAll(p => p.Tracks == null && p.Series.Count == 0);
                var target = file.Playlists.FirstOrDefault(p => p.Id == playlistId);
                if (target != null)
                    target.Series.Add(seriesId);
                else if (playlistId.Length > 0)
                    file.Playlists.Add(new FolderPlaylistConfig { Id = playlistId, Name = gamePlaylists.Contains(playlistId) ? null : name, Series = new() { seriesId } });
            }
            Write(modPath, file);
        }

        /// <summary>Drops playlist tracks whose song is not kept, leaving the rest of the file as written. Returns the dropped song ids.</summary>
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
        private static string SlotText<T>(IReadOnlyList<T> values) =>
            values.Distinct().Count() == 1 ? $"{values[0]}" : $"[{string.Join(", ", values)}]";
    }
}
