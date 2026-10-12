using Moq;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Models;
using Xunit;

namespace Tests.Unit
{
    public class VanillaVolumeHelperTests
    {
        /// <summary>Two vanilla songs (mario, zelda series) and one modded song in the mario series.</summary>
        private static IAudioStateService State()
        {
            var mod = new Mock<IMusicMod>().Object;
            var state = new Mock<IAudioStateService>();
            state.Setup(s => s.GetGameTitleEntries()).Returns(new[]
            {
                new GameTitleEntry("ui_gametitle_smb") { UiSeriesId = "ui_series_mario" },
                new GameTitleEntry("ui_gametitle_loz") { UiSeriesId = "ui_series_zelda" }
            });
            state.Setup(s => s.GetBgmDbRootEntries()).Returns(new[]
            {
                new BgmDbRootEntry("ui_bgm_a") { StreamSetId = "set_a", UiGameTitleId = "ui_gametitle_smb" },
                new BgmDbRootEntry("ui_bgm_b") { StreamSetId = "set_b", UiGameTitleId = "ui_gametitle_loz" },
                new BgmDbRootEntry("ui_bgm_m", mod) { StreamSetId = "set_m", UiGameTitleId = "ui_gametitle_smb" }
            });
            state.Setup(s => s.GetBgmStreamSetEntries()).Returns(new[]
            {
                new BgmStreamSetEntry("set_a") { Info0 = "info_a" },
                new BgmStreamSetEntry("set_b") { Info0 = "info_b" },
                new BgmStreamSetEntry("set_m", mod) { Info0 = "info_m" }
            });
            state.Setup(s => s.GetBgmAssignedInfoEntries()).Returns(new[]
            {
                new BgmAssignedInfoEntry("info_a") { StreamId = "stream_a" },
                new BgmAssignedInfoEntry("info_b") { StreamId = "stream_b" },
                new BgmAssignedInfoEntry("info_m", mod) { StreamId = "stream_m" }
            });
            state.Setup(s => s.GetBgmStreamPropertyEntries()).Returns(new[]
            {
                new BgmStreamPropertyEntry("stream_a") { DataName0 = "a" },
                new BgmStreamPropertyEntry("stream_b") { DataName0 = "b" },
                new BgmStreamPropertyEntry("stream_m", mod) { DataName0 = "m" }
            });
            state.Setup(s => s.GetBgmPropertyEntries()).Returns(new[]
            {
                new BgmPropertyEntry("a", "a.nus3audio"),
                new BgmPropertyEntry("b", "b.nus3audio"),
                new BgmPropertyEntry("m", "m.nus3audio", mod)
            });
            return state.Object;
        }

        private static readonly Dictionary<string, float> NoTrackDb = new();

        [Fact]
        public void BoostsByToneId_AddsTheSeriesBoostToTheGlobalBoost_ForVanillaSongsOnly()
        {
            var boosts = VanillaVolumeHelper.BoostsByToneId(State(), 1f, new Dictionary<string, float> { ["ui_series_mario"] = -3f }, NoTrackDb, true);

            Assert.Equal(new Dictionary<string, float> { ["a"] = -2f, ["b"] = 1f }, boosts);
        }

        [Fact]
        public void BoostsByToneId_LeavesOutSongsWithNoBoost()
        {
            var boosts = VanillaVolumeHelper.BoostsByToneId(State(), 0f, new Dictionary<string, float> { ["ui_series_zelda"] = 2f }, NoTrackDb, true);

            Assert.Equal(new Dictionary<string, float> { ["b"] = 2f }, boosts);
        }

        [Fact]
        public void BoostsByToneId_AppliesOnlyTrackBoostsWhenGlobalAndSeriesAreOff()
        {
            var boosts = VanillaVolumeHelper.BoostsByToneId(State(), 1f, new Dictionary<string, float> { ["ui_series_mario"] = -3f },
                new Dictionary<string, float> { ["ui_bgm_b"] = 4f, ["ui_bgm_m"] = 5f }, false);

            Assert.Equal(new Dictionary<string, float> { ["b"] = 4f }, boosts);
        }

        [Fact]
        public void BoostsByToneId_AddsTrackBoostsToGlobalAndSeries()
        {
            var boosts = VanillaVolumeHelper.BoostsByToneId(State(), 1f, new Dictionary<string, float> { ["ui_series_mario"] = -3f },
                new Dictionary<string, float> { ["ui_bgm_a"] = 2f }, true);

            Assert.Equal(new Dictionary<string, float> { ["b"] = 1f }, boosts);
        }
    }
}
