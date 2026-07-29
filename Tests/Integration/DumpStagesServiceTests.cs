using Moq;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Models;
using Tests.Helpers;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Integration
{
    /// <summary>
    /// Drives <see cref="DumpStagesService.Run"/> with a mocked
    /// <see cref="IAudioStateService"/> so the grouping + CSV-writing logic is
    /// exercised deterministically without loading real PRC data. The service
    /// writes stages_dump.csv into the current working directory, so this runs
    /// under the CwdSensitive collection and restores the cwd afterwards.
    /// </summary>
    [Collection("CwdSensitive")]
    public class DumpStagesServiceTests : IDisposable
    {
        private readonly TestEnvironment _env;
        private readonly string _origCwd;

        public DumpStagesServiceTests()
        {
            _env = new TestEnvironment();
            _origCwd = Directory.GetCurrentDirectory();
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_origCwd);
            _env.Dispose();
        }

        [Fact]
        public void Run_WritesStagesDumpCsv()
        {
            var audioState = new Mock<IAudioStateService>();
            audioState.Setup(m => m.GetStagesEntries()).Returns(new[]
            {
                new StageEntry { UiStageId = "ui_stage_temple", UiSeriesId = "ui_series_zelda", BgmSetId = "bgmzelda" },
                new StageEntry { UiStageId = "ui_stage_battlefield", UiSeriesId = "ui_series_smashbros", BgmSetId = "bgmsmashbtl" },
                new StageEntry { UiStageId = "ui_stage_castle", UiSeriesId = "ui_series_zelda", BgmSetId = "bgmzelda" },
            });

            var workDir = Path.Combine(_env.TempDir, "dump");
            Directory.CreateDirectory(workDir);
            Directory.SetCurrentDirectory(workDir);

            new DumpStagesService(audioState.Object,
                TestEnvironment.CreateLogger<DumpStagesService>()).Run();

            audioState.Verify(m => m.InitBgmEntriesFromStateManager(), Times.Once);

            var csvPath = Path.Combine(workDir, "stages_dump.csv");
            Assert.True(File.Exists(csvPath), "stages_dump.csv should be written to the working directory");

            var lines = File.ReadAllLines(csvPath);
            Assert.Equal("ui_stage_id,ui_series_id,bgm_set_id", lines[0]);
            // Sorted by UiSeriesId then UiStageId: smashbros before zelda.
            Assert.Equal("ui_stage_battlefield,ui_series_smashbros,bgmsmashbtl", lines[1]);
            Assert.Equal("ui_stage_castle,ui_series_zelda,bgmzelda", lines[2]);
            Assert.Equal("ui_stage_temple,ui_series_zelda,bgmzelda", lines[3]);
        }

        [Fact]
        public void Run_InitFailure_ReturnsWithoutWritingCsv()
        {
            var audioState = new Mock<IAudioStateService>();
            audioState.Setup(m => m.InitBgmEntriesFromStateManager())
                .Throws(new InvalidOperationException("boom"));

            var workDir = Path.Combine(_env.TempDir, "dump-fail");
            Directory.CreateDirectory(workDir);
            Directory.SetCurrentDirectory(workDir);

            var ex = Record.Exception(() =>
                new DumpStagesService(audioState.Object,
                    TestEnvironment.CreateLogger<DumpStagesService>()).Run());

            Assert.Null(ex); // failure is caught and logged, not thrown
            Assert.False(File.Exists(Path.Combine(workDir, "stages_dump.csv")),
                "No CSV should be written when game data fails to load");
        }
    }
}
