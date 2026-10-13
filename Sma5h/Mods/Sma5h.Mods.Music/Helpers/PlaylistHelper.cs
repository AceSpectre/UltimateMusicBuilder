using Sma5h.Mods.Music.Models;
using Sma5h.Mods.Music.Models.PlaylistEntryModels;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Sma5h.Mods.Music.Helpers
{
    /// <summary>
    /// Playlist changes shared by the build and the desktop playlist editor, so both see the same playlists.
    /// A track has an order and incidence per stage order slot (0-15); order -1 leaves it out of that slot.
    /// </summary>
    public static class PlaylistHelper
    {
        public const int OrderCount = 16;
        public const string SmashBattlePlaylist = "bgmsmashbtl";
        /// <summary>Every mod series gets a playlist of all its songs, named this prefix plus the series id.</summary>
        public const string GameTitlePlaylistPrefix = "bgm_gametitle_";

        private static readonly PropertyInfo[] OrderProperties = Properties("Order");
        private static readonly PropertyInfo[] IncidenceProperties = Properties("Incidence");

        public static short[] Orders(PlaylistValueEntry track) => OrderProperties.Select(p => (short)p.GetValue(track)).ToArray();
        public static ushort[] Incidences(PlaylistValueEntry track) => IncidenceProperties.Select(p => (ushort)p.GetValue(track)).ToArray();

        public static PlaylistValueEntry Track(string uiBgmId, IReadOnlyList<short> orders, IReadOnlyList<ushort> incidences)
        {
            var track = new PlaylistValueEntry { UiBgmId = uiBgmId };
            for (var i = 0; i < OrderCount; i++)
            {
                OrderProperties[i].SetValue(track, orders[i]);
                IncidenceProperties[i].SetValue(track, incidences[i]);
            }
            return track;
        }

        public static PlaylistValueEntry Track(string uiBgmId, short order, ushort incidence) =>
            Track(uiBgmId, Enumerable.Repeat(order, OrderCount).ToArray(), Enumerable.Repeat(incidence, OrderCount).ToArray());

        /// <summary>Adds the playlist, or merges its new tracks into the one with the same id. Returns the tracks added.</summary>
        public static int Merge(IDictionary<string, PlaylistEntry> playlists, PlaylistEntry playlist)
        {
            if (!playlists.TryGetValue(playlist.Id, out var existing))
            {
                playlists[playlist.Id] = playlist;
                return playlist.Tracks.Count;
            }
            var known = existing.Tracks.Select(t => t.UiBgmId).ToHashSet();
            var added = playlist.Tracks.Where(t => known.Add(t.UiBgmId)).ToList();
            existing.Tracks.AddRange(added);
            return added.Count;
        }

        /// <summary>
        /// The songs the build always keeps in a playlist: a mod series' own playlists hold every song of the series,
        /// and bgmsmashbtl plus the playlists of the series' stages hold its sound test songs.
        /// </summary>
        public static List<RequiredSong> RequiredSongs(IEnumerable<StageEntry> stages, IEnumerable<PlaylistEntry> seriesPlaylists,
            IEnumerable<(string UiBgmId, string UiSeriesId)> soundTestSongs)
        {
            var stagePlaylists = stages
                .Where(s => !string.IsNullOrEmpty(s.UiSeriesId) && !string.IsNullOrEmpty(s.BgmSetId))
                .ToLookup(s => s.UiSeriesId, s => s.BgmSetId);
            return seriesPlaylists
                .SelectMany(p => p.Tracks.Select(t => new RequiredSong(p.Id, t.UiBgmId, t.Incidence0)))
                .Concat(soundTestSongs.SelectMany(song => stagePlaylists[song.UiSeriesId ?? ""].Prepend(SmashBattlePlaylist).Distinct()
                    .Select(id => new RequiredSong(id, song.UiBgmId, 100))))
                .ToList();
        }

        /// <summary>
        /// After the mods' playlists are merged in: playlists.toml playlists replace theirs, the required songs any playlist
        /// lacks join at its end, then playlists.toml stages get their playlists. Returns the songs added.
        /// </summary>
        public static int ApplyModPlaylists(IDictionary<string, PlaylistEntry> playlists, IEnumerable<StageEntry> stages,
            IEnumerable<RequiredSong> required, IEnumerable<PlaylistEntry> overrides, IEnumerable<StagePlaylistAssignment> assignments)
        {
            foreach (var playlist in overrides)
            {
                if (!playlists.TryGetValue(playlist.Id, out var existing))
                    playlists[playlist.Id] = existing = new PlaylistEntry(playlist.Id);
                existing.Title = playlist.Title ?? existing.Title;
                existing.Tracks = playlist.Tracks.ToList();
            }

            var added = 0;
            foreach (var song in required)
            {
                if (!playlists.TryGetValue(song.PlaylistId, out var playlist) || playlist.Tracks.Any(t => t.UiBgmId == song.UiBgmId)) continue;
                var order = playlist.Tracks.Count > 0 ? (short)(playlist.Tracks.Max(t => t.Order0) + 1) : (short)0;
                playlist.Tracks.Add(Track(song.UiBgmId, order, song.Incidence));
                added++;
            }

            var byStage = assignments.GroupBy(a => a.UiStageId).ToDictionary(g => g.Key, g => g.Last());
            foreach (var stage in stages)
            {
                if (!byStage.TryGetValue(stage.UiStageId, out var assignment)) continue;
                stage.BgmSetId = assignment.BgmSetId;
                stage.BgmSettingNo = assignment.BgmSettingNo;
            }
            return added;
        }

        private static PropertyInfo[] Properties(string prefix) =>
            Enumerable.Range(0, OrderCount).Select(i => typeof(PlaylistValueEntry).GetProperty(prefix + i)).ToArray();
    }
}
