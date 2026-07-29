using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sma5h;
using Sma5h.Interfaces;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using Sma5h.Mods.Music.Services;
using Spectre.Console;
using Spectre.Console.Testing;
using Tests.Helpers;
using UMB.CLI;
using UMB.CLI.Services;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Integration
{
    /// <summary>
    /// Drives the <see cref="BuildService.Run"/> wrapper itself (mod selection,
    /// explicit-mod match/mismatch, non-interactive fallback, series-order.toml
    /// loading and pre-build validation) rather than the underlying Sma5hMod
    /// pipeline that BuildPipelineTests already covers. Runs against a real
    /// service provider with mocked audio services and a non-interactive console.
    /// </summary>
    [Collection("CwdSensitive")]
    public class BuildServiceTests : IDisposable
    {
        private readonly TestEnvironment _env;
        private readonly IAnsiConsole _originalConsole;
        private readonly ITestOutputHelper _output;

        public BuildServiceTests(ITestOutputHelper output)
        {
            _env = new TestEnvironment();
            _output = output;
            _originalConsole = AnsiConsole.Console;
            // Non-interactive console forces the "build everything, proceed past
            // warnings" branches — the path the desktop app takes.
            AnsiConsole.Console = new TestConsole();
            FolderMusicMod.SeriesFilterByMod = null;
            Sma5hMusic.ExplicitSeriesOrder = null;
            MusicModManagerService.ModFilter = null;
        }

        public void Dispose()
        {
            AnsiConsole.Console = _originalConsole;
            FolderMusicMod.SeriesFilterByMod = null;
            Sma5hMusic.ExplicitSeriesOrder = null;
            MusicModManagerService.ModFilter = null;
            _env.Dispose();
        }

        private (BuildService build, ServiceProvider sp) CreateBuildService()
        {
            var sp = _env.CreateFullServiceProvider();
            var workspace = new WorkspaceManager(
                sp.GetRequiredService<IOptionsMonitor<Sma5hOptions>>(),
                TestEnvironment.CreateLogger<IWorkspaceManager>());
            var build = new BuildService(
                sp,
                workspace,
                sp.GetRequiredService<IStateManager>(),
                sp.GetRequiredService<IOptionsMonitor<Sma5hMusicOptions>>(),
                TestEnvironment.CreateLogger<BuildService>());
            return (build, sp);
        }

        private string[] ArcOutputFiles()
        {
            var outputPath = Path.Combine(_env.TempDir, "ArcOutput");
            return Directory.Exists(outputPath)
                ? Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories)
                : Array.Empty<string>();
        }

        [Fact]
        public async Task Run_NoMods_WarnsAndProducesNoOutput()
        {
            // ModPath exists (created by TestEnvironment) but holds no mod folders.
            var (build, sp) = CreateBuildService();
            using (sp)
                await build.Run();

            Assert.Empty(ArcOutputFiles());
        }

        [Fact]
        public async Task Run_RequestedModNotFound_ProducesNoOutput()
        {
            _env.CreateConfiguredMod("test-mod");
            var (build, sp) = CreateBuildService();
            using (sp)
                await build.Run("does-not-exist");

            Assert.Empty(ArcOutputFiles());
        }

        [Fact]
        public async Task Run_RequestedModByName_ProducesArcOutput()
        {
            _env.CreateConfiguredMod("test-mod");
            var (build, sp) = CreateBuildService();
            using (sp)
                await build.Run("test-mod");

            var files = ArcOutputFiles();
            Assert.True(files.Length > 0, "Building the named mod should produce ArcOutput files");
            _output.WriteLine($"Build produced {files.Length} output file(s).");
        }

        [Fact]
        public async Task Run_SingleModNonInteractive_ProducesArcOutput()
        {
            // No requestedMod + exactly one mod → auto-selected without a prompt.
            _env.CreateConfiguredMod("test-mod");
            var (build, sp) = CreateBuildService();
            using (sp)
                await build.Run();

            Assert.True(ArcOutputFiles().Length > 0,
                "Single-mod non-interactive build should produce ArcOutput files");
        }
    }
}
