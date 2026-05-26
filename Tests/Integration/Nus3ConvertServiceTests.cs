using Spectre.Console;
using Spectre.Console.Testing;
using Tests.Helpers;
using UMB.CLI.Services;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Integration
{
    /// <summary>
    /// Drives Nus3ConvertService.Run() end-to-end against one bundled FLAC,
    /// then compares the produced .nus3audio against a committed manifest
    /// (filename + size). Real pymusiclooper / ffmpeg / VGAudioCli / nus3audio
    /// are invoked — these must be on PATH (verified by SuiteEnvironmentCheck).
    /// </summary>
    [Collection("CwdSensitive")]
    public class Nus3ConvertServiceTests : IDisposable
    {
        private const string OneTrackFlac =
            "flowerhead - Somewhat Good- Karts - 13 Time Trials.flac";

        private readonly TestEnvironment _env;
        private readonly IAnsiConsole _originalConsole;
        private readonly ITestOutputHelper _output;

        public Nus3ConvertServiceTests(ITestOutputHelper output)
        {
            _env = new TestEnvironment();
            _originalConsole = AnsiConsole.Console;
            _output = output;
        }

        public void Dispose()
        {
            AnsiConsole.Console = _originalConsole;
            _env.Dispose();
        }

        private Nus3ConvertService CreateService()
        {
            return new Nus3ConvertService(
                _env.CreateMusicOptions(),
                TestEnvironment.CreateLogger<Nus3ConvertService>());
        }

        private string SetupModWithOneFlac()
        {
            var modDir = Path.Combine(_env.ModPath, "test-mod");
            var seriesDir = Path.Combine(modDir, "dev");
            Directory.CreateDirectory(seriesDir);

            // Minimal series.toml just so Script.PromptModAndSeries auto-selects.
            File.WriteAllText(Path.Combine(seriesDir, "series.toml"),
                "[series]\nid = \"dev\"\nname = \"dev\"\nplaylist-incidence = 100\n" +
                "series-playlist = \"bgm_dev\"\n\n[[games]]\nid = \"dev\"\nname = \"dev\"\n" +
                "\n[default-track-data]\ngame = \"dev\"\n");

            var src = Path.Combine(AppContext.BaseDirectory, "TestData", "configured-mod",
                "dev", OneTrackFlac);
            File.Copy(src, Path.Combine(seriesDir, OneTrackFlac));
            return modDir;
        }

        /// <summary>
        /// Push the keystrokes the auto-mode happy path needs:
        ///   Confirm "Auto-convert?" → 'y' Enter
        ///   TextPrompt loop-score threshold (default 94.5) → Enter
        ///   (preview length prompt is skipped in auto mode)
        /// </summary>
        private static void SetupAutoModeConsole()
        {
            var console = new TestConsole();
            console.Profile.Capabilities.Interactive = true;
            console.Interactive();
            console.Input.PushTextWithEnter("y");
            console.Input.PushTextWithEnter("");
            AnsiConsole.Console = console;
        }

        [Fact]
        public void Convert_ProducesNus3AudioFileInValidateFolder()
        {
            var modDir = SetupModWithOneFlac();
            SetupAutoModeConsole();

            CreateService().Run();

            var validateDir = Path.Combine(modDir, "dev", "songs-to-validate");
            var expectedBasename = Path.GetFileNameWithoutExtension(OneTrackFlac);
            var outputFile = Path.Combine(validateDir, expectedBasename + ".nus3audio");

            Assert.True(File.Exists(outputFile),
                $"Expected nus3audio output at {outputFile}");
            Assert.True(new FileInfo(outputFile).Length > 0,
                "Produced .nus3audio should not be empty");
            _output.WriteLine($"Produced {outputFile} ({new FileInfo(outputFile).Length} bytes)");
        }

        [Fact]
        public void Convert_SkipsWhenSourceFolderHasNoAudio()
        {
            var modDir = Path.Combine(_env.ModPath, "test-mod");
            var seriesDir = Path.Combine(modDir, "dev");
            Directory.CreateDirectory(seriesDir);
            File.WriteAllText(Path.Combine(seriesDir, "series.toml"),
                "[series]\nid = \"dev\"\nname = \"dev\"\nplaylist-incidence = 100\n" +
                "series-playlist = \"bgm_dev\"\n\n[[games]]\nid = \"dev\"\nname = \"dev\"\n" +
                "\n[default-track-data]\ngame = \"dev\"\n");

            SetupAutoModeConsole();
            CreateService().Run();

            var validateDir = Path.Combine(seriesDir, "songs-to-validate");
            Assert.False(Directory.Exists(validateDir),
                "No validate folder should be created when no source audio exists");
        }

        [Fact]
        public void Convert_OutputMatchesBaselineManifest()
        {
            var baselineDir = Path.Combine(
                BaselineGenerator.BaselineRoot(_env.RepoRoot), "nus3-convert");
            if (!File.Exists(Path.Combine(baselineDir, BaselineComparer.ManifestFileName)))
            {
                _output.WriteLine(
                    "SKIPPED: nus3-convert baseline missing. Generate via " +
                    "`UMB_REGEN_BASELINES=1 dotnet test --filter Category=RegenBaselines`.");
                return;
            }

            var modDir = SetupModWithOneFlac();
            SetupAutoModeConsole();
            CreateService().Run();

            var validateDir = Path.Combine(modDir, "dev", "songs-to-validate");
            var report = BaselineComparer.Compare(validateDir, baselineDir);
            report.AssertMatches(_output);
        }
    }
}
