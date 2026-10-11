using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sma5h.Interfaces;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Services;
using Tests.Helpers;
using Xunit;

namespace Tests.Unit
{
    /// <summary>
    /// The gain formula and the loudnorm-JSON parser — the two pure pieces of
    /// LufsAnalysisService. CalculateGain is public; the ComputeGainDb clamp is
    /// reached through it. ParseLoudnormJson/ParseFloat
    /// are private and reached via reflection.
    /// </summary>
    public class LufsGainTests
    {
        private static LufsAnalysisService CreateService()
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
                    },
                    GlobalVolumeDb = 3.5f
                }
            };
            var optMock = new Mock<IOptionsMonitor<Sma5hMusicOptions>>();
            optMock.Setup(m => m.CurrentValue).Returns(options);
            return new LufsAnalysisService(
                optMock.Object,
                new Mock<IProcessService>().Object,
                new Mock<IServiceProvider>().Object,
                new Mock<ILogger<ILufsAnalysisService>>().Object);
        }

        // ── CalculateGain / ComputeGainDb ───────────────────────────────────

        [Theory]
        [InlineData(-20.0f, -14.0f, 12.0f, 6.0f)]
        [InlineData(-14.0f, -14.0f, 12.0f, 0.0f)]
        [InlineData(-10.0f, -14.0f, 12.0f, -4.0f)]
        public void CalculateGain_AppliesFormula(float measured, float target, float maxDb, float expected)
        {
            var gain = CreateService().CalculateGain(
                new LufsMeasurement { IntegratedLufs = measured, IsValid = true }, target, maxDb);

            Assert.Equal(expected, gain.Db, precision: 3);
            Assert.False(gain.WasClamped);
        }

        [Fact]
        public void CalculateGain_ClampsAtMax()
        {
            var gain = CreateService().CalculateGain(
                new LufsMeasurement { IntegratedLufs = -40.0f, IsValid = true }, -14.0f, 3.5f);

            Assert.Equal(3.5f, gain.Db, precision: 3);
            Assert.True(gain.WasClamped);
        }

        [Fact]
        public void CalculateGain_InvalidMeasurementReturnsZero()
        {
            var gain = CreateService().CalculateGain(
                new LufsMeasurement { IsValid = false }, -14.0f, 12.0f);
            Assert.Equal(0f, gain.Db);
            Assert.False(gain.WasClamped);
        }

        [Fact]
        public void CalculateGain_NullMeasurementReturnsZero()
        {
            var gain = CreateService().CalculateGain(null, -14.0f, 12.0f);
            Assert.Equal(0f, gain.Db);
            Assert.False(gain.WasClamped);
        }

        [Fact]
        public void CalculateGain_NearSilentSourceIsClampedToMax()
        {
            var gain = CreateService().CalculateGain(
                new LufsMeasurement { IntegratedLufs = -1000f, IsValid = true }, 0.0f, 12.0f);

            Assert.Equal(12.0f, gain.Db, precision: 3);
            Assert.True(gain.WasClamped);
        }

        // ── ParseLoudnormJson / ParseFloat (private, via reflection) ────────

        private static LufsMeasurement ParseJson(string stderr)
            => Reflect.InvokeInstance<LufsMeasurement>(
                CreateService(), "ParseLoudnormJson", stderr, "track.wav");

        [Fact]
        public void ParseLoudnormJson_ParsesValidBlock()
        {
            var stderr =
                "ffmpeg noise line\n" +
                "[Parsed_loudnorm_0 @ 0x0] \n" +
                "{\n  \"input_i\" : \"-18.50\",\n  \"input_tp\" : \"-2.10\",\n  \"input_lra\" : \"5.40\"\n}\n";

            var m = ParseJson(stderr);

            Assert.True(m.IsValid);
            Assert.Equal(-18.5f, m.IntegratedLufs, precision: 2);
            Assert.Equal(-2.1f, m.TruePeakDb, precision: 2);
            Assert.Equal(5.4f, m.LoudnessRangeLu, precision: 2);
        }

        [Fact]
        public void ParseLoudnormJson_NoJsonBlockIsInvalid()
        {
            var m = ParseJson("ffmpeg emitted no json here");
            Assert.False(m.IsValid);
        }

        [Fact]
        public void ParseLoudnormJson_NonNumericIntegratedIsInvalid()
        {
            var m = ParseJson("{ \"input_i\" : \"-inf\", \"input_tp\" : \"-1.0\" }");
            Assert.False(m.IsValid);
        }

        [Fact]
        public void ParseLoudnormJson_MissingOptionalFieldsDefaultToZero()
        {
            var m = ParseJson("{ \"input_i\" : \"-12.0\" }");
            Assert.True(m.IsValid);
            Assert.Equal(-12.0f, m.IntegratedLufs, precision: 2);
            Assert.Equal(0f, m.TruePeakDb);
            Assert.Equal(0f, m.LoudnessRangeLu);
        }
    }
}
