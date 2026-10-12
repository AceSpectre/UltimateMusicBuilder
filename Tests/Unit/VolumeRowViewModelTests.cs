using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using UMB.CLI.Views;
using Xunit;

namespace Tests.Unit
{
    /// <summary>The dB sum behind the CLI Config Volume window.</summary>
    public class VolumeRowViewModelTests
    {
        [Fact]
        public void EffectiveDb_SumsGlobalAutoAndTrackBoosts()
        {
            var vm = new VolumeRowViewModel { GlobalDb = 3f, AutoGainDb = 6f, TrackDb = -2f };

            Assert.Equal(7f, vm.EffectiveDb, precision: 4);
            Assert.Equal(VolumeHelper.DbToMultiplier(7f), vm.EffectiveBankVolume, precision: 4);
        }

        [Fact]
        public void Sma5hMusicOptions_DefaultsToNoGlobalBoostAndTwelveDbMaxGain()
        {
            Assert.Equal(0f, new Sma5hMusicOptions.Sma5hMusicOptionsSection().GlobalVolumeDb);
            Assert.Equal(12f, new Sma5hMusicOptions.LufsNormalizationOptions().MaxGainDb);
        }
    }
}
