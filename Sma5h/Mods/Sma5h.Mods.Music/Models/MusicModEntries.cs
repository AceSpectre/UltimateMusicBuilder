using System.Collections.Generic;

namespace Sma5h.Mods.Music.Models
{
    public class MusicModEntries
    {
        public List<BgmDbRootEntry> BgmDbRootEntries { get; }
        public List<BgmAssignedInfoEntry> BgmAssignedInfoEntries { get; }
        public List<BgmStreamSetEntry> BgmStreamSetEntries { get; }
        public List<BgmStreamPropertyEntry> BgmStreamPropertyEntries { get; }
        public List<BgmPropertyEntry> BgmPropertyEntries { get; }
        public List<GameTitleEntry> GameTitleEntries { get; }
        public List<SeriesEntry> SeriesEntries { get; }
        public List<PlaylistEntry> PlaylistEntries { get; }
        public Dictionary<string, List<string>> SeriesSongOrderings { get; }
        /// <summary>Series dB boost by UiSeriesId.</summary>
        public Dictionary<string, float> SeriesVolumeDb { get; }
        public List<VanillaBgmOverride> VanillaBgmOverrides { get; }
        /// <summary>icon.png path by UiSeriesId, for vanilla series.</summary>
        public Dictionary<string, string> VanillaSeriesIcons { get; }
        /// <summary>playlists.toml playlists, which replace the playlists with the same id.</summary>
        public List<PlaylistEntry> PlaylistOverrides { get; }
        public List<StagePlaylistAssignment> StageAssignments { get; }

        public MusicModEntries()
        {
            BgmDbRootEntries = new List<BgmDbRootEntry>();
            BgmAssignedInfoEntries = new List<BgmAssignedInfoEntry>();
            BgmStreamSetEntries = new List<BgmStreamSetEntry>();
            BgmStreamPropertyEntries = new List<BgmStreamPropertyEntry>();
            BgmPropertyEntries = new List<BgmPropertyEntry>();
            GameTitleEntries = new List<GameTitleEntry>();
            SeriesEntries = new List<SeriesEntry>();
            PlaylistEntries = new List<PlaylistEntry>();
            SeriesSongOrderings = new Dictionary<string, List<string>>();
            SeriesVolumeDb = new Dictionary<string, float>();
            VanillaBgmOverrides = new List<VanillaBgmOverride>();
            VanillaSeriesIcons = new Dictionary<string, string>();
            PlaylistOverrides = new List<PlaylistEntry>();
            StageAssignments = new List<StagePlaylistAssignment>();
        }
    }
}
