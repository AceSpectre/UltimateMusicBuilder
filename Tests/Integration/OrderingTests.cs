using Microsoft.Extensions.DependencyInjection;
using Sma5h.Interfaces;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using Tests.Helpers;
using Tomlyn;
using Tomlyn.Model;
using Xunit;

namespace Tests.Integration
{
    /// <summary>
    /// Tests series/track ordering data operations. The actual GUI reordering
    /// (SeriesOrderService, TrackOrderService) requires Avalonia windows,
    /// so we test the data reading/writing that those services operate on.
    /// </summary>
    [Collection("CwdSensitive")]
    public class OrderingTests : IDisposable
    {
        private readonly TestEnvironment _env;

        public OrderingTests()
        {
            _env = new TestEnvironment();
            FolderMusicMod.SeriesFilterByMod = null;
            Sma5hMusic.ExplicitSeriesOrder = null;
        }

        public void Dispose()
        {
            FolderMusicMod.SeriesFilterByMod = null;
            Sma5hMusic.ExplicitSeriesOrder = null;
            _env.Dispose();
        }

        // ── Series ordering ────────────────────────────────────────────────

        [Fact]
        public void SeriesOrderToml_ParsesCorrectly()
        {
            var modDir = _env.CreateConfiguredMod();
            var orderPath = Path.Combine(modDir, "series-order.toml");

            var toml = File.ReadAllText(orderPath);
            var model = Toml.ToModel(toml);
            Assert.True(model.ContainsKey("order"));

            var order = model["order"] as TomlArray;
            Assert.NotNull(order);
            Assert.Contains("dev", order.OfType<string>());
        }

        [Fact]
        public void SeriesOrderToml_CanBeRewritten()
        {
            var modDir = _env.CreateConfiguredMod();
            var orderPath = Path.Combine(modDir, "series-order.toml");

            var newOrder = new[] { "dev", "another-series" };
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("order = [");
            foreach (var id in newOrder)
                sb.AppendLine($"    \"{id}\",");
            sb.AppendLine("]");
            File.WriteAllText(orderPath, sb.ToString());

            var toml = File.ReadAllText(orderPath);
            var model = Toml.ToModel(toml);
            var order = (model["order"] as TomlArray).OfType<string>().ToList();
            Assert.Equal(2, order.Count);
            Assert.Equal("dev", order[0]);
            Assert.Equal("another-series", order[1]);
        }

        // ── Track ordering ─────────────────────────────────────────────────

        [Fact]
        public void TracksCsv_OrderColumnCanBeAdded()
        {
            var modDir = _env.CreateConfiguredMod();
            var csvPath = Path.Combine(modDir, "dev", "tracks.csv");

            var lines = File.ReadAllLines(csvPath).ToList();
            // Add "order" column if not present
            if (!lines[0].Contains(",order"))
            {
                lines[0] += ",order";
                for (int i = 1; i < lines.Count; i++)
                    lines[i] += "," + (i - 1);
            }
            File.WriteAllLines(csvPath, lines);

            var result = File.ReadAllLines(csvPath);
            Assert.Contains("order", result[0]);
            Assert.EndsWith(",0", result[1]);
        }

        [Fact]
        public void SongOrderToml_CanBeCreatedForExistingSeries()
        {
            var modDir = _env.CreateConfiguredMod();
            var songOrderPath = Path.Combine(modDir, "mario", "song_order.toml");

            // Create song_order.toml as TrackOrderService would
            var orderedIds = new[]
            {
                "ui_bgm_flowerhead___somewhat_good__lofi___01_summer",
                "ui_bgm_flowerhead___somewhat_good__lofi___02_rainy",
                "ui_bgm_vanilla_ps01",
                "ui_bgm_flowerhead___somewhat_good__lofi___03_brain"
            };

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("song_order = [");
            foreach (var id in orderedIds)
                sb.AppendLine($"    \"{id}\",");
            sb.AppendLine("]");
            File.WriteAllText(songOrderPath, sb.ToString());

            Assert.True(File.Exists(songOrderPath));
            var toml = File.ReadAllText(songOrderPath);
            var model = Toml.ToModel(toml);
            var order = (model["song_order"] as TomlArray).OfType<string>().ToList();
            Assert.Equal(4, order.Count);
        }

        [Fact]
        public void SongOrderToml_LoadedByFolderMusicMod()
        {
            var modDir = _env.CreateConfiguredMod();
            var songOrderPath = Path.Combine(modDir, "mario", "song_order.toml");

            var orderedIds = new[] { "id_a", "id_b", "id_c" };
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("song_order = [");
            foreach (var id in orderedIds)
                sb.AppendLine($"    \"{id}\",");
            sb.AppendLine("]");
            File.WriteAllText(songOrderPath, sb.ToString());

            var mod = new Sma5h.Mods.Music.MusicMods.FolderMusicMod.FolderMusicMod(
                TestEnvironment.CreateLogger<Sma5h.Mods.Music.Interfaces.IMusicMod>(),
                TestEnvironment.CreateMockAudioMetadata().Object,
                modDir);

            var entries = mod.GetMusicModEntries();
            Assert.True(entries.SeriesSongOrderings.ContainsKey("ui_series_mario"));
            Assert.Equal(3, entries.SeriesSongOrderings["ui_series_mario"].Count);
            Assert.Equal("id_a", entries.SeriesSongOrderings["ui_series_mario"][0]);
        }

        // ── Volume config data ─────────────────────────────────────────────

        [Fact]
        public void TracksCsv_VolumeColumnParsedCorrectly()
        {
            var modDir = _env.CreateConfiguredMod();
            var csvPath = Path.Combine(modDir, "dev", "tracks.csv");

            var mod = new Sma5h.Mods.Music.MusicMods.FolderMusicMod.FolderMusicMod(
                TestEnvironment.CreateLogger<Sma5h.Mods.Music.Interfaces.IMusicMod>(),
                TestEnvironment.CreateMockAudioMetadata().Object,
                modDir);

            var entries = mod.GetMusicModEntries();
            foreach (var prop in entries.BgmPropertyEntries)
                Assert.Equal(1.0f, prop.AudioVolume);
        }

        [Fact]
        public void TracksCsv_VolumeCanBeOverridden()
        {
            var modDir = _env.CreateConfiguredMod();
            var csvPath = Path.Combine(modDir, "dev", "tracks.csv");

            var lines = File.ReadAllLines(csvPath).ToList();
            // Change volume of first track (column index 7)
            var cols = lines[1].Split(',');
            cols[7] = "0.5";
            lines[1] = string.Join(',', cols);
            File.WriteAllLines(csvPath, lines);

            var mod = new Sma5h.Mods.Music.MusicMods.FolderMusicMod.FolderMusicMod(
                TestEnvironment.CreateLogger<Sma5h.Mods.Music.Interfaces.IMusicMod>(),
                TestEnvironment.CreateMockAudioMetadata().Object,
                modDir);

            var entries = mod.GetMusicModEntries();
            var first = entries.BgmPropertyEntries.First();
            Assert.Equal(0.5f, first.AudioVolume);
        }

        // ── In-memory ordering reflected after Init/Build ──────────────────

        [Fact]
        public void SeriesOrder_AppliedToInMemoryState_GammaBeforeDev()
        {
            // series-order.toml sets ["gamma", "dev"]. Build assigns each
            // custom series a DispOrderSound from ExplicitSeriesOrder, so gamma
            // must end up with the smaller DispOrderSound on its SeriesEntry.
            BaselineGenerator.SetupSeriesOrdered(_env);

            using var sp = _env.CreateFullServiceProvider();
            var audioState = sp.GetRequiredService<IAudioStateService>();
            var stateManager = sp.GetRequiredService<IStateManager>();
            var sma5hMod = sp.GetRequiredService<ISma5hMod>();

            BaselineGenerator.ApplyExplicitSeriesOrder(_env);
            stateManager.Init();
            sma5hMod.Init();
            sma5hMod.Build(useCache: false);

            var gammaSeries = audioState.GetSeriesEntries()
                .FirstOrDefault(s => s.UiSeriesId == "ui_series_gamma");
            var devSeries = audioState.GetSeriesEntries()
                .FirstOrDefault(s => s.UiSeriesId == "ui_series_dev");

            Assert.NotNull(gammaSeries);
            Assert.NotNull(devSeries);
            Assert.True(gammaSeries.DispOrderSound < devSeries.DispOrderSound,
                $"gamma DispOrderSound ({gammaSeries.DispOrderSound}) should be lower than " +
                $"dev DispOrderSound ({devSeries.DispOrderSound})");
        }

        [Fact]
        public void TrackOrder_CustomSeriesRespectsCsvOrderColumn()
        {
            BaselineGenerator.SetupTrackOrdered(_env);

            using var sp = _env.CreateFullServiceProvider();
            var audioState = sp.GetRequiredService<IAudioStateService>();
            var stateManager = sp.GetRequiredService<IStateManager>();
            var sma5hMod = sp.GetRequiredService<ISma5hMod>();

            stateManager.Init();
            sma5hMod.Init();
            sma5hMod.Build(useCache: false);

            var devTracks = audioState.GetBgmDbRootEntries()
                .Where(e => e.UiGameTitleId == "ui_gametitle_somewhat_good_karts")
                .OrderBy(e => e.TestDispOrder)
                .ToList();

            Assert.Equal(13, devTracks.Count);
            // Reversed order column means track 13 (Time Trials) should now have
            // the lowest TestDispOrder among dev tracks, track 01 (KARTS!) the highest.
            Assert.Contains("Time Trials", devTracks[0].Title["en_us"]);
            Assert.Contains("KARTS!", devTracks[^1].Title["en_us"]);
        }

        [Fact]
        public void TrackOrder_ExistingSeriesRespectsSongOrderToml()
        {
            BaselineGenerator.SetupTrackOrdered(_env);
            var modDir = Path.Combine(_env.ModPath, "test-mod");

            var mod = new FolderMusicMod(
                TestEnvironment.CreateLogger<IMusicMod>(),
                TestEnvironment.CreateMockAudioMetadata().Object,
                modDir);
            var entries = mod.GetMusicModEntries();

            Assert.True(entries.SeriesSongOrderings.ContainsKey("ui_series_mario"),
                "song_order.toml in mario/ should produce a SeriesSongOrderings entry for ui_series_mario");
            var order = entries.SeriesSongOrderings["ui_series_mario"];
            Assert.Equal(7, order.Count);
            Assert.Contains("ui_bgm_ps01", order); // interleaved vanilla reference survives
            // First modded track in the configured order is 03 brain empty
            Assert.Equal("ui_bgm_flowerhead___somewhat_good__lofi___03_brain_empty", order[0]);
        }
    }
}
