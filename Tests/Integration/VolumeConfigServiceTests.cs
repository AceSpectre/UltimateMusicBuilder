using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sma5h.Interfaces;
using Sma5h.Mods.Music;
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
    /// LufsAnalysisService.CalculateGain) and the composition of
    /// GlobalVolumeMultiplier × AutoGain × UserOverride is exposed on
    /// VolumeRowViewModel.EffectiveBankVolume. Both are exercised here.
    /// </summary>
    public class VolumeConfigServiceTests : IDisposable
    {
        private readonly TestEnvironment _env;

        public VolumeConfigServiceTests()
        {
            _env = new TestEnvironment();
        }

        public void Dispose() => _env.Dispose();

        private LufsAnalysisService CreateLufsService(Sma5hMusicOptions options = null)
        {
            options ??= new Sma5hMusicOptions
            {
                Sma5hMusic = new Sma5hMusicOptions.Sma5hMusicOptionsSection
                {
                    LufsNormalization = new Sma5hMusicOptions.LufsNormalizationOptions
                    {
                        TargetLufs = -14.0f,
                        MaxGainMultiplier = 4.0f,
                        Enabled = true
                    },
                    GlobalVolumeMultiplier = 1.5f
                }
            };
            var optMock = new Mock<IOptionsMonitor<Sma5hMusicOptions>>();
            optMock.Setup(m => m.CurrentValue).Returns(options);
            var processMock = new Mock<IProcessService>();
            var sp = new Mock<IServiceProvider>();
            var loggerMock = new Mock<ILogger<ILufsAnalysisService>>();
            return new LufsAnalysisService(optMock.Object, processMock.Object, sp.Object, loggerMock.Object);
        }

        // ── Gain formula ───────────────────────────────────────────────────

        [Theory]
        [InlineData(-20.0f, -14.0f, 4.0f, 1.995f)]  // +6 dB → ~2.0× (within clamp)
        [InlineData(-14.0f, -14.0f, 4.0f, 1.0f)]    //  0 dB → 1.0×
        [InlineData(-10.0f, -14.0f, 4.0f, 0.6309f)] // -4 dB → ~0.63×
        public void CalculateGain_AppliesTargetLufsFormula(
            float measured, float target, float maxMult, float expected)
        {
            var lufs = CreateLufsService();
            var measurement = new LufsMeasurement { IntegratedLufs = measured, IsValid = true };

            var gain = lufs.CalculateGain(measurement, target, maxMult);

            Assert.InRange(gain.Multiplier, expected - 0.01f, expected + 0.01f);
            Assert.False(gain.WasClamped);
        }

        [Fact]
        public void CalculateGain_ClampsAtMaxMultiplier()
        {
            var lufs = CreateLufsService();
            // measured -40 LUFS → target -14 → +26 dB → ~20× raw, clamped to 1.5
            var measurement = new LufsMeasurement { IntegratedLufs = -40.0f, IsValid = true };

            var gain = lufs.CalculateGain(measurement, targetLufs: -14.0f, maxMultiplier: 1.5f);

            Assert.Equal(1.5f, gain.Multiplier, precision: 3);
            Assert.True(gain.WasClamped);
        }

        [Fact]
        public void CalculateGain_InvalidMeasurementReturnsUnity()
        {
            var lufs = CreateLufsService();
            var measurement = new LufsMeasurement { IsValid = false };

            var gain = lufs.CalculateGain(measurement, targetLufs: -14.0f, maxMultiplier: 4.0f);

            Assert.Equal(1.0f, gain.Multiplier);
            Assert.False(gain.WasClamped);
        }

        // ── GlobalVolumeMultiplier composition (the "appsettings override") ─

        [Fact]
        public void EffectiveBankVolume_ComposesGlobalAutoAndUserOverride()
        {
            var vm = new VolumeRowViewModel
            {
                GlobalVolumeMultiplier = 1.5f,
                AutoGain = 2.0f,
                UserOverride = 0.5f
            };

            // Build pipeline applies effective = global * auto * user
            Assert.Equal(1.5f * 2.0f * 0.5f, vm.EffectiveBankVolume, precision: 4);
        }

        [Fact]
        public void EffectiveBankVolume_RespectsAppsettingsGlobalOverride()
        {
            // Default appsettings.json sets GlobalVolumeMultiplier = 1.5.
            // Override to 2.7 (which is what the bundled sma5h-test-mod uses) and
            // confirm the row model uses the overridden value verbatim.
            var customOptions = new Sma5hMusicOptions
            {
                Sma5hMusic = new Sma5hMusicOptions.Sma5hMusicOptionsSection
                {
                    GlobalVolumeMultiplier = 2.7f
                }
            };
            var globalMult = customOptions.Sma5hMusic.GlobalVolumeMultiplier;
            var vm = new VolumeRowViewModel
            {
                GlobalVolumeMultiplier = globalMult,
                AutoGain = 1.0f,
                UserOverride = 1.0f
            };

            Assert.Equal(2.7f, vm.EffectiveBankVolume, precision: 3);
        }

        [Fact]
        public void EffectiveBankVolume_AutoGainClampedFlowsThrough()
        {
            // Reproduce the full chain: LUFS gain calculation produces clamped
            // AutoGain, then composition multiplies global × clamped × user.
            var lufs = CreateLufsService();
            var measurement = new LufsMeasurement { IntegratedLufs = -50.0f, IsValid = true };
            var gain = lufs.CalculateGain(measurement, targetLufs: -14.0f, maxMultiplier: 1.5f);

            var vm = new VolumeRowViewModel
            {
                GlobalVolumeMultiplier = 1.5f,
                AutoGain = gain.Multiplier,
                UserOverride = 1.0f
            };

            Assert.True(gain.WasClamped);
            Assert.Equal(1.5f * 1.5f * 1.0f, vm.EffectiveBankVolume, precision: 4);
        }

        // ── Options plumbing ───────────────────────────────────────────────

        [Fact]
        public void Sma5hMusicOptions_DefaultGlobalVolumeMultiplierIsOnePointFive()
        {
            // Compile-time guard: bug surface if the option default ever changes
            // without updating callers / tests / appsettings.json simultaneously.
            var options = new Sma5hMusicOptions.Sma5hMusicOptionsSection();
            Assert.Equal(1.5f, options.GlobalVolumeMultiplier);
        }
    }
}
