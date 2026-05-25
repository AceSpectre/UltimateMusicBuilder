using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Collections.Generic;

namespace Sma5h.Mods.Music
{
    public class Sma5hMusicOptions : Sma5hOptions
    {
        public Sma5hMusicOptionsSection Sma5hMusic { get; set; }

        public class Sma5hMusicOptionsSection
        {
            public Sma5hMusicOptionsAutoPlaylistsSection PlaylistMapping { get; set; }
            public bool EnableAudioCaching { get; set; }
            public string AudioConversionFormat { get; set; }
            public string AudioConversionFormatFallBack { get; set; }
            public string DefaultLocale { get; set; }
            public string ModPath { get; set; }
            public string CachePath { get; set; }
            public float GlobalVolumeMultiplier { get; set; } = 1.5f;
            public LufsNormalizationOptions LufsNormalization { get; set; } = new();
        }

        public class Sma5hMusicOptionsAutoPlaylistsSection
        {
            [JsonConverter(typeof(StringEnumConverter))]
            public PlaylistGeneration GenerationMode { get; set; }
            public ushort AutoMappingIncidence { get; set; }
            public Dictionary<string, string> AutoMapping { get; set; }
        }

        public class LufsNormalizationOptions
        {
            public bool Enabled { get; set; } = true;
            public float TargetLufs { get; set; } = -14.0f;
            public float MaxGainMultiplier { get; set; } = 4.0f;
            // ffmpeg is resolved from the system PATH only (install via choco/brew/apt).
            // Filename for the per-series cache file. Location is always derived from the
            // audio file's directory — every series folder gets its own copy alongside tracks.csv.
            public string LufsCacheFileName { get; set; } = "LUFS.csv";
        }

        public enum PlaylistGeneration
        {
            Manual = 0,
            OnlyMissingSongs = 1,
            AllSongs = 2
        }
    }
}
