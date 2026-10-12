using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using Tomlyn;
using UMB.CLI.Desktop;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Unit.Desktop
{
    public class SongPresetsTomlTests
    {
        private const string Header = "[series]\nid = \"s\"\n\n[[games]]\nid = \"g1\"\nname = \"Game One\"\n";

        private static string[] Lines(string text) => text.Split('\n');

        private static string Write(string text, SongPresets presets) => string.Join("\n", SongPresetsToml.Write(Lines(text), presets));

        private static SongPresets Sample() => new()
        {
            SeriesDefault = new SongPreset { Game = "g1", Author = "Nobuo", Copyright = "Square", RecordType = "arrange", InSoundtest = "False" },
            GameDefaults = { new SongPreset { Game = "g1", Author = "Uematsu", PinchSong = "battle_pinch.nus3audio" } },
            Presets = { new SongPreset { Name = "Remix \"B\"", Game = "g1", RecordType = "new_arrange", InSoundtest = "True" } }
        };

        [Fact]
        public void Write_ThenRead_RoundTripsEveryPreset()
        {
            var read = SongPresetsToml.Read(Write(Header, Sample()));

            Assert.Equivalent(Sample(), read, strict: true);
        }

        [Fact]
        public void Write_OnlyWritesTheKeysAPresetSets()
        {
            var toml = Write(Header, Sample());

            Assert.Contains("[[game-defaults]]\ngame = \"g1\"\nauthor = \"Uematsu\"\npinch-song = \"battle_pinch.nus3audio\"\n", toml);
            Assert.Contains("[[presets]]\nname = \"Remix \\\"B\\\"\"\ngame = \"g1\"\nrecord-type = \"new_arrange\"\nin-soundtest = true", toml);
            Assert.Contains("in-soundtest = false", toml);
        }

        [Fact]
        public void Write_DropsGameDefaultsThatSetNothing()
        {
            var presets = new SongPresets { GameDefaults = { new SongPreset { Game = "g1" } } };

            Assert.DoesNotContain("[[game-defaults]]", Write(Header + "\n[[game-defaults]]\ngame = \"g1\"\nauthor = \"x\"\n", presets));
        }

        [Fact]
        public void Write_KeepsVolumeAndOtherTables_AndPutsNewKeysInsideTheTable()
        {
            var toml = Write(Header + "\n[default-track-data]\nvolume-db = -2.5\n\n[[playlists]]\nid = \"bgmmario\"\n", Sample());

            Assert.Contains("[default-track-data]\nvolume-db = -2.5\ngame = \"g1\"", toml);
            Assert.Contains("[[playlists]]\nid = \"bgmmario\"", toml);
            Assert.Contains("[[games]]", toml);
        }

        [Fact]
        public void Write_CreatesTheSeriesDefaultTableOnlyForNonDefaultValues()
        {
            Assert.DoesNotContain("[default-track-data]", Write(Header, new SongPresets()));
            Assert.Contains("[default-track-data]", Write(Header, new SongPresets { SeriesDefault = { InSoundtest = "False" } }));
        }

        [Fact]
        public void Read_FillsSeriesDefaultFallbacks_AndLeavesPresetKeysEmpty()
        {
            var read = SongPresetsToml.Read(Header + "\n[[presets]]\nname = \"Blank\"\n");

            Assert.Equal(("original", "True"), (read.SeriesDefault.RecordType, read.SeriesDefault.InSoundtest));
            Assert.Equal(("", ""), (read.Presets[0].RecordType, read.Presets[0].InSoundtest));
        }

        [Fact]
        public void WrittenPresets_StillParseAsASeriesFile()
        {
            var options = CliUtil.KebabTomlOptions();

            var config = Toml.ToModel<FolderSeriesFileConfig>(Write(Header, Sample()), options: options);

            Assert.Equal(("battle_pinch.nus3audio", "Remix \"B\""), (config.GameDefaults[0].PinchSong, config.Presets[0].Name));
            Assert.False(config.DefaultTrackData.InSoundtest);
        }
    }
}
