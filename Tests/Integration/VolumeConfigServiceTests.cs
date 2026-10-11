using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sma5h.Interfaces;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Services;
using Tests.Helpers;
using UMB.CLI.Views;
using Xunit;

namespace Tests.Integration
{
    /// <summary>
    /// Tests the gain/volume math used by VolumeConfigService.
    ///
    /// VolumeConfigService.Run() launches an Avalonia window so it can't be
    /// invoked from a test, but the gain calculation is pure (in
    /// LufsAnalysisService.CalculateGain) and the sum of
    /// GlobalDb + AutoGainDb + TrackDb is exposed on VolumeRowViewModel.
    /// Both are exercised here.
    /// </summary>
    public class VolumeConfigServiceTests : IDisposable
    {
        private readonly TestEnvironment _env;

        public VolumeConfigServiceTests()
        {
            _env = new TestEnvironment();
        }

        public void Dispose() => _env.Dispose();

        private LufsAnalysisService CreateLufsService()
        {
            var options = new Sma5hMusicOptions
            {
                Sma5hMusic = new Sma5hMusicOptions.Sma5hMusicOptionsSection
                {
                    LufsNormalization = new Sma5hMusicOptions.LufsNormalizationOptions
                    {
                        TargetLufs = -14.0f,
                        MaxGainDb = 12.0f,
                        Enabled = true
                    }
                }
            };
            var optMock = new Mock<IOptionsMonitor<Sma5hMusicOptions>>();
            optMock.Setup(m => m.CurrentValue).Returns(options);
            return new LufsAnalysisService(optMock.Object, new Mock<IProcessService>().Object,
                new Mock<IServiceProvider>().Object, new Mock<ILogger<ILufsAnalysisService>>().Object);
        }

        [Fact]
        public void EffectiveDb_SumsGlobalAutoAndTrackBoosts()
        {
            var vm = new VolumeRowViewModel { GlobalDb = 3f, AutoGainDb = 6f, TrackDb = -2f };

            Assert.Equal(7f, vm.EffectiveDb, precision: 4);
            Assert.Equal(VolumeHelper.DbToMultiplier(7f), vm.EffectiveBankVolume, precision: 4);
        }

        [Fact]
        public void EffectiveDb_AutoGainClampFlowsThrough()
        {
            var gain = CreateLufsService().CalculateGain(
                new LufsMeasurement { IntegratedLufs = -50.0f, IsValid = true }, targetLufs: -14.0f, maxGainDb: 3.5f);

            var vm = new VolumeRowViewModel { GlobalDb = 1f, AutoGainDb = gain.Db };

            Assert.True(gain.WasClamped);
            Assert.Equal(4.5f, vm.EffectiveDb, precision: 4);
        }

        [Fact]
        public void Sma5hMusicOptions_DefaultsToNoGlobalBoostAndTwelveDbMaxGain()
        {
            Assert.Equal(0f, new Sma5hMusicOptions.Sma5hMusicOptionsSection().GlobalVolumeDb);
            Assert.Equal(12f, new Sma5hMusicOptions.LufsNormalizationOptions().MaxGainDb);
        }
    }
}
