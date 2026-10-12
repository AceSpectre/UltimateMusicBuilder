using System.Text.Json;
using Moq;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using Tests.Helpers;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Integration
{
    /// <summary>
    /// Drives the three non-interactive VolumeConfigService entry points the desktop
    /// app calls — <see cref="VolumeConfigService.RunSaveBatch"/>,
    /// <see cref="VolumeConfigService.RunAnalyzeBatch"/> and
    /// <see cref="VolumeConfigService.RunPreviewBatch"/>. The LUFS and audio-decode
    /// services are mocked, so these run without ffmpeg/vgmstream.
    /// </summary>
    public class VolumeConfigBatchTests : IDisposable
    {
        private readonly TestEnvironment _env;

        public VolumeConfigBatchTests()
        {
            _env = new TestEnvironment();
        }

        public void Dispose() => _env.Dispose();

        private VolumeConfigService CreateService(
            Mock<ILufsAnalysisService> lufs = null,
            Mock<IAudioDecodeService> decode = null)
        {
            return new VolumeConfigService(
                _env.CreateMusicOptions(),
                (lufs ?? new Mock<ILufsAnalysisService>()).Object,
                (decode ?? new Mock<IAudioDecodeService>()).Object,
                TestEnvironment.CreateLogger<VolumeConfigService>());
        }

        private string WriteJson(object input)
        {
            var path = Path.Combine(_env.TempDir, "vol-batch-" + Guid.NewGuid().ToString("N")[..8] + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(input));
            return path;
        }

        /// <summary>Creates a series folder with a tracks.csv and the named source files on disk.</summary>
        private string SetupSeries(string seriesName, params (string filename, string volume)[] tracks)
        {
            var seriesDir = Path.Combine(_env.ModPath, "test-mod", seriesName);
            Directory.CreateDirectory(seriesDir);

            var csv = new System.Text.StringBuilder();
            csv.AppendLine("filename,title,volume_db");
            foreach (var (filename, volume) in tracks)
            {
                csv.AppendLine($"{filename},{Path.GetFileNameWithoutExtension(filename)},{volume}");
                File.WriteAllBytes(Path.Combine(seriesDir, filename), new byte[] { 0x01, 0x02 });
            }
            File.WriteAllText(Path.Combine(seriesDir, "tracks.csv"), csv.ToString());
            return seriesDir;
        }

        private static Mock<ILufsAnalysisService> ValidLufsMock(
            float lufs = -20.0f, float gain = 2.0f, bool available = true)
        {
            var mock = new Mock<ILufsAnalysisService>();
            var measurement = new LufsMeasurement { IntegratedLufs = lufs, IsValid = true };
            mock.Setup(m => m.Measure(It.IsAny<string>())).Returns(measurement);
            mock.Setup(m => m.MeasureCached(It.IsAny<string>())).Returns(measurement);
            mock.Setup(m => m.CalculateGain(It.IsAny<LufsMeasurement>(), It.IsAny<float>(), It.IsAny<float>()))
                .Returns(new GainResult(VolumeHelper.MultiplierToDb(gain), false));
            mock.Setup(m => m.IsAvailable).Returns(available);
            return mock;
        }

        private static VolumeAnalyzeResultDto ReadResult(string path)
        {
            return JsonSerializer.Deserialize<VolumeAnalyzeResultDto>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        // ── RunSaveBatch ────────────────────────────────────────────────────

        [Fact]
        public void RunSaveBatch_AppliesOverridesToCorrectRows()
        {
            var seriesDir = SetupSeries("dev", ("a.nus3audio", "0"), ("b.nus3audio", "0"));
            var json = WriteJson(new
            {
                seriesPath = seriesDir,
                overrides = new[]
                {
                    new { originalIndex = 0, db = 1.5f },
                    new { originalIndex = 1, db = -3f }
                }
            });

            CreateService().RunSaveBatch(json);

            var csv = File.ReadAllText(Path.Combine(seriesDir, "tracks.csv"));
            Assert.Contains("a.nus3audio,a,1.5", csv);
            Assert.Contains("b.nus3audio,b,-3.0", csv);
        }

        [Fact]
        public void RunSaveBatch_WritesTheSeriesVolume_AndAnalyzeReadsItBack()
        {
            var seriesDir = SetupSeries("dev", ("a.nus3audio", "0"));
            File.WriteAllText(Path.Combine(seriesDir, "series.toml"), "[series]\nid = \"dev\"\n");

            CreateService().RunSaveBatch(WriteJson(new { seriesPath = seriesDir, overrides = Array.Empty<object>(), seriesDb = -2.5 }));

            var outputPath = Path.Combine(_env.TempDir, "analyze-out.json");
            CreateService(ValidLufsMock()).RunAnalyzeBatch(WriteJson(new { seriesPath = seriesDir, outputPath }));
            Assert.Equal(-2.5, ReadResult(outputPath).SeriesDb);
        }

        [Fact]
        public void RunSaveBatch_AddsVolumeDbColumnWhenAbsent()
        {
            var seriesDir = Path.Combine(_env.ModPath, "test-mod", "dev");
            Directory.CreateDirectory(seriesDir);
            File.WriteAllText(Path.Combine(seriesDir, "tracks.csv"),
                "filename,title\na.nus3audio,a\n");
            var json = WriteJson(new
            {
                seriesPath = seriesDir,
                overrides = new[] { new { originalIndex = 0, db = 2.0f } }
            });

            CreateService().RunSaveBatch(json);

            var csv = File.ReadAllText(Path.Combine(seriesDir, "tracks.csv"));
            Assert.Contains("volume_db", csv.Split('\n')[0]);
            Assert.Contains("a.nus3audio,a,2.0", csv);
        }

        [Fact]
        public void RunSaveBatch_IgnoresOutOfRangeIndices()
        {
            var seriesDir = SetupSeries("dev", ("a.nus3audio", "0"));
            var json = WriteJson(new
            {
                seriesPath = seriesDir,
                overrides = new[]
                {
                    new { originalIndex = 99, db = 9.9f },
                    new { originalIndex = -1, db = 9.9f },
                    new { originalIndex = 0, db = 3.0f }
                }
            });

            var ex = Record.Exception(() => CreateService().RunSaveBatch(json));

            Assert.Null(ex);
            var csv = File.ReadAllText(Path.Combine(seriesDir, "tracks.csv"));
            Assert.Contains("a.nus3audio,a,3.0", csv);
            Assert.DoesNotContain("9.9", csv);
        }

        [Fact]
        public void RunSaveBatch_MissingSeriesPath_ReturnsWithoutThrowing()
        {
            var json = WriteJson(new { overrides = new[] { new { originalIndex = 0, db = 1.0f } } });
            var ex = Record.Exception(() => CreateService().RunSaveBatch(json));
            Assert.Null(ex);
        }

        [Fact]
        public void RunSaveBatch_MissingCsv_ReturnsWithoutThrowing()
        {
            var seriesDir = Path.Combine(_env.ModPath, "test-mod", "dev");
            Directory.CreateDirectory(seriesDir);
            var json = WriteJson(new
            {
                seriesPath = seriesDir,
                overrides = new[] { new { originalIndex = 0, db = 1.0f } }
            });

            var ex = Record.Exception(() => CreateService().RunSaveBatch(json));
            Assert.Null(ex);
        }

        // ── RunAnalyzeBatch ─────────────────────────────────────────────────

        [Fact]
        public void RunAnalyzeBatch_Analyze_WritesMeasuredResult()
        {
            var seriesDir = SetupSeries("dev", ("a.nus3audio", "0"), ("b.nus3audio", "0"));
            var outputPath = Path.Combine(_env.TempDir, "analyze-out.json");
            var lufs = ValidLufsMock(lufs: -18.0f, gain: 2.5f, available: true);
            var json = WriteJson(new { seriesPath = seriesDir, outputPath, analyze = true });

            CreateService(lufs).RunAnalyzeBatch(json);

            var result = ReadResult(outputPath);
            Assert.Equal("dev", result.SeriesName);
            Assert.True(result.FfmpegAvailable);
            Assert.Equal(MusicConstants.DEFAULT_NUS3BANK_VOLUME_DB, result.BankBaseDb);
            Assert.Equal(2, result.Items.Count);
            Assert.All(result.Items, item =>
            {
                Assert.True(item.HasMeasurement);
                Assert.Equal(-18.0f, item.MeasuredLufs);
                Assert.Equal(VolumeHelper.MultiplierToDb(2.5f), item.AutoGainDb, precision: 4);
            });
            lufs.Verify(m => m.Measure(It.IsAny<string>()), Times.Exactly(2));
            lufs.Verify(m => m.SaveCache(), Times.Once);
        }

        [Fact]
        public void RunAnalyzeBatch_ReadOnly_UsesCachedAndDoesNotSave()
        {
            var seriesDir = SetupSeries("dev", ("a.nus3audio", "0"));
            var outputPath = Path.Combine(_env.TempDir, "analyze-out.json");
            var lufs = ValidLufsMock();
            var json = WriteJson(new { seriesPath = seriesDir, outputPath, analyze = false });

            CreateService(lufs).RunAnalyzeBatch(json);

            lufs.Verify(m => m.MeasureCached(It.IsAny<string>()), Times.Once);
            lufs.Verify(m => m.Measure(It.IsAny<string>()), Times.Never);
            lufs.Verify(m => m.SaveCache(), Times.Never);
        }

        [Fact]
        public void RunAnalyzeBatch_MissingSourceFile_ReportsNoMeasurement()
        {
            // tracks.csv references a file that isn't on disk.
            var seriesDir = Path.Combine(_env.ModPath, "test-mod", "dev");
            Directory.CreateDirectory(seriesDir);
            File.WriteAllText(Path.Combine(seriesDir, "tracks.csv"),
                "filename,title,volume_db\nghost.nus3audio,ghost,0\n");
            var outputPath = Path.Combine(_env.TempDir, "analyze-out.json");
            var json = WriteJson(new { seriesPath = seriesDir, outputPath, analyze = true });

            CreateService(ValidLufsMock()).RunAnalyzeBatch(json);

            var result = ReadResult(outputPath);
            Assert.Single(result.Items);
            Assert.False(result.Items[0].HasMeasurement);
            Assert.Equal(0f, result.Items[0].AutoGainDb);
        }

        [Fact]
        public void RunAnalyzeBatch_NoTracksCsv_WritesEmptyResult()
        {
            var seriesDir = Path.Combine(_env.ModPath, "test-mod", "dev");
            Directory.CreateDirectory(seriesDir);
            var outputPath = Path.Combine(_env.TempDir, "analyze-out.json");
            var json = WriteJson(new { seriesPath = seriesDir, outputPath, analyze = true });

            CreateService(ValidLufsMock()).RunAnalyzeBatch(json);

            var result = ReadResult(outputPath);
            Assert.Equal("dev", result.SeriesName);
            Assert.Empty(result.Items);
        }

        [Fact]
        public void RunAnalyzeBatch_MissingOutputPath_WritesNothing()
        {
            var seriesDir = SetupSeries("dev", ("a.nus3audio", "0"));
            var json = WriteJson(new { seriesPath = seriesDir, analyze = true }); // no outputPath

            var ex = Record.Exception(() => CreateService(ValidLufsMock()).RunAnalyzeBatch(json));

            Assert.Null(ex);
            Assert.Empty(Directory.GetFiles(_env.TempDir, "analyze-out*.json"));
        }

        [Fact]
        public void RunAnalyzeBatch_OutputIsCamelCaseJson()
        {
            var seriesDir = SetupSeries("dev", ("a.nus3audio", "0"));
            var outputPath = Path.Combine(_env.TempDir, "analyze-out.json");
            var json = WriteJson(new { seriesPath = seriesDir, outputPath, analyze = true });

            CreateService(ValidLufsMock()).RunAnalyzeBatch(json);

            var raw = File.ReadAllText(outputPath);
            Assert.Contains("\"seriesName\"", raw);
            Assert.Contains("\"ffmpegAvailable\"", raw);
            Assert.Contains("\"items\"", raw);
            Assert.DoesNotContain("\"SeriesName\"", raw);
        }

        // ── RunPreviewBatch ─────────────────────────────────────────────────

        [Fact]
        public void RunPreviewBatch_DecodesSourceToOutput()
        {
            var seriesDir = SetupSeries("dev", ("a.nus3audio", "0"));
            var outputPath = Path.Combine(_env.TempDir, "preview.wav");
            var decode = new Mock<IAudioDecodeService>();
            decode.Setup(m => m.DecodeToWav(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
            var json = WriteJson(new { seriesPath = seriesDir, filename = "a.nus3audio", outputPath });

            CreateService(decode: decode).RunPreviewBatch(json);

            decode.Verify(m => m.DecodeToWav(
                Path.Combine(seriesDir, "a.nus3audio"), outputPath), Times.Once);
        }

        [Fact]
        public void RunPreviewBatch_MissingSourceFile_DoesNotDecode()
        {
            var seriesDir = Path.Combine(_env.ModPath, "test-mod", "dev");
            Directory.CreateDirectory(seriesDir);
            var outputPath = Path.Combine(_env.TempDir, "preview.wav");
            var decode = new Mock<IAudioDecodeService>();
            var json = WriteJson(new { seriesPath = seriesDir, filename = "ghost.nus3audio", outputPath });

            CreateService(decode: decode).RunPreviewBatch(json);

            decode.Verify(m => m.DecodeToWav(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void RunPreviewBatch_DecodeFailure_ReturnsWithoutThrowing()
        {
            var seriesDir = SetupSeries("dev", ("a.nus3audio", "0"));
            var outputPath = Path.Combine(_env.TempDir, "preview.wav");
            var decode = new Mock<IAudioDecodeService>();
            decode.Setup(m => m.DecodeToWav(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
            var json = WriteJson(new { seriesPath = seriesDir, filename = "a.nus3audio", outputPath });

            var ex = Record.Exception(() => CreateService(decode: decode).RunPreviewBatch(json));
            Assert.Null(ex);
        }

        [Fact]
        public void RunPreviewBatch_MissingFields_DoesNotDecode()
        {
            var seriesDir = SetupSeries("dev", ("a.nus3audio", "0"));
            var decode = new Mock<IAudioDecodeService>();
            var json = WriteJson(new { seriesPath = seriesDir, filename = "a.nus3audio" }); // no outputPath

            CreateService(decode: decode).RunPreviewBatch(json);

            decode.Verify(m => m.DecodeToWav(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }
    }
}
