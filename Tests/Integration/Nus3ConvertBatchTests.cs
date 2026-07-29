using System.Text.Json;
using Tests.Helpers;
using UMB.CLI.Services;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Integration
{
    /// <summary>
    /// Drives <see cref="Nus3ConvertService.RunBatch(string)"/> — the non-interactive
    /// entry point the desktop app shells out to. Validation/early-return branches run
    /// tool-free; the happy paths ("end-to-end" and "loop" modes) invoke the real
    /// ffmpeg / VGAudioCli / nus3audio chain (present because SuiteEnvironmentCheck
    /// requires them).
    /// </summary>
    [Collection("CwdSensitive")]
    public class Nus3ConvertBatchTests : IDisposable
    {
        private const string OneTrackFlac =
            "flowerhead - Somewhat Good- Karts - 13 Time Trials.flac";

        private readonly TestEnvironment _env;
        private readonly ITestOutputHelper _output;

        public Nus3ConvertBatchTests(ITestOutputHelper output)
        {
            _env = new TestEnvironment();
            _output = output;
        }

        public void Dispose() => _env.Dispose();

        private Nus3ConvertService CreateService() =>
            new Nus3ConvertService(
                _env.CreateMusicOptions(),
                TestEnvironment.CreateLogger<Nus3ConvertService>());

        /// <summary>Creates a series folder and returns its path.</summary>
        private string SetupSeriesDir(string seriesName = "dev")
        {
            var seriesDir = Path.Combine(_env.ModPath, "test-mod", seriesName);
            Directory.CreateDirectory(seriesDir);
            return seriesDir;
        }

        private string CopyRealFlac(string seriesDir)
        {
            var src = Path.Combine(AppContext.BaseDirectory, "TestData", "configured-mod",
                "dev", OneTrackFlac);
            var dst = Path.Combine(seriesDir, OneTrackFlac);
            File.Copy(src, dst);
            return dst;
        }

        /// <summary>Serialises a batch-input object to a temp JSON file and returns its path.</summary>
        private string WriteJson(object input)
        {
            var path = Path.Combine(_env.TempDir, "nus3-batch-" + Guid.NewGuid().ToString("N")[..8] + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(input));
            return path;
        }

        // ── Validation / early-return branches (tool-free) ──────────────────

        [Fact]
        public void RunBatch_NullPath_ReturnsWithoutThrowing()
        {
            var ex = Record.Exception(() => CreateService().RunBatch(null));
            Assert.Null(ex);
        }

        [Fact]
        public void RunBatch_MissingJsonFile_ReturnsWithoutThrowing()
        {
            var missing = Path.Combine(_env.TempDir, "does-not-exist.json");
            var ex = Record.Exception(() => CreateService().RunBatch(missing));
            Assert.Null(ex);
        }

        [Fact]
        public void RunBatch_EmptyDecisions_CreatesNoValidateFolder()
        {
            var seriesDir = SetupSeriesDir();
            var json = WriteJson(new { seriesPath = seriesDir, decisions = Array.Empty<object>() });

            CreateService().RunBatch(json);

            Assert.False(Directory.Exists(Path.Combine(seriesDir, "songs-to-validate")),
                "Empty-decisions input should short-circuit before creating the validate folder");
        }

        [Fact]
        public void RunBatch_SeriesPathMissing_ReturnsWithoutThrowing()
        {
            var json = WriteJson(new
            {
                seriesPath = Path.Combine(_env.TempDir, "no-such-series"),
                decisions = new[] { new { filename = "x.flac", mode = "end-to-end" } }
            });

            var ex = Record.Exception(() => CreateService().RunBatch(json));
            Assert.Null(ex);
        }

        [Fact]
        public void RunBatch_SkipsWhenOutputAlreadyExists()
        {
            var seriesDir = SetupSeriesDir();
            CopyRealFlac(seriesDir);

            // Pre-place a distinctive stub where the output would land; the batch
            // must skip it untouched (the guard runs before any conversion work).
            var validateDir = Path.Combine(seriesDir, "songs-to-validate");
            Directory.CreateDirectory(validateDir);
            var basename = Path.GetFileNameWithoutExtension(OneTrackFlac);
            var outputNus3 = Path.Combine(validateDir, basename + ".nus3audio");
            var marker = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
            File.WriteAllBytes(outputNus3, marker);

            var json = WriteJson(new
            {
                seriesPath = seriesDir,
                decisions = new[] { new { filename = OneTrackFlac, mode = "end-to-end" } }
            });

            CreateService().RunBatch(json);

            Assert.Equal(marker, File.ReadAllBytes(outputNus3));
        }

        [Fact]
        public void RunBatch_SkipsMissingSourceFile()
        {
            var seriesDir = SetupSeriesDir();
            var json = WriteJson(new
            {
                seriesPath = seriesDir,
                decisions = new[] { new { filename = "not-on-disk.flac", mode = "end-to-end" } }
            });

            CreateService().RunBatch(json);

            var outputNus3 = Path.Combine(seriesDir, "songs-to-validate", "not-on-disk.nus3audio");
            Assert.False(File.Exists(outputNus3),
                "No output should be produced for a decision whose source file is absent");
        }

        // ── Happy paths (real ffmpeg / VGAudioCli / nus3audio) ──────────────

        [Fact]
        public void RunBatch_EndToEndMode_ProducesNus3Audio()
        {
            var seriesDir = SetupSeriesDir();
            CopyRealFlac(seriesDir);
            var json = WriteJson(new
            {
                seriesPath = seriesDir,
                decisions = new[] { new { filename = OneTrackFlac, mode = "end-to-end" } }
            });

            CreateService().RunBatch(json);

            var basename = Path.GetFileNameWithoutExtension(OneTrackFlac);
            var outputNus3 = Path.Combine(seriesDir, "songs-to-validate", basename + ".nus3audio");
            Assert.True(File.Exists(outputNus3), $"Expected nus3audio at {outputNus3}");
            Assert.True(new FileInfo(outputNus3).Length > 0, "Produced .nus3audio should not be empty");
            _output.WriteLine($"Produced {outputNus3} ({new FileInfo(outputNus3).Length} bytes)");
        }

        [Fact]
        public void RunBatch_LoopMode_ProducesNus3Audio()
        {
            var seriesDir = SetupSeriesDir();
            CopyRealFlac(seriesDir);
            var json = WriteJson(new
            {
                seriesPath = seriesDir,
                decisions = new[]
                {
                    new { filename = OneTrackFlac, mode = "loop", loopStartSamples = 1000L, loopEndSamples = 48000L }
                }
            });

            CreateService().RunBatch(json);

            var basename = Path.GetFileNameWithoutExtension(OneTrackFlac);
            var outputNus3 = Path.Combine(seriesDir, "songs-to-validate", basename + ".nus3audio");
            Assert.True(File.Exists(outputNus3), $"Expected nus3audio at {outputNus3}");
            Assert.True(new FileInfo(outputNus3).Length > 0, "Produced .nus3audio should not be empty");
        }
    }
}
