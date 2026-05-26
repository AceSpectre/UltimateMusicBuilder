using Microsoft.Extensions.Logging;
using Moq;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Models;
using Tests.Helpers;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Integration
{
    public class ScaffoldServiceTests : IDisposable
    {
        private readonly TestEnvironment _env;

        public ScaffoldServiceTests()
        {
            _env = new TestEnvironment();
        }

        public void Dispose() => _env.Dispose();

        private ScaffoldService CreateService(Mock<IAudioStateService> audioState = null)
        {
            audioState ??= TestEnvironment.CreateMockAudioStateService();
            return new ScaffoldService(
                _env.CreateMusicOptions(),
                audioState.Object,
                TestEnvironment.CreateLogger<ScaffoldService>());
        }

        // ── New series scaffold ────────────────────────────────────────────

        [Fact]
        public void Scaffold_CreatesSeriesTomlForNewSeries()
        {
            _env.CreateUnconfiguredMod();
            var service = CreateService();
            service.Run();

            var tomlPath = Path.Combine(_env.ModPath, "scaffold-mod", "dev", "series.toml");
            Assert.True(File.Exists(tomlPath));

            var content = File.ReadAllText(tomlPath);
            Assert.Contains("[series]", content);
            Assert.Contains("id = \"dev\"", content);
            Assert.DoesNotContain("existing-series", content);
        }

        [Fact]
        public void Scaffold_CreatesTracksCsvForNewSeries()
        {
            _env.CreateUnconfiguredMod();
            var service = CreateService();
            service.Run();

            var csvPath = Path.Combine(_env.ModPath, "scaffold-mod", "dev", "tracks.csv");
            Assert.True(File.Exists(csvPath));

            var lines = File.ReadAllLines(csvPath);
            Assert.True(lines.Length > 1, "CSV should have header + data rows");

            var header = lines[0];
            Assert.Contains("filename", header);
            Assert.Contains("game", header);
            Assert.Contains("title", header);
            Assert.Contains("volume", header);
            Assert.Contains("in_soundtest", header);
        }

        [Fact]
        public void Scaffold_PopulatesAllFlacFilesInCsv()
        {
            _env.CreateUnconfiguredMod();
            var service = CreateService();
            service.Run();

            var csvPath = Path.Combine(_env.ModPath, "scaffold-mod", "dev", "tracks.csv");
            var lines = File.ReadAllLines(csvPath);
            Assert.Equal(14, lines.Length); // header + 13 tracks
        }

        [Fact]
        public void Scaffold_CsvHasCorrectFilenames()
        {
            _env.CreateUnconfiguredMod();
            var service = CreateService();
            service.Run();

            var csvContent = File.ReadAllText(
                Path.Combine(_env.ModPath, "scaffold-mod", "dev", "tracks.csv"));
            Assert.Contains("flowerhead - Somewhat Good- Karts - 01 KARTS!.nus3audio", csvContent);
            Assert.Contains("flowerhead - Somewhat Good- Karts - 13 Time Trials.nus3audio", csvContent);
        }

        [Fact]
        public void Scaffold_CreatesDefaultTrackDataSection()
        {
            _env.CreateUnconfiguredMod();
            var service = CreateService();
            service.Run();

            var tomlPath = Path.Combine(_env.ModPath, "scaffold-mod", "dev", "series.toml");
            var content = File.ReadAllText(tomlPath);
            Assert.Contains("[default-track-data]", content);
            Assert.Contains("game =", content);
            Assert.Contains("volume =", content);
        }

        [Fact]
        public void Scaffold_CreatesSeriesPlaylistField()
        {
            _env.CreateUnconfiguredMod();
            var service = CreateService();
            service.Run();

            var tomlPath = Path.Combine(_env.ModPath, "scaffold-mod", "dev", "series.toml");
            var content = File.ReadAllText(tomlPath);
            Assert.Contains("series-playlist =", content);
        }

        // ── Existing series scaffold ───────────────────────────────────────

        [Fact]
        public void Scaffold_DetectsExistingSeriesFromVanillaData()
        {
            _env.CreateUnconfiguredMod();

            var audioState = TestEnvironment.CreateMockAudioStateService();
            var marioSeries = new SeriesEntry("ui_series_mario", EntrySource.Core)
            {
                NameId = "mario"
            };
            marioSeries.MSBTTitle["en_us"] = "Mario";
            audioState.Setup(s => s.GetSeriesEntries()).Returns(new[] { marioSeries });

            var marioGame = new GameTitleEntry("ui_gametitle_mario", EntrySource.Core)
            {
                NameId = "mario",
                UiSeriesId = "ui_series_mario"
            };
            marioGame.MSBTTitle["en_us"] = "Mario";
            audioState.Setup(s => s.GetGameTitleEntries()).Returns(new[] { marioGame });

            var marioStage = new StageEntry
            {
                UiSeriesId = "ui_series_mario",
                BgmSetId = "bgmmario"
            };
            audioState.Setup(s => s.GetStagesEntries()).Returns(new[] { marioStage });

            var service = CreateService(audioState);
            service.Run();

            var tomlPath = Path.Combine(_env.ModPath, "scaffold-mod", "mario", "series.toml");
            var content = File.ReadAllText(tomlPath);
            Assert.Contains("existing-series = true", content);
            Assert.Contains("id = \"mario\"", content);
        }

        [Fact]
        public void Scaffold_Mario_PopulatesAllTracks()
        {
            _env.CreateUnconfiguredMod();
            var service = CreateService();
            service.Run();

            var csvPath = Path.Combine(_env.ModPath, "scaffold-mod", "mario", "tracks.csv");
            var lines = File.ReadAllLines(csvPath);
            Assert.Equal(7, lines.Length); // header + 6 tracks
        }

        // ── Series order ───────────────────────────────────────────────────

        [Fact]
        public void Scaffold_CreatesSeriesOrderToml()
        {
            _env.CreateUnconfiguredMod();
            var service = CreateService();
            service.Run();

            var orderPath = Path.Combine(_env.ModPath, "scaffold-mod", "series-order.toml");
            Assert.True(File.Exists(orderPath));

            var content = File.ReadAllText(orderPath);
            Assert.Contains("order = [", content);
        }

        [Fact]
        public void Scaffold_SeriesOrderContainsCustomSeriesOnly()
        {
            _env.CreateUnconfiguredMod();

            var audioState = TestEnvironment.CreateMockAudioStateService();
            var marioSeries = new SeriesEntry("ui_series_mario", EntrySource.Core) { NameId = "mario" };
            audioState.Setup(s => s.GetSeriesEntries()).Returns(new[] { marioSeries });

            var service = CreateService(audioState);
            service.Run();

            var content = File.ReadAllText(
                Path.Combine(_env.ModPath, "scaffold-mod", "series-order.toml"));
            Assert.Contains("\"dev\"", content);
            Assert.DoesNotContain("\"mario\"", content);
        }

        // ── Idempotency ────────────────────────────────────────────────────

        [Fact]
        public void Scaffold_IdempotentOnSecondRun()
        {
            _env.CreateUnconfiguredMod();
            var service = CreateService();

            service.Run();
            var csvBefore = File.ReadAllText(
                Path.Combine(_env.ModPath, "scaffold-mod", "dev", "tracks.csv"));

            service.Run();
            var csvAfter = File.ReadAllText(
                Path.Combine(_env.ModPath, "scaffold-mod", "dev", "tracks.csv"));

            Assert.Equal(csvBefore, csvAfter);
        }

        [Fact]
        public void Scaffold_AddsNewTracksToExistingCsv()
        {
            _env.CreateUnconfiguredMod();
            var service = CreateService();
            service.Run();

            var devDir = Path.Combine(_env.ModPath, "scaffold-mod", "dev");
            File.WriteAllBytes(Path.Combine(devDir, "new_track.nus3audio"), Array.Empty<byte>());

            service.Run();

            var csvContent = File.ReadAllText(Path.Combine(devDir, "tracks.csv"));
            Assert.Contains("new_track.nus3audio", csvContent);
        }
    }
}
