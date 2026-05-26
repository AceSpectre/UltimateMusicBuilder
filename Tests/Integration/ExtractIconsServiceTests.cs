using Spectre.Console;
using Spectre.Console.Testing;
using Tests.Helpers;
using UMB.CLI.Services;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Integration
{
    /// <summary>
    /// Invokes ExtractIconsService.Run() against a committed Sma5h-format mod
    /// containing a single BNTX (produced by running the real build pipeline,
    /// see <see cref="BaselineGenerator.GenerateExtractIconsSource"/>). The
    /// committed BNTX is the same as what the build test exercises, so this
    /// test covers the round-trip: build's PNG → BNTX → extracted PNG.
    /// </summary>
    [Collection("CwdSensitive")]
    public class ExtractIconsServiceTests : IDisposable
    {
        private readonly TestEnvironment _env;
        private readonly IAnsiConsole _originalConsole;
        private readonly ITestOutputHelper _output;

        public ExtractIconsServiceTests(ITestOutputHelper output)
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

        private ExtractIconsService CreateService()
        {
            return new ExtractIconsService(
                _env.CreateMusicOptions(),
                TestEnvironment.CreateLogger<ExtractIconsService>());
        }

        private string BaselineExtractIconsSource()
            => Path.Combine(BaselineGenerator.BaselineRoot(_env.RepoRoot),
                "extract-icons-source");

        private static void SetupConsole(string builtModPath)
        {
            var console = new TestConsole();
            console.Profile.Capabilities.Interactive = true;
            console.Interactive();
            // TextPrompt → path to built Sma5h mod
            console.Input.PushTextWithEnter(builtModPath);
            // SelectionPrompt → first (only) UMB mod = Enter
            console.Input.PushKey(ConsoleKey.Enter);
            AnsiConsole.Console = console;
        }

        private string SetupUmbModWithEmptySeries(string seriesId)
        {
            var modDir = Path.Combine(_env.ModPath, "umb-target");
            var seriesDir = Path.Combine(modDir, seriesId);
            Directory.CreateDirectory(seriesDir);
            return modDir;
        }

        private static (int width, int height) ReadPngDimensions(string pngPath)
        {
            using var fs = File.OpenRead(pngPath);
            var header = new byte[24];
            if (fs.Read(header, 0, 24) != 24)
                throw new InvalidOperationException("PNG too short");
            // IHDR: width at byte 16, height at byte 20, both big-endian uint32.
            int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            int height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
            return (width, height);
        }

        // ── Tests ──────────────────────────────────────────────────────────

        [Fact]
        public void Extract_ProducesPngForEachSeriesBntx()
        {
            var modDir = SetupUmbModWithEmptySeries("dev");
            SetupConsole(BaselineExtractIconsSource());

            CreateService().Run();

            var pngPath = Path.Combine(modDir, "dev", "icon.png");
            Assert.True(File.Exists(pngPath),
                $"Expected extracted icon.png at {pngPath}");
            Assert.True(new FileInfo(pngPath).Length > 0,
                "Extracted icon.png should not be empty");
        }

        [Fact]
        public void Extract_PngDimensionsMatchConfiguredModIcon()
        {
            var modDir = SetupUmbModWithEmptySeries("dev");
            SetupConsole(BaselineExtractIconsSource());

            CreateService().Run();

            var extracted = Path.Combine(modDir, "dev", "icon.png");
            var source = Path.Combine(AppContext.BaseDirectory, "TestData",
                "configured-mod", "dev", "icon.png");
            Assert.True(File.Exists(extracted));
            Assert.True(File.Exists(source));

            var extractedDims = ReadPngDimensions(extracted);
            var sourceDims = ReadPngDimensions(source);

            _output.WriteLine($"Source icon: {sourceDims.width}×{sourceDims.height}");
            _output.WriteLine($"Extracted icon: {extractedDims.width}×{extractedDims.height}");

            // BNTX→PNG roundtrip via ultimate_tex_cli preserves dimensions even
            // though pixel bytes aren't necessarily identical.
            Assert.Equal(sourceDims.width, extractedDims.width);
            Assert.Equal(sourceDims.height, extractedDims.height);
        }

        [Fact]
        public void Extract_SkipsBntxWithNoMatchingSeriesFolder()
        {
            // UMB mod has only a "gamma" series; the BNTX baseline contains
            // series_0_dev.bntx — so nothing should be extracted.
            var modDir = SetupUmbModWithEmptySeries("gamma");
            SetupConsole(BaselineExtractIconsSource());

            CreateService().Run();

            Assert.False(File.Exists(Path.Combine(modDir, "gamma", "icon.png")));
            Assert.False(File.Exists(Path.Combine(modDir, "dev", "icon.png")));
        }
    }
}
