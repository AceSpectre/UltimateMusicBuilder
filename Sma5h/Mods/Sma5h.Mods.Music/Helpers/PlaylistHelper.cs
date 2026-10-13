using Sma5h.Mods.Music.Models;
using Sma5h.Mods.Music.Models.PlaylistEntryModels;
using System;
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

        /// <summary>The playlists the build adds a mod song of a series to: bgmsmashbtl and the playlists of the series' stages.</summary>
        public static Func<string, ISet<string>> ModSongPlaylists(IEnumerable<StageEntry> stages)
        {
            var stagePlaylists = stages
                .Where(s => !string.IsNullOrEmpty(s.UiSeriesId) && !string.IsNullOrEmpty(s.BgmSetId))
                .ToLookup(s => s.UiSeriesId, s => s.BgmSetId);
            return uiSeriesId => stagePlaylists[uiSeriesId ?? ""].Prepend(SmashBattlePlaylist).ToHashSet();
        }

        /// <summary>Adds each mod song missing from its <see cref="ModSongPlaylists"/> at their end. Returns the insertions.</summary>
        public static int AddModSongs(IDictionary<string, PlaylistEntry> playlists, IEnumerable<StageEntry> stages,
            IEnumerable<(string UiBgmId, string UiSeriesId)> songs)
        {
            var targets = ModSongPlaylists(stages);
            var added = 0;
            foreach (var (uiBgmId, uiSeriesId) in songs)
            {
                foreach (var id in targets(uiSeriesId))
                {
                    if (!playlists.TryGetValue(id, out var playlist) || playlist.Tracks.Any(t => t.UiBgmId == uiBgmId)) continue;
                    var order = playlist.Tracks.Count > 0 ? (short)(playlist.Tracks.Max(t => t.Order0) + 1) : (short)0;
                    playlist.Tracks.Add(Track(uiBgmId, order, 100));
                    added++;
                }
            }
            return added;
        }

        /// <summary>playlists.toml playlists replace the ones with the same id. Applied before <see cref="AddModSongs"/>, so new mod songs still join.</summary>
        public static void ApplyPlaylists(IDictionary<string, PlaylistEntry> playlists, IEnumerable<PlaylistEntry> overrides)
        {
            foreach (var playlist in overrides)
            {
                if (!playlists.TryGetValue(playlist.Id, out var existing))
                    playlists[playlist.Id] = existing = new PlaylistEntry(playlist.Id);
                existing.Title = playlist.Title;
                existing.Tracks = playlist.Tracks.ToList();
            }
        }

        /// <summary>playlists.toml stages get new playlists. Applied after <see cref="AddModSongs"/>, which follows the game's stage playlists.</summary>
        public static void ApplyStages(IEnumerable<StageEntry> stages, IEnumerable<StagePlaylistAssignment> assignments)
        {
            var byStage = assignments.GroupBy(a => a.UiStageId).ToDictionary(g => g.Key, g => g.Last());
            foreach (var stage in stages)
            {
                if (!byStage.TryGetValue(stage.UiStageId, out var assignment)) continue;
                stage.BgmSetId = assignment.BgmSetId;
                stage.BgmSettingNo = assignment.BgmSettingNo;
            }
        }

        private static PropertyInfo[] Properties(string prefix) =>
            Enumerable.Range(0, OrderCount).Select(i => typeof(PlaylistValueEntry).GetProperty(prefix + i)).ToArray();
    }
}
