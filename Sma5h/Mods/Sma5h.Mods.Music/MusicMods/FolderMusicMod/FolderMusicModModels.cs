using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace Sma5h.Mods.Music.MusicMods.FolderMusicMod
{
    // ── TOML models (Tomlyn POCO mapping) ────────────────────────────────────

    public class FolderSeriesConfig
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int PlaylistIncidence { get; set; } = 100;
        public bool ExistingSeries { get; set; }
    }

    public class FolderGameConfig
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class FolderPlaylistOverrideConfig
    {
        public string Id { get; set; }
        public int Incidence { get; set; } = 100;
    }

    public class FolderDefaultTrackDataConfig
    {
        public string Game { get; set; }
        public string Author { get; set; }
        public string Copyright { get; set; }
        public string RecordType { get; set; } = "original";
        public float Volume { get; set; } = 2.7f;
    }

    /// <summary>Root POCO for series.toml deserialization.</summary>
    public class FolderSeriesFileConfig
    {
        public FolderSeriesConfig Series { get; set; }
        public List<FolderGameConfig> Games { get; set; } = new();
        public List<FolderPlaylistOverrideConfig> Playlists { get; set; } = new();
        public FolderDefaultTrackDataConfig DefaultTrackData { get; set; }
    }

    // ── CSV models (CsvHelper mapping) ───────────────────────────────────────

    public class FolderTrackCsvRow
    {
        public string Filename { get; set; }
        public string Game { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string Copyright { get; set; }
        public string RecordType { get; set; }
        public string SpecialCategory { get; set; }
        public float Volume { get; set; } = 2.7f;
        public string Info1 { get; set; }
    }

    public class FolderTrackCsvRowMap : ClassMap<FolderTrackCsvRow>
    {
        public FolderTrackCsvRowMap()
        {
            Map(m => m.Filename).Name("filename");
            Map(m => m.Game).Name("game");
            Map(m => m.Title).Name("title");
            Map(m => m.Author).Name("author").Optional();
            Map(m => m.Copyright).Name("copyright").Optional();
            Map(m => m.RecordType).Name("record_type").Optional().Default("original");
            Map(m => m.SpecialCategory).Name("special_category").Optional();
            Map(m => m.Volume).Name("volume").Optional().Default(2.7f);
            Map(m => m.Info1).Name("info1").Optional().Default("");
        }
    }

    // ── MusicModInformation subclass ─────────────────────────────────────────

    /// <summary>
    /// MusicModInformation for folder-based mods. Overrides the read-only Id
    /// property since there is no metadata_mod.json to read it from.
    /// </summary>
    public class FolderMusicModInformation : Models.MusicModInformation
    {
        private readonly string _id;

        [JsonIgnore]
        public override string Id => _id;

        public FolderMusicModInformation(string id, string name)
        {
            _id = id;
            Name = name;
            Version = 1;
        }
    }
}
