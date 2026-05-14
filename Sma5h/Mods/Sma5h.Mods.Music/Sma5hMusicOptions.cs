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
            public string FfmpegPath { get; set; } = "Tools\\FFmpeg\\ffmpeg.exe";
            public string MeasurementCacheFile { get; set; } = "Cache\\lufs_measurements.json";
        }

        public enum PlaylistGeneration
        {
            Manual = 0,
            OnlyMissingSongs = 1,
            AllSongs = 2
        }
    }
}
