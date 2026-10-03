using Moq;
using Tests.Helpers;
using UMB.CLI.Desktop;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Unit.Desktop
{
    public class SongPresetTests : IDisposable
    {
        private const string Csv = "filename,game,title,volume,info1,in_soundtest\nold.flac,g2,Existing,0.75,target.flac,False\n";
        private const string Toml = "# Keep this comment\n[series]\nid = \"s\"\nname = \"Series\"\nplaylist-incidence = 100\ncustom = \"keep\"\n\n"
            + "[[games]]\nid = \"g1\"\nname = \"One\"\n\n[[games]]\nid = \"g2\"\nname = \"Two\"\n\n[[games]]\nid = \"g3\"\nname = \"Three\"\n\n"
            + "[default-track-data]\ngame = \"g1\"\nauthor = \"Series author\"\ncopyright = \"Series copyright\"\nrecord-type = \"original\"\nvolume = 0.8\n\n"
            + "[[playlists]]\nid = \"stage\"\nsongs = \"*\"\n";
        private readonly DesktopWorkspace _ws = new();
        private readonly TrackOrderService _service;

        public SongPresetTests()
        {
            var catalog = new VanillaCatalogService(_ws.CoreOptions(), new Mock<IServiceProvider>().Object);
            _service = new TrackOrderService(_ws.MusicOptions(), catalog, TestEnvironment.CreateLogger<TrackOrderService>());
        }

        public void Dispose() => _ws.Dispose();

        private static DefaultTrackData Preset(string game, string author = "Game author", float volume = 1.4f) => new()
        {
            Game = game, Author = author, Copyright = "Game copyright", RecordType = "arrange", Volume = volume
        };

        private string CreateSeries() => _ws.WriteSeries("m", "s", Csv, Toml);

        private void Save(string path, params DefaultTrackData[] presets) =>
            _service.SavePresets(path, _service.Load(path).DefaultTrackData, presets.ToList());

        [Fact]
        public void SavePresets_RoundTripsMultipleGamesAndEscapedFields_PreservesOtherTables()
        {
            var path = CreateSeries();
            Save(path, Preset("g1", "Composer \"One\"\\Second\nLine"), Preset("g2"));

            var data = _service.Load(path);
            Assert.Equal(new[] { "g1", "g2" }, data.SongPresets.Select(p => p.Game));
            Assert.Equal("Composer \"One\"\\Second\nLine", data.SongPresets[0].Author);
            Assert.Equal(1.4f, data.SongPresets[1].Volume);
            var text = File.ReadAllText(Path.Combine(path, "series.toml"));
            Assert.Contains("# Keep this comment", text);
            Assert.Contains("custom = \"keep\"", text);
            Assert.Contains("[[playlists]]\nid = \"stage\"\nsongs = \"*\"", text);
            Assert.Equal(Csv, File.ReadAllText(Path.Combine(path, "tracks.csv")));
            Save(path);
            var removed = File.ReadAllText(Path.Combine(path, "series.toml"));
            Assert.DoesNotContain("[[song-presets]]", removed);
            Assert.Equal(new[] { "g1", "g2", "g3" }, _service.Load(path).Games.Select(g => g.Id));
            Assert.Contains("[[playlists]]\nid = \"stage\"\nsongs = \"*\"", removed);
        }

        [Fact]
        public void ApplyPreset_UsesGameMetadataAndVolume_PreservesOtherSongFieldsAndPersistsOnSave()
        {
            var path = CreateSeries();
            Save(path, Preset("g2"));
            var item = _service.Load(path).Items.Single();
            var fields = _service.ApplyPreset(path, item.Fields, "g2", false);
            Assert.Equal(("g2", "Game author", "Game copyright", "arrange", (float?)1.4f),
                (fields.Game, fields.Author, fields.Copyright, fields.RecordType, fields.Volume));
            Assert.Equal(("Existing", "target.flac", "False"), (fields.Title, fields.Info1, fields.InSoundtest));

            _service.Save(path, new() { new SaveTrackItem(item.Id, fields) });
            Assert.Equal(1.4f, _service.Load(path).Items.Single().Fields.Volume);
        }

        [Fact]
        public void ApplyPreset_SeriesDefaultsCanBeChosenExplicitly_AndAbsentGamePresetFallsBack()
        {
            var path = CreateSeries();
            Save(path, Preset("g2"));
            var fields = _service.Load(path).Items.Single().Fields;
            var fallback = _service.ApplyPreset(path, fields, "g3", false);
            Assert.Equal(("g3", "Series author", (float?)0.8f), (fallback.Game, fallback.Author, fallback.Volume));
            var series = _service.ApplyPreset(path, fields, "g2", true);
            Assert.Equal(("g1", "Series author", "original", (float?)0.8f),
                (series.Game, series.Author, series.RecordType, series.Volume));

            Save(path);
            Assert.Empty(_service.Load(path).SongPresets);
            Assert.Equal("Series author", _service.ApplyPreset(path, fields, "g2", false).Author);
        }

        [Fact]
        public void Scaffold_ImportsWithDefaultGamePreset_AndDoesNotChangeExistingRows()
        {
            // Scaffold's normal TOML parser rejects unrelated extension keys.
            var path = _ws.WriteSeries("m", "s", Csv, Toml.Replace("custom = \"keep\"\n", ""));
            Save(path, Preset("g1"));
            File.WriteAllBytes(Path.Combine(path, "new.nus3audio"), Array.Empty<byte>());
            var scaffold = new ScaffoldService(_ws.MusicOptions(), TestEnvironment.CreateMockAudioStateService().Object,
                TestEnvironment.CreateLogger<ScaffoldService>());
            scaffold.Run();

            var data = _service.Load(path);
            var imported = data.Items.Single(i => i.Filename == "new.nus3audio").Fields;
            Assert.Equal(("g1", "Game author", "Game copyright", "arrange", (float?)1.4f),
                (imported.Game, imported.Author, imported.Copyright, imported.RecordType, imported.Volume));
            var existing = data.Items.Single(i => i.Filename == "old.flac").Fields;
            Assert.Equal(("Existing", "g2", (float?)0.75f), (existing.Title, existing.Game, existing.Volume));

            Save(path);
            File.WriteAllBytes(Path.Combine(path, "fallback.nus3audio"), Array.Empty<byte>());
            scaffold.Run();
            Assert.Equal("Series author", _service.Load(path).Items.Single(i => i.Filename == "fallback.nus3audio").Fields.Author);
        }

        [Fact]
        public void SavePresets_RejectsInvalidInputsBeforeWriting()
        {
            var path = CreateSeries();
            var tomlPath = Path.Combine(path, "series.toml");
            foreach (var presets in new[]
            {
                new[] { Preset("g1"), Preset("g1") }, new[] { Preset("unknown") },
                new[] { Preset("g1", volume: -1) }, new[] { Preset("g1", volume: float.NaN) }
            })
            {
                Assert.Throws<DesktopApiException>(() => Save(path, presets));
                Assert.Equal(Toml, File.ReadAllText(tomlPath));
            }
        }

        [Fact]
        public void Save_WithoutVolumeLeavesExistingVolumeUnchanged()
        {
            var path = CreateSeries();
            var item = _service.Load(path).Items.Single();
            item.Fields.Volume = null;
            item.Fields.Title = "Edited";
            _service.Save(path, new() { new SaveTrackItem(item.Id, item.Fields) });
            Assert.Equal(0.75f, _service.Load(path).Items.Single().Fields.Volume);
        }

        [Fact]
        public void PresetOperations_RejectPathsOutsideMods()
        {
            var outside = Path.Combine(_ws.Root, "outside");
            Assert.Throws<DesktopApiException>(() => _service.SavePresets(outside, new(), new()));
            Assert.Throws<DesktopApiException>(() => _service.ApplyPreset(outside, new(), "g1", false));
        }
    }
}
