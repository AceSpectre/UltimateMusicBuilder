using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Testing;
using Tests.Helpers;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Integration
{
    /// <summary>
    /// Tests ConvertService — migrates an old Sma5h-format mod (metadata_mod.json
    /// + .nus3audio files in OldMods/) into a UMB folder-mod (series.toml +
    /// tracks.csv per series folder under MusicMods/).
    ///
    /// The service uses Spectre.Console interactive prompts, so each test
    /// swaps in a TestConsole and pushes Enter keys to accept the default
    /// selection / default mod name.
    ///
    /// Runs in a non-parallel collection because ConvertService.LoadExistingSeriesIds
    /// reads a cwd-relative path, so the fixture mutates
    /// <see cref="Environment.CurrentDirectory"/> for the duration of each test —
    /// running these in parallel with other suites would race on that global.
    /// </summary>
    [Collection("CwdSensitive")]
    public class ConvertServiceTests : IDisposable
    {
        private readonly TestEnvironment _env;
        private readonly IAnsiConsole _originalConsole;
        private readonly string _originalCwd;

        public ConvertServiceTests()
        {
            _env = new TestEnvironment();
            _originalConsole = AnsiConsole.Console;
            _originalCwd = Environment.CurrentDirectory;
            // ConvertService.LoadExistingSeriesIds() resolves ParamLabels.csv
            // relative to cwd. Point cwd at the repo so existing-series
            // detection works (e.g. ui_series_doubutsu).
            Environment.CurrentDirectory = _env.RepoRoot;
        }

        public void Dispose()
        {
            AnsiConsole.Console = _originalConsole;
            Environment.CurrentDirectory = _originalCwd;
            _env.Dispose();
        }

        private string SetupOldMod(string modName = "sma5h-test-mod")
        {
            // ConvertService looks for old mods in <ModPath>/../OldMods.
            // _env.ModPath = <TempDir>/MusicMods, so OldMods lives at <TempDir>/OldMods.
            var oldModsRoot = Path.Combine(Path.GetDirectoryName(_env.ModPath), "OldMods");
            var oldModDir = Path.Combine(oldModsRoot, modName);
            Directory.CreateDirectory(oldModDir);

            var sourceDir = Path.Combine(
                AppContext.BaseDirectory, "TestData", "old-mods", "sma5h-test-mod");
            foreach (var file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(oldModDir, Path.GetFileName(file)), overwrite: true);

            return oldModDir;
        }

        private static TestConsole SetupConsole()
        {
            var console = new TestConsole();
            console.Profile.Capabilities.Interactive = true;
            console.Interactive();
            // Press Enter to accept first item in the SelectionPrompt
            console.Input.PushKey(ConsoleKey.Enter);
            // Press Enter to accept the default value in the TextPrompt
            console.Input.PushTextWithEnter("");
            AnsiConsole.Console = console;
            return console;
        }

        private ConvertService CreateService()
        {
            return new ConvertService(
                _env.CreateMusicOptions(),
                TestEnvironment.CreateLogger<ConvertService>());
        }

        // ── Happy path ─────────────────────────────────────────────────────

        [Fact]
        public void Convert_CreatesOutputModFolder()
        {
            SetupOldMod();
            SetupConsole();

            CreateService().Run();

            var outputDir = Path.Combine(_env.ModPath, "sma5h-test-mod");
            Assert.True(Directory.Exists(outputDir),
                $"Output mod folder should exist at {outputDir}");
        }

        [Fact]
        public void Convert_CreatesSeriesFolderUsingNameId()
        {
            SetupOldMod();
            SetupConsole();

            CreateService().Run();

            // metadata_mod.json declares series name_id = "doubutsu"
            var seriesDir = Path.Combine(_env.ModPath, "sma5h-test-mod", "doubutsu");
            Assert.True(Directory.Exists(seriesDir),
                $"Series folder 'doubutsu' should be created at {seriesDir}");
        }

        [Fact]
        public void Convert_WritesSeriesTomlMarkedAsExisting()
        {
            SetupOldMod();
            SetupConsole();

            CreateService().Run();

            var tomlPath = Path.Combine(_env.ModPath, "sma5h-test-mod", "doubutsu", "series.toml");
            Assert.True(File.Exists(tomlPath));

            var content = File.ReadAllText(tomlPath);
            Assert.Contains("id = \"doubutsu\"", content);
            // ui_series_doubutsu is a vanilla series → existing-series = true
            Assert.Contains("existing-series = true", content);
            // Existing series should NOT get a custom series-playlist field
            Assert.DoesNotContain("series-playlist =", content);
            Assert.Contains("[[games]]", content);
            Assert.Contains("id = \"flowerhead\"", content);
        }

        [Fact]
        public void Convert_WritesTracksCsvWithExpectedColumns()
        {
            SetupOldMod();
            SetupConsole();

            CreateService().Run();

            var csvPath = Path.Combine(_env.ModPath, "sma5h-test-mod", "doubutsu", "tracks.csv");
            Assert.True(File.Exists(csvPath));

            var lines = File.ReadAllLines(csvPath);
            Assert.Equal(2, lines.Length); // header + 1 track
            var header = lines[0];
            foreach (var col in new[] { "filename", "game", "title", "author", "copyright",
                "record_type", "special_category", "volume", "info1", "in_soundtest", "order" })
            {
                Assert.Contains(col, header);
            }
        }

        [Fact]
        public void Convert_PopulatesTrackRowFromMetadata()
        {
            SetupOldMod();
            SetupConsole();

            CreateService().Run();

            var csvPath = Path.Combine(_env.ModPath, "sma5h-test-mod", "doubutsu", "tracks.csv");
            var lines = File.ReadAllLines(csvPath);
            var row = lines[1];

            Assert.Contains("flowerhead__somewhat_good_karts__01_karts.nus3audio", row);
            Assert.Contains("flowerhead", row); // game name_id
            // record_type "record_original" has its "record_" prefix stripped
            Assert.Contains("original", row);
            // nus3bank_config.volume = 2.7
            Assert.Contains("2.7", row);
        }

        [Fact]
        public void Convert_CopiesNus3AudioFileToSeriesFolder()
        {
            SetupOldMod();
            SetupConsole();

            CreateService().Run();

            var audioPath = Path.Combine(_env.ModPath, "sma5h-test-mod", "doubutsu",
                "flowerhead__somewhat_good_karts__01_karts.nus3audio");
            Assert.True(File.Exists(audioPath), $"Audio file should be copied to {audioPath}");
            Assert.True(new FileInfo(audioPath).Length > 0, "Copied audio file should not be empty");
        }

        [Fact]
        public void Convert_ExistingSeriesDoesNotAppearInSeriesOrderToml()
        {
            SetupOldMod();
            SetupConsole();

            CreateService().Run();

            // The mod has a single series (Animal Crossing = existing) so no
            // series-order.toml is produced (file written only when >=2 custom series).
            var orderPath = Path.Combine(_env.ModPath, "sma5h-test-mod", "series-order.toml");
            Assert.False(File.Exists(orderPath),
                "series-order.toml should not be written when the only series is existing");
        }

        // ── Error paths ────────────────────────────────────────────────────

        [Fact]
        public void Convert_NoOldModsDir_LogsErrorAndReturns()
        {
            // No SetupOldMod() — OldMods folder does not exist
            SetupConsole();

            CreateService().Run();

            var outputDir = Path.Combine(_env.ModPath, "sma5h-test-mod");
            Assert.False(Directory.Exists(outputDir));
        }

        [Fact]
        public void Convert_EmptyOldModsDir_LogsErrorAndReturns()
        {
            var oldModsRoot = Path.Combine(Path.GetDirectoryName(_env.ModPath), "OldMods");
            Directory.CreateDirectory(oldModsRoot); // exists but empty
            SetupConsole();

            CreateService().Run();

            Assert.Empty(Directory.GetDirectories(_env.ModPath));
        }
    }
}
