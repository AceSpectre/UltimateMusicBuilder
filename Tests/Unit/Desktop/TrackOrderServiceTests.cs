using Moq;
using Tests.Helpers;
using UMB.CLI.Desktop;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Unit.Desktop
{
    public class TrackOrderServiceTests : IDisposable
    {
        private const string BasicCsv =
            "filename,title,game\n" +
            "Mass Destruction.flac,Mass Destruction,Persona 3\n" +
            "reach-out.flac,Reach Out to the Truth,Persona 4\n";

        private const string FullCsv =
            "filename,game,title,author,copyright,record_type,special_category,volume,info1,in_soundtest\n" +
            "a.nus3audio,g1,Song A,Auth,Copy,original,sf_situationlink,1,a_pinch.nus3audio,True\n" +
            "a_pinch.nus3audio,g1,Song A Pinch,,,original,,1,,True\n" +
            "b.nus3audio,g2,Song B,,,arrange,,1,,False\n";

        private const string SeriesTomlWithGames =
            "[series]\nid = \"s\"\nexisting-series = true\n\n" +
            "[[games]]\nid = \"g1\"\nname = \"Game One\"\n\n" +
            "[[games]]\nid = \"g2\"\nname = \"Game Two\"\n";

        private readonly DesktopWorkspace _ws = new();
        private readonly TrackOrderService _service;

        public TrackOrderServiceTests()
        {
            // No game files in the workspace, so the catalog is empty and never touches the provider.
            var catalog = new VanillaCatalogService(_ws.CoreOptions(), new Mock<IServiceProvider>().Object);
            _service = new TrackOrderService(_ws.MusicOptions(), catalog, TestEnvironment.CreateLogger<TrackOrderService>());
        }

        public void Dispose() => _ws.Dispose();

        private static List<SaveTrackItem> Order(params string[] ids) => ids.Select(id => new SaveTrackItem(id, null)).ToList();

        private static List<SaveTrackItem> PayloadFrom(TrackOrderData data) => data.Items.Select(i => new SaveTrackItem(i.Id, i.Fields)).ToList();

        // ── Load ─────────────────────────────────────────────────────────────

        [Fact]
        public void Load_MapsRowsToItemsWithDerivedBgmIds()
        {
            var data = _service.Load(_ws.WriteSeries("persona", "persona", BasicCsv));

            Assert.Equal(2, data.Items.Count);
            var first = data.Items[0];
            Assert.Equal(("mod:0", "Mass Destruction", "Persona 3 - Mass Destruction.flac", "ui_bgm_mass_destruction", false, (int?)0),
                (first.Id, first.Title, first.Subtitle, first.BgmId, first.IsLocked, first.OriginalIndex));
            Assert.Equal("ui_bgm_reach_out", data.Items[1].BgmId);
        }

        [Fact]
        public void Load_TitleFallsBackToFilenameThenTrackNumber()
        {
            var data = _service.Load(_ws.WriteSeries("m", "s", "filename,title,game\nonly-file.flac,,\n,,\n"));

            Assert.Equal("only-file.flac", data.Items[0].Title);
            Assert.Equal("Track 2", data.Items[1].Title);
        }

        [Fact]
        public void Load_OmitsGameFromSubtitleWhenAbsent()
        {
            Assert.Equal("song.flac", _service.Load(_ws.WriteSeries("m", "s", "filename,title,game\nsong.flac,Song,\n")).Items[0].Subtitle);
        }

        [Fact]
        public void Load_ReadsExistingSeriesFlag()
        {
            var seriesPath = _ws.WriteSeries("m", "s", BasicCsv, "id = \"final_fantasy\"\nexisting-series = true\n");
            Assert.True(_service.Load(seriesPath).IsExistingSeries);
        }

        [Fact]
        public void Load_ExistingSeriesDefaultsToFalseWithoutSeriesToml()
        {
            Assert.False(_service.Load(_ws.WriteSeries("m", "s", BasicCsv)).IsExistingSeries);
        }

        [Fact]
        public void Load_SortsByOrderColumn_UnorderedLast()
        {
            var csv = "filename,title,game,order\na.flac,A,,2\nb.flac,B,,\nc.flac,C,,0\n";
            Assert.Equal(new[] { "C", "A", "B" }, _service.Load(_ws.WriteSeries("m", "s", csv)).Items.Select(i => i.Title));
        }

        [Fact]
        public void Load_SongOrderKeepsVanillaEntriesLocked_AndMatchesModsByBgmId()
        {
            var songOrder = "song_order = [\n  \"ui_bgm_vanilla_one\",\n  \"ui_bgm_mass_destruction\",\n  \"ui_bgm_vanilla_two\"\n]\n";
            var data = _service.Load(_ws.WriteSeries("persona", "persona", BasicCsv, "existing-series = true\n", songOrder));

            Assert.True(data.HasSongOrder);
            Assert.Equal(new[] { "ui_bgm_vanilla_one", "ui_bgm_mass_destruction", "ui_bgm_vanilla_two", "ui_bgm_reach_out" },
                data.Items.Select(i => i.BgmId));
            Assert.True(data.Items[0].IsLocked);
            Assert.Null(data.Items[0].OriginalIndex);
            Assert.Equal("Vanilla One", data.Items[0].Title);
            Assert.False(data.Items[1].IsLocked);
            Assert.Equal(0, data.Items[1].OriginalIndex);
        }

        [Fact]
        public void RejectsSeriesPathsOutsideTheModsFolder()
        {
            _ws.WriteSeries("m", "s", BasicCsv);
            var outside = Path.Combine(_ws.Root, "not-mods");

            Assert.Equal("Invalid series path.", Assert.Throws<DesktopApiException>(() => _service.Load(outside)).Message);
            Assert.Equal("Invalid series path.", Assert.Throws<DesktopApiException>(() => _service.Save(outside, new())).Message);
        }

        // ── Save ─────────────────────────────────────────────────────────────

        [Fact]
        public void Save_RewritesCsvWithOrderColumn()
        {
            var seriesPath = _ws.WriteSeries("m", "s", BasicCsv);

            _service.Save(seriesPath, Order("mod:1", "mod:0"));

            Assert.Contains("order", File.ReadLines(Path.Combine(seriesPath, "tracks.csv")).First());
            Assert.Equal(new[] { "Reach Out to the Truth", "Mass Destruction" }, _service.Load(seriesPath).Items.Select(i => i.Title));
        }

        [Fact]
        public void Save_AppendsOmittedRows()
        {
            var seriesPath = _ws.WriteSeries("m", "s", BasicCsv);

            _service.Save(seriesPath, Order("mod:1"));

            Assert.Equal(new[] { "Reach Out to the Truth", "Mass Destruction" }, _service.Load(seriesPath).Items.Select(i => i.Title));
        }

        [Fact]
        public void Save_RoundTripsCommasInTitles()
        {
            var seriesPath = _ws.WriteSeries("m", "s", "filename,title,game\nsong.flac,\"Hello, World\",Game\n");

            _service.Save(seriesPath, Order("mod:0"));

            Assert.Equal("Hello, World", _service.Load(seriesPath).Items[0].Title);
        }

        [Fact]
        public void Save_WritesSongOrderForExistingSeries_WithVanillaAndModTags()
        {
            var seriesPath = _ws.WriteSeries("persona", "persona", BasicCsv, "existing-series = true\n",
                "song_order = [\n  \"ui_bgm_vanilla_one\",\n  \"ui_bgm_mass_destruction\"\n]\n");

            _service.Save(seriesPath, Order("mod:0", "vanilla:0:ui_bgm_vanilla_one", "mod:1"));

            var written = File.ReadAllText(Path.Combine(seriesPath, "song_order.toml"));
            Assert.Contains("song_order = [", written);
            Assert.Contains("\"ui_bgm_mass_destruction\"", written);
            Assert.Contains("# mod", written);
            Assert.Contains("\"ui_bgm_vanilla_one\"", written);
            Assert.Contains("# vanilla", written);
        }

        [Fact]
        public void Save_DoesNotWriteSongOrderForCustomSeries()
        {
            var seriesPath = _ws.WriteSeries("m", "s", BasicCsv, "existing-series = false\n");

            _service.Save(seriesPath, Order("mod:0", "mod:1"));

            Assert.False(File.Exists(Path.Combine(seriesPath, "song_order.toml")));
        }

        // ── Track fields ─────────────────────────────────────────────────────

        [Fact]
        public void Load_ReadsGamesFromSeriesToml()
        {
            var data = _service.Load(_ws.WriteSeries("m", "s", FullCsv, SeriesTomlWithGames));
            Assert.Equal(new[] { new SeriesGame("g1", "Game One"), new SeriesGame("g2", "Game Two") }, data.Games);
        }

        [Fact]
        public void Load_HasNoGamesWithoutSeriesToml()
        {
            Assert.Empty(_service.Load(_ws.WriteSeries("m", "s", FullCsv)).Games);
        }

        [Fact]
        public void Load_ReadsDefaultTrackData()
        {
            var toml = SeriesTomlWithGames +
                "\n[default-track-data]\ngame = \"g2\"\nauthor = \"Nobuo\"\ncopyright = \"Square\"\nrecord-type = \"arrange\"\nvolume = 1.0\n";

            var defaults = _service.Load(_ws.WriteSeries("m", "s", FullCsv, toml)).DefaultTrackData;

            Assert.Equal(("g2", "Nobuo", "Square", "arrange"), (defaults.Game, defaults.Author, defaults.Copyright, defaults.RecordType));
        }

        [Fact]
        public void Load_DefaultTrackDataIsNullWithoutTheTable()
        {
            Assert.Null(_service.Load(_ws.WriteSeries("m", "s", FullCsv, SeriesTomlWithGames)).DefaultTrackData);
        }

        [Fact]
        public void Load_ExposesEditableFields()
        {
            var item = _service.Load(_ws.WriteSeries("m", "s", FullCsv)).Items[0];

            Assert.Equal("a.nus3audio", item.Filename);
            var f = item.Fields;
            Assert.Equal(("Song A", "g1", "Auth", "Copy", "original", "sf_situationlink", "a_pinch.nus3audio", "True"),
                (f.Title, f.Game, f.Author, f.Copyright, f.RecordType, f.SpecialCategory, f.Info1, f.InSoundtest));
        }

        [Fact]
        public void Load_MarksRowsReferencedByInfo1AsPinchTargets()
        {
            var items = _service.Load(_ws.WriteSeries("m", "s", FullCsv)).Items;

            Assert.True(items.Single(i => i.Filename == "a_pinch.nus3audio").IsPinchTarget);
            Assert.False(items.Single(i => i.Filename == "a.nus3audio").IsPinchTarget);
        }

        [Fact]
        public void Save_RoundTripsEditedFields()
        {
            var seriesPath = _ws.WriteSeries("m", "s", FullCsv);
            var items = PayloadFrom(_service.Load(seriesPath));
            var b = items.Single(i => i.Id == "mod:2").Fields;
            b.Title = "Renamed B";
            b.Game = "g1";
            b.RecordType = "new_arrange";
            b.InSoundtest = "True";

            _service.Save(seriesPath, items);

            var reloaded = _service.Load(seriesPath).Items.Single(i => i.Filename == "b.nus3audio").Fields;
            Assert.Equal(("Renamed B", "g1", "new_arrange", "True"), (reloaded.Title, reloaded.Game, reloaded.RecordType, reloaded.InSoundtest));
            Assert.Contains("Renamed B", File.ReadAllText(Path.Combine(seriesPath, "tracks.csv")));
        }

        [Fact]
        public void Save_SetsAndClearsThePinchCouplingTogether()
        {
            var seriesPath = _ws.WriteSeries("m", "s", FullCsv);
            void SetPinch(string info1, string category)
            {
                var items = PayloadFrom(_service.Load(seriesPath));
                var b = items.Single(i => i.Id == "mod:2").Fields;
                b.Info1 = info1;
                b.SpecialCategory = category;
                _service.Save(seriesPath, items);
            }
            TrackFields BFields() => _service.Load(seriesPath).Items.Single(i => i.Filename == "b.nus3audio").Fields;

            SetPinch("a_pinch.nus3audio", "sf_situationlink");
            Assert.Equal(("a_pinch.nus3audio", "sf_situationlink"), (BFields().Info1, BFields().SpecialCategory));

            SetPinch("", "");
            Assert.Equal(("", ""), (BFields().Info1, BFields().SpecialCategory));
        }

        [Fact]
        public void Save_KeepsANonPinchSpecialCategory()
        {
            var seriesPath = _ws.WriteSeries("m", "s", "filename,game,title,special_category,in_soundtest\nx.flac,g1,X,mario_3dland_scenelink,True\n");
            var items = PayloadFrom(_service.Load(seriesPath));
            items[0].Fields.Title = "X edited";

            _service.Save(seriesPath, items);

            var fields = _service.Load(seriesPath).Items[0].Fields;
            Assert.Equal(("mario_3dland_scenelink", "X edited"), (fields.SpecialCategory, fields.Title));
        }

        [Fact]
        public void Save_KeepsAVanillaPinchReference_WithoutMarkingATarget()
        {
            var seriesPath = _ws.WriteSeries("m", "s", FullCsv);
            var items = PayloadFrom(_service.Load(seriesPath));
            var b = items.Single(i => i.Id == "mod:2").Fields;
            b.Info1 = "info_bgm_some_vanilla_song";
            b.SpecialCategory = "sf_situationlink";

            _service.Save(seriesPath, items);

            var reloaded = _service.Load(seriesPath).Items;
            var row = reloaded.Single(i => i.Filename == "b.nus3audio");
            Assert.Equal(("info_bgm_some_vanilla_song", "sf_situationlink"), (row.Fields.Info1, row.Fields.SpecialCategory));
            Assert.Contains(reloaded, i => i.IsPinchTarget);
            Assert.False(row.IsPinchTarget);
        }
    }
}
