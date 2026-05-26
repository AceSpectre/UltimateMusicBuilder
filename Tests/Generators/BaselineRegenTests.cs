using Spectre.Console;
using Spectre.Console.Testing;
using Tests.Helpers;
using UMB.CLI.Services;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Generators
{
    /// <summary>
    /// Regenerate committed baselines. Run explicitly with
    /// <c>UMB_REGEN_BASELINES=1 dotnet test --filter Category=RegenBaselines</c>.
    /// When the env var is unset each test no-ops, so a stray run of the full
    /// suite never overwrites committed baselines.
    /// </summary>
    [Trait("Category", "RegenBaselines")]
    [Collection("CwdSensitive")]
    public class BaselineRegenTests : IDisposable
    {
        private readonly TestEnvironment _env;
        private readonly ITestOutputHelper _output;

        public BaselineRegenTests(ITestOutputHelper output)
        {
            _env = new TestEnvironment();
            _output = output;
        }

        public void Dispose() => _env.Dispose();

        private string BaselineDir(string scenario)
            => Path.Combine(BaselineGenerator.BaselineRoot(_env.RepoRoot), scenario);

        private bool ShouldRegen()
        {
            if (Environment.GetEnvironmentVariable("UMB_REGEN_BASELINES") == "1")
                return true;
            _output.WriteLine(
                "SKIPPED: set UMB_REGEN_BASELINES=1 to regenerate committed baselines.");
            return false;
        }

        [Fact]
        public void Regen_DefaultBuild()
        {
            if (!ShouldRegen()) return;
            var dir = BaselineDir("default-build");
            BaselineGenerator.GenerateDefaultBuild(_env, dir);
            _output.WriteLine($"Wrote default-build baseline → {dir}");
            Assert.True(Directory.Exists(dir));
        }

        [Fact]
        public void Regen_SeriesOrdered()
        {
            if (!ShouldRegen()) return;
            var dir = BaselineDir("series-ordered");
            BaselineGenerator.GenerateSeriesOrdered(_env, dir);
            _output.WriteLine($"Wrote series-ordered baseline → {dir}");
            Assert.True(Directory.Exists(dir));
        }

        [Fact]
        public void Regen_TrackOrdered()
        {
            if (!ShouldRegen()) return;
            var dir = BaselineDir("track-ordered");
            BaselineGenerator.GenerateTrackOrdered(_env, dir);
            _output.WriteLine($"Wrote track-ordered baseline → {dir}");
            Assert.True(Directory.Exists(dir));
        }

        [Fact]
        public void Regen_ExtractIconsSource()
        {
            if (!ShouldRegen()) return;
            var dir = BaselineDir("extract-icons-source");
            BaselineGenerator.GenerateExtractIconsSource(_env, dir);
            _output.WriteLine($"Wrote extract-icons-source baseline → {dir}");
            Assert.True(Directory.Exists(Path.Combine(dir, "ui", "replace", "series", "series_0")));
        }

        [Fact]
        public void Regen_Nus3Convert()
        {
            if (!ShouldRegen()) return;

            const string OneTrackFlac =
                "flowerhead - Somewhat Good- Karts - 13 Time Trials.flac";

            // Replicate Nus3ConvertServiceTests.SetupModWithOneFlac to keep regen
            // and the test in lockstep.
            var modDir = Path.Combine(_env.ModPath, "test-mod");
            var seriesDir = Path.Combine(modDir, "dev");
            Directory.CreateDirectory(seriesDir);
            File.WriteAllText(Path.Combine(seriesDir, "series.toml"),
                "[series]\nid = \"dev\"\nname = \"dev\"\nplaylist-incidence = 100\n" +
                "series-playlist = \"bgm_dev\"\n\n[[games]]\nid = \"dev\"\nname = \"dev\"\n" +
                "\n[default-track-data]\ngame = \"dev\"\n");
            var src = Path.Combine(AppContext.BaseDirectory, "TestData", "configured-mod",
                "dev", OneTrackFlac);
            File.Copy(src, Path.Combine(seriesDir, OneTrackFlac));

            var originalConsole = AnsiConsole.Console;
            try
            {
                var console = new TestConsole();
                console.Profile.Capabilities.Interactive = true;
                console.Interactive();
                console.Input.PushTextWithEnter("y"); // auto-mode
                console.Input.PushTextWithEnter("");  // default threshold
                AnsiConsole.Console = console;

                var svc = new Nus3ConvertService(
                    _env.CreateMusicOptions(),
                    TestEnvironment.CreateLogger<Nus3ConvertService>());
                svc.Run();
            }
            finally { AnsiConsole.Console = originalConsole; }

            var producedDir = Path.Combine(seriesDir, "songs-to-validate");
            var baselineDir = BaselineDir("nus3-convert");
            BaselineGenerator.GenerateNus3ConvertManifest(producedDir, baselineDir);
            _output.WriteLine($"Wrote nus3-convert baseline → {baselineDir}");
            Assert.True(File.Exists(Path.Combine(baselineDir, BaselineComparer.ManifestFileName)));
        }
    }
}
