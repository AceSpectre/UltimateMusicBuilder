using Microsoft.Extensions.Logging;
using Moq;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using Sma5h.Mods.Music.Models;
using System.Linq;
using Tests.Helpers;
using Tomlyn;
using Tomlyn.Model;
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

        private static Mock<IAudioStateService> CreateMarioExistingSeriesAudioState(
            params (string BgmId, short TestDispOrder)[] vanillaSongs)
        {
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

            audioState.Setup(s => s.GetStagesEntries()).Returns(new[]
            {
                new StageEntry
                {
                    UiSeriesId = "ui_series_mario",
                    BgmSetId = "bgmmario"
                }
            });

            var bgmEntries = vanillaSongs.Select(song =>
            {
                var entry = new BgmDbRootEntry(song.BgmId)
                {
                    UiGameTitleId = "ui_gametitle_mario",
                    TestDispOrder = song.TestDispOrder
                };
                entry.Title["en_us"] = song.BgmId;
                return entry;
            }).ToArray();
            audioState.Setup(s => s.GetBgmDbRootEntries()).Returns(bgmEntries);

            return audioState;
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

            var audioState = CreateMarioExistingSeriesAudioState();

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

        [Fact]
        public void Scaffold_CreatesSongOrderTomlForExistingSeries_WhenMissing()
        {
            _env.CreateUnconfiguredMod();
            var audioState = CreateMarioExistingSeriesAudioState(
                ("ui_bgm_vanilla_ps01", 10),
                ("ui_bgm_vanilla_ps02", 20));

            CreateService(audioState).Run();

            var songOrderPath = Path.Combine(_env.ModPath, "scaffold-mod", "mario", "song_order.toml");
            Assert.True(File.Exists(songOrderPath));

            var model = Toml.ToModel(File.ReadAllText(songOrderPath));
            var order = (model["song_order"] as TomlArray).OfType<string>().ToList();

            var expectedModIds = TestEnvironment.MarioTrackFiles
                .Select(name => Path.ChangeExtension(name, ".nus3audio"))
                .Select(name => MusicConstants.InternalIds.UI_BGM_ID_PREFIX + FolderMusicMod.DeriveToneId(name))
                .ToList();

            Assert.Equal(2 + expectedModIds.Count, order.Count);
            Assert.Equal("ui_bgm_vanilla_ps01", order[0]);
            Assert.Equal("ui_bgm_vanilla_ps02", order[1]);
            Assert.Equal(expectedModIds, order.Skip(2).ToList());
        }

        [Fact]
        public void Scaffold_DoesNotOverwriteExistingSongOrderTomlForExistingSeries()
        {
            _env.CreateUnconfiguredMod();
            var marioDir = Path.Combine(_env.ModPath, "scaffold-mod", "mario");
            var songOrderPath = Path.Combine(marioDir, "song_order.toml");
            var original = "song_order = [\n  \"ui_bgm_custom_existing\"\n]\n";
            File.WriteAllText(songOrderPath, original);

            var audioState = CreateMarioExistingSeriesAudioState(
                ("ui_bgm_vanilla_ps01", 10),
                ("ui_bgm_vanilla_ps02", 20));

            CreateService(audioState).Run();

            Assert.Equal(original, File.ReadAllText(songOrderPath));
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

        // ── Column-migration / data-preservation ───────────────────────────

        [Fact]
        public void Scaffold_AddsMissingColumnsToOldCsv()
        {
            _env.CreateUnconfiguredMod();
            var seriesDir = Path.Combine(_env.ModPath, "scaffold-mod", "dev");

            // Pre-create a series.toml so scaffold proceeds to CSV migration
            File.WriteAllText(Path.Combine(seriesDir, "series.toml"),
                "[series]\nid = \"dev\"\nname = \"Dev\"\nplaylist-incidence = 100\nseries-playlist = \"bgm_dev\"\n\n[[games]]\nid = \"dev\"\nname = \"Dev\"\n\n[default-track-data]\ngame = \"dev\"\n");

            // Legacy CSV with only 3 columns and one real row
            var trackName = TestEnvironment.DevTrackFiles[0].Replace(".flac", ".nus3audio");
            var csvPath = Path.Combine(seriesDir, "tracks.csv");
            File.WriteAllText(csvPath,
                "filename,game,title\n" +
                $"{trackName},dev,Legacy Title\n");

            CreateService().Run();

            var lines = File.ReadAllLines(csvPath);
            var header = lines[0];
            foreach (var col in new[] { "filename", "game", "title", "author", "copyright",
                "record_type", "special_category", "volume", "info1", "in_soundtest" })
            {
                Assert.Contains(col, header);
            }

            // Original row's first three fields are preserved verbatim
            Assert.Contains(trackName, lines[1]);
            Assert.Contains("Legacy Title", lines[1]);
        }

        [Fact]
        public void Scaffold_PreservesNonDefaultValuesInExistingRows()
        {
            _env.CreateUnconfiguredMod();
            var seriesDir = Path.Combine(_env.ModPath, "scaffold-mod", "dev");

            File.WriteAllText(Path.Combine(seriesDir, "series.toml"),
                "[series]\nid = \"dev\"\nname = \"Dev\"\nplaylist-incidence = 100\nseries-playlist = \"bgm_dev\"\n\n[[games]]\nid = \"dev\"\nname = \"Dev\"\n\n[default-track-data]\ngame = \"dev\"\n");

            var trackName = TestEnvironment.DevTrackFiles[0].Replace(".flac", ".nus3audio");
            var csvPath = Path.Combine(seriesDir, "tracks.csv");
            File.WriteAllText(csvPath,
                "filename,game,title,author,copyright,record_type,special_category,volume,info1,in_soundtest\n" +
                $"{trackName},dev,My Custom Title,CustomAuthor,CustomCopyright,arrangement,,0.5,,True\n");

            CreateService().Run();

            var content = File.ReadAllText(csvPath);
            Assert.Contains("My Custom Title", content);
            Assert.Contains("CustomAuthor", content);
            Assert.Contains("CustomCopyright", content);
            Assert.Contains("arrangement", content);
            Assert.Contains("0.5", content);
        }

        [Fact]
        public void Scaffold_DoesNotDeleteRowsForExistingTracks()
        {
            _env.CreateUnconfiguredMod();

            // Initial scaffold creates 13 dev rows
            CreateService().Run();
            var csvPath = Path.Combine(_env.ModPath, "scaffold-mod", "dev", "tracks.csv");
            var before = File.ReadAllLines(csvPath);
            Assert.Equal(14, before.Length); // header + 13

            // Second scaffold run with all files still present must not drop any
            CreateService().Run();
            var after = File.ReadAllLines(csvPath);
            Assert.Equal(14, after.Length);
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
