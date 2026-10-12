using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Models;
using Xunit;

namespace Tests.Unit
{
    public class VanillaOverrideHelperTests
    {
        private readonly BgmDbRootEntry _song = new("ui_bgm_a")
        {
            StreamSetId = "set_a",
            UiGameTitleId = "ui_gametitle_smb",
            RecordType = "record_original",
            TestDispOrder = 4,
            Title = new() { ["us_en"] = "Ground Theme", ["jp_ja"] = "地上BGM" },
            Author = new() { ["us_en"] = "Koji Kondo" },
            Copyright = new()
        };
        private readonly BgmStreamSetEntry _set = new("set_a") { SpecialCategory = "", Info1 = "" };
        private readonly SeriesEntry _series = new("ui_series_mario");
        private readonly IAudioStateService _state;

        public VanillaOverrideHelperTests()
        {
            var mod = new Mock<IMusicMod>().Object;
            var state = new Mock<IAudioStateService>();
            state.Setup(s => s.GetBgmDbRootEntries()).Returns(new[] { _song, new BgmDbRootEntry("ui_bgm_m", mod) });
            state.Setup(s => s.GetBgmStreamSetEntries()).Returns(new[] { _set });
            state.Setup(s => s.GetSeriesEntries()).Returns(new[] { _series, new SeriesEntry("ui_series_zelda") });
            _state = state.Object;
        }

        private static VanillaBgmOverride Edit(string uiBgmId = "ui_bgm_a") => new()
        {
            UiBgmId = uiBgmId,
            UiGameTitleId = "ui_gametitle_smb",
            Title = "Ground Theme",
            Author = "Koji Kondo",
            Copyright = "",
            RecordType = "record_original",
            SpecialCategory = "",
            Info1 = ""
        };

        private void Apply(VanillaBgmOverride edit) => VanillaOverrideHelper.Apply(_state, new[] { edit }, "en_us", NullLogger.Instance);

        [Fact]
        public void Apply_KeepsTranslationsOfUnchangedTexts()
        {
            Apply(Edit());

            Assert.Equal(2, _song.Title.Count);
            Assert.Equal("Koji Kondo", _song.Author["us_en"]);
            Assert.Empty(_song.Copyright);
        }

        [Fact]
        public void Apply_ReplacesEveryTranslationOfAChangedText()
        {
            var edit = Edit();
            edit.Title = "Overworld";
            edit.Copyright = "Nintendo";
            Apply(edit);

            Assert.Equal(new Dictionary<string, string> { ["en_us"] = "Overworld" }, _song.Title);
            Assert.Equal(new Dictionary<string, string> { ["en_us"] = "Nintendo" }, _song.Copyright);
        }

        [Fact]
        public void Apply_SetsRecordTypeGameAndPinchSong()
        {
            var edit = Edit();
            edit.RecordType = "record_arrange";
            edit.UiGameTitleId = "ui_gametitle_new";
            edit.SpecialCategory = "sf_situationlink";
            edit.Info1 = "info_b";
            Apply(edit);

            Assert.Equal(("record_arrange", "ui_gametitle_new"), (_song.RecordType, _song.UiGameTitleId));
            Assert.Equal(("sf_situationlink", "info_b"), (_set.SpecialCategory, _set.Info1));
        }

        [Fact]
        public void Apply_HidesTheSongFromSoundTestOnlyWhenUnticked()
        {
            Apply(Edit());
            Assert.Equal(4, _song.TestDispOrder);

            var edit = Edit();
            edit.InSoundtest = false;
            Apply(edit);
            Assert.Equal(-1, _song.TestDispOrder);
        }

        [Fact]
        public void Apply_SkipsModSongsAndUnknownIds()
        {
            Apply(Edit("ui_bgm_m"));
            Apply(Edit("ui_bgm_missing"));

            Assert.Equal("Ground Theme", _song.Title["us_en"]);
        }

        [Fact]
        public void ApplySeriesIcons_SetsTheIconOfListedSeries()
        {
            VanillaOverrideHelper.ApplySeriesIcons(_state, new Dictionary<string, string> { ["ui_series_mario"] = "mario/icon.png" });

            Assert.Equal("mario/icon.png", _series.IconPath);
        }
    }
}
