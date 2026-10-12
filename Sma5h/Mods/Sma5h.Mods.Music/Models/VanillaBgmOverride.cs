namespace Sma5h.Mods.Music.Models
{
    /// <summary>Edits to a vanilla song, from a tracks.csv row whose filename is the song's ui_bgm id.</summary>
    public class VanillaBgmOverride
    {
        public string UiBgmId { get; set; }
        public string UiGameTitleId { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string Copyright { get; set; }
        public string RecordType { get; set; }
        public string SpecialCategory { get; set; }
        public string Info1 { get; set; }
        public bool InSoundtest { get; set; } = true;
        public float VolumeDb { get; set; }
    }
}
