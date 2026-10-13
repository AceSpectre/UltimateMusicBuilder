using Sma5h.Mods.Music.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UMB.CLI.Desktop;

namespace UMB.CLI.Services
{
    /// <summary>Writes and edits a mod's playlists.toml (read by FolderMusicMod).</summary>
    public static class PlaylistsToml
    {
        private static readonly Regex SongLine = new(@"^\s*\{\s*song\s*=\s*""([^""]*)""");

        public static string PathIn(string modPath) => Path.Combine(modPath, MusicConstants.MusicModFiles.FOLDER_MOD_PLAYLISTS_TOML_FILE);

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
                if ((playlist.Tracks ?? new()).Count == 0)
                    lines.Add("tracks = []");
                else
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

        /// <summary>Renames a playlist and the stages playing it.</summary>
        public static void RenamePlaylist(string modPath, string oldId, string newId)
        {
            var path = PathIn(modPath);
            if (!File.Exists(path)) return;
            var old = new Regex($@"^(id|playlist) = ""{Regex.Escape(SeriesToml.Escape(oldId))}""\s*$");
            SeriesToml.WriteLines(path, SeriesToml.ReadLines(path).Select(line => old.Replace(line, m => $"{m.Groups[1].Value} = \"{SeriesToml.Escape(newId)}\"")));
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
