using System.Text.RegularExpressions;
using Tests.Helpers;
using UMB.CLI.Desktop;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Unit.Desktop
{
    public class SeriesOrderServiceTests : IDisposable
    {
        private const string DummyCsv = "filename,title,game\nx.flac,X,Game\n";

        // 1x1 transparent PNG.
        private const string PngDataUrl =
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

        private const string RichToml =
            "[series]\nid = \"a\"\nname = \"A\"\nexisting-series = false\nplaylist-incidence = 100\nseries-playlist = \"bgmmario\"\n\n" +
            "[[games]]\nid = \"g1\"\nname = \"Game One\"\n\n[default-track-data]\ngame = \"g1\"\n";

        private readonly DesktopWorkspace _ws = new();
        private readonly SeriesOrderService _service;
        private readonly string _modPath;

        public SeriesOrderServiceTests()
        {
            _service = new SeriesOrderService(_ws.MusicOptions(), TestEnvironment.CreateLogger<SeriesOrderService>());
            _modPath = _ws.ModDir("mymod");
        }

        public void Dispose() => _ws.Dispose();

        private string WriteCustomSeries(string dir, string toml) => _ws.WriteSeries("mymod", dir, DummyCsv, toml);

        private string SeriesToml(string dir) => File.ReadAllText(Path.Combine(_modPath, dir, "series.toml"));

        private static List<SaveSeriesItem> Order(params string[] ids) => ids.Select(id => new SaveSeriesItem(id, null)).ToList();

        /// <summary>Saves every loaded item with its fields changed by <paramref name="edit"/>.</summary>
        private void SaveEdited(Action<SeriesFields> edit)
        {
            var items = _service.Load(_modPath).Items.Where(i => !i.IsExistingSeries).Select(i =>
            {
                edit(i.Fields);
                return new SaveSeriesItem(i.Id, i.Fields);
            }).ToList();
            _service.Save(_modPath, items);
        }

        private static CreateSeriesInput OneGame(Action<CreateSeriesInput> edit = null)
        {
            var input = new CreateSeriesInput
            {
                SeriesId = "my_series",
                Name = "My Series",
                SeriesPlaylist = "bgm_my_series",
                Games = new List<SeriesGame> { new("first_game", "First Game") }
            };
            edit?.Invoke(input);
            return input;
        }

        // ── Load ─────────────────────────────────────────────────────────────

        [Fact]
        public void Load_ListsCustomSeriesByName_WhenNoOrderFile()
        {
            WriteCustomSeries("first", "id = \"bravo\"\nname = \"Bravo\"\n");
            WriteCustomSeries("second", "id = \"alpha\"\nname = \"Alpha\"\n");

            var data = _service.Load(_modPath);

            Assert.Equal("mymod", data.ModName);
            Assert.False(data.HasSeriesOrder);
            Assert.Equal(2, data.Items.Count(i => !i.IsExistingSeries));
            Assert.Equal(("series:0", "Alpha", "alpha", (string)null, 0),
                (data.Items[0].Id, data.Items[0].Name, data.Items[0].SeriesId, data.Items[0].IconDataUrl, data.Items[0].OriginalIndex));
            Assert.Equal("bravo", data.Items[1].SeriesId);
        }

        [Fact]
        public void Load_ExcludesEtcDotfilesAndFoldersWithoutToml()
        {
            WriteCustomSeries("good", "id = \"good\"\nname = \"Good\"\n");
            WriteCustomSeries("existing", "id = \"ff\"\nname = \"FF\"\nexisting-series = true\n");
            WriteCustomSeries("etcdir", "id = \"etc\"\nname = \"Etc\"\n");
            WriteCustomSeries(".hidden", "id = \"h\"\nname = \"H\"\n");
            _ws.ModDir("mymod", "notoml");

            Assert.Equal(new[] { "good" }, _service.Load(_modPath).Items.Where(i => !i.IsExistingSeries).Select(i => i.SeriesId));
        }

        [Fact]
        public void Load_HonoursSeriesOrder_AppendingUnlistedByName()
        {
            WriteCustomSeries("da", "id = \"a\"\nname = \"A\"\n");
            WriteCustomSeries("db", "id = \"b\"\nname = \"B\"\n");
            WriteCustomSeries("dc", "id = \"c\"\nname = \"C\"\n");
            DesktopWorkspace.WriteFile(_modPath, "series-order.toml", "order = [\n  \"c\",\n  \"a\"\n]\n");

            var data = _service.Load(_modPath);

            Assert.True(data.HasSeriesOrder);
            Assert.Equal(new[] { "c", "a", "b" }, data.Items.Where(i => !i.IsExistingSeries).Select(i => i.SeriesId));
        }

        [Fact]
        public void Load_EmbedsIconAsDataUrl()
        {
            var seriesDir = WriteCustomSeries("p", "id = \"p\"\nname = \"P\"\n");
            var png = new byte[] { 0x89, 0x50, 0x4e, 0x47 };
            File.WriteAllBytes(Path.Combine(seriesDir, "icon.png"), png);

            Assert.Equal("data:image/png;base64," + Convert.ToBase64String(png), _service.Load(_modPath).Items[0].IconDataUrl);
        }

        [Fact]
        public void RejectsModPathsOutsideTheModsFolder()
        {
            var outside = Path.Combine(_ws.Root, "outside");

            Assert.Equal("Invalid mod path.", Assert.Throws<DesktopApiException>(() => _service.Load(outside)).Message);
            Assert.Equal("Invalid mod path.", Assert.Throws<DesktopApiException>(() => _service.Save(outside, new())).Message);
            Assert.Equal("Invalid mod path.", Assert.Throws<DesktopApiException>(() => _service.Create(outside, OneGame())).Message);
            Assert.Equal("Invalid mod path.", Assert.Throws<DesktopApiException>(() => _service.SetIcon(outside, "a", PngDataUrl)).Message);
        }


        [Fact]
        public void Load_IncludesVanillaSeriesWithoutCreatingFolders()
        {
            var splatoon = _service.Load(_modPath).Items.Single(i => i.SeriesId == "splatoon");
            Assert.True(splatoon.IsExistingSeries);
            Assert.Equal("Splatoon", splatoon.Fields.Name);
            Assert.Empty(Directory.GetDirectories(_modPath));
        }

        [Fact]
        public void Save_UnchangedVanillaSeriesDoesNotCreateOverrides()
        {
            var loaded = _service.Load(_modPath);
            _service.Save(_modPath, loaded.Items.Select(i => new SaveSeriesItem(i.Id, i.Fields)).ToList());
            Assert.Empty(Directory.GetDirectories(_modPath));
            Assert.Empty(UMB.CLI.Services.SeriesToml.ReadIdList(Path.Combine(_modPath, "series-order.toml")));
        }

        [Fact]
        public void Save_CreatesVanillaGameAndDefaultsAndPreservesExistingFiles()
        {
            var dir = WriteCustomSeries("splatoon_override", "[series]\nid = \"splatoon\"\nexisting-series = true\nname = \"Splatoon\"\n# retained\ncustom-key = 7\n");
            var originalCsv = File.ReadAllText(Path.Combine(dir, "tracks.csv"));
            var item = _service.Load(_modPath).Items.Single(i => i.SeriesId == "splatoon");
            item.Fields.Games.Add(new("splatoon_3", "Splatoon 3"));
            item.Fields.DefaultGame = "splatoon_3";
            item.Fields.DefaultAuthor = "Composer";
            item.Fields.DefaultVolume = 0.8;
            _service.Save(_modPath, new() { new(item.Id, item.Fields) });

            var reloaded = _service.Load(_modPath).Items.Single(i => i.SeriesId == "splatoon");
            Assert.Contains(new SeriesGame("splatoon_3", "Splatoon 3"), reloaded.Fields.Games);
            Assert.Equal(("splatoon_3", "Composer", 0.8), (reloaded.Fields.DefaultGame, reloaded.Fields.DefaultAuthor, reloaded.Fields.DefaultVolume));
            Assert.Contains("existing-series = true", SeriesToml("splatoon_override"));
            Assert.Contains("# retained\ncustom-key = 7", SeriesToml("splatoon_override"));
            Assert.Equal(originalCsv, File.ReadAllText(Path.Combine(dir, "tracks.csv")));
            Assert.DoesNotContain("splatoon", File.ReadAllText(Path.Combine(_modPath, "series-order.toml")));
        }

        [Fact]
        public void Save_MaterializesOnlyTheEditedVanillaSeries()
        {
            var item = _service.Load(_modPath).Items.Single(i => i.SeriesId == "splatoon");
            item.Fields.Games.Add(new("splatoon_3", "Splatoon 3"));
            item.Fields.DefaultCopyright = "Copyright";
            _service.Save(_modPath, new() { new(item.Id, item.Fields) });
            Assert.Equal(new[] { "splatoon" }, Directory.GetDirectories(_modPath).Select(Path.GetFileName));
            Assert.Contains("existing-series = true", SeriesToml("splatoon"));
            Assert.Equal("Copyright", _service.Load(_modPath).Items.Single(i => i.SeriesId == "splatoon").Fields.DefaultCopyright);
        }

        [Fact]
        public void SetIcon_MaterializesVanillaSeriesAndRetainsItsName()
        {
            Assert.Equal(PngDataUrl, _service.SetIcon(_modPath, "splatoon", PngDataUrl));
            var item = _service.Load(_modPath).Items.Single(i => i.SeriesId == "splatoon");
            Assert.True(item.IsExistingSeries);
            Assert.Equal("Splatoon", item.Name);
            Assert.Equal(PngDataUrl, item.IconDataUrl);
            Assert.Contains("existing-series = true", SeriesToml("splatoon"));
        }

        [Fact]
        public void SetIcon_InvalidVanillaIconDoesNotCreateFiles()
        {
            Assert.Throws<DesktopApiException>(() => _service.SetIcon(_modPath, "splatoon", "data:image/png;base64,a"));
            Assert.Empty(Directory.GetDirectories(_modPath));
        }

        [Fact]
        public void Save_VanillaFolderWithoutSettingsPreservesExistingData()
        {
            var dir = _ws.ModDir("mymod", "splatoon");
            File.WriteAllText(Path.Combine(dir, "tracks.csv"), DummyCsv);
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "keep");
            var item = _service.Load(_modPath).Items.Single(i => i.SeriesId == "splatoon");
            item.Fields.DefaultAuthor = "Composer";
            _service.Save(_modPath, new() { new(item.Id, item.Fields) });
            Assert.Equal(DummyCsv, File.ReadAllText(Path.Combine(dir, "tracks.csv")));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(dir, "notes.txt")));
        }

        [Fact]
        public void SetIcon_RejectsVanillaFolderUsedByAnotherSeries()
        {
            WriteCustomSeries("splatoon", "[series]\nid = \"another_series\"\nname = \"Another Series\"\n");
            Assert.Throws<DesktopApiException>(() => _service.SetIcon(_modPath, "splatoon", PngDataUrl));
            Assert.Contains("another_series", SeriesToml("splatoon"));
            Assert.False(File.Exists(Path.Combine(_modPath, "splatoon", "icon.png")));
        }

        [Fact]
        public void Create_RejectsVanillaSeriesId()
        {
            Assert.Throws<DesktopApiException>(() => _service.Create(_modPath, OneGame(i => i.SeriesId = "splatoon")));
            Assert.Empty(Directory.GetDirectories(_modPath));
        }

        // ── Create ───────────────────────────────────────────────────────────

        [Fact]
        public void Create_WritesSeriesTomlAndHeaderOnlyCsv()
        {
            var result = _service.Create(_modPath, OneGame());

            Assert.Contains("my_series", result.Items.Where(i => !i.IsExistingSeries).Select(i => i.SeriesId));
            var toml = SeriesToml("my_series");
            Assert.Contains("id = \"my_series\"", toml);
            Assert.Contains("name = \"My Series\"", toml);
            Assert.Contains("playlist-incidence = 100", toml);
            Assert.Contains("series-playlist = \"bgm_my_series\"", toml);
            Assert.Contains("[[games]]", toml);
            Assert.Contains("id = \"first_game\"", toml);
            Assert.Equal("filename,game,title,author,copyright,record_type,special_category,volume,info1,in_soundtest",
                File.ReadAllText(Path.Combine(_modPath, "my_series", "tracks.csv")).Trim());
        }

        [Fact]
        public void Create_MakesTheFirstGameTheDefaultGame()
        {
            _service.Create(_modPath, OneGame(i => i.Games = new() { new("first_game", "First"), new("second_game", "Second") }));

            var fields = _service.Load(_modPath).Items.Single(i => i.SeriesId == "my_series").Fields;
            Assert.Equal("first_game", fields.DefaultGame);
            Assert.Equal(new[] { new SeriesGame("first_game", "First"), new SeriesGame("second_game", "Second") }, fields.Games);
            Assert.Equal("original", fields.DefaultRecordType);
            Assert.Equal(1, fields.DefaultVolume);
        }

        [Fact]
        public void Create_OmitsBlankSeriesPlaylist()
        {
            _service.Create(_modPath, OneGame(i => i.SeriesPlaylist = ""));
            Assert.DoesNotContain("series-playlist", SeriesToml("my_series"));
        }

        [Fact]
        public void Create_RejectsAnExistingSeriesId()
        {
            WriteCustomSeries("my_series", "id = \"my_series\"\nname = \"Existing\"\n");
            Assert.Contains("already exists", Assert.Throws<DesktopApiException>(() => _service.Create(_modPath, OneGame())).Message);
        }

        [Theory]
        [InlineData("Bad Id!")]
        [InlineData("")]
        public void Create_RejectsAnInvalidSeriesId(string seriesId)
        {
            Assert.Throws<DesktopApiException>(() => _service.Create(_modPath, OneGame(i => i.SeriesId = seriesId)));
        }

        [Fact]
        public void Create_RejectsTheReservedEtcId()
        {
            Assert.Contains("reserved", Assert.Throws<DesktopApiException>(() => _service.Create(_modPath, OneGame(i => i.SeriesId = "etc"))).Message);
        }

        [Fact]
        public void Create_RejectsABlankName()
        {
            Assert.Throws<DesktopApiException>(() => _service.Create(_modPath, OneGame(i => i.Name = "  ")));
        }

        [Fact]
        public void Create_RequiresAGame()
        {
            Assert.Contains("game", Assert.Throws<DesktopApiException>(() => _service.Create(_modPath, OneGame(i => i.Games = new()))).Message);
        }

        [Fact]
        public void Create_WritesTheIcon()
        {
            _service.Create(_modPath, OneGame(i => i.IconDataUrl = PngDataUrl));
            Assert.Equal(PngDataUrl, _service.Load(_modPath).Items.Single(i => i.SeriesId == "my_series").IconDataUrl);
        }

        [Fact]
        public void Create_RejectsANonPngIcon()
        {
            Assert.Contains("PNG", Assert.Throws<DesktopApiException>(() =>
                _service.Create(_modPath, OneGame(i => i.IconDataUrl = "data:image/jpeg;base64,AAAA"))).Message);
        }

        // ── SetIcon ──────────────────────────────────────────────────────────

        [Fact]
        public void SetIcon_WritesTheIconAndReturnsItsDataUrl()
        {
            WriteCustomSeries("da", "id = \"a\"\nname = \"A\"\n");

            Assert.Equal(PngDataUrl, _service.SetIcon(_modPath, "a", PngDataUrl));
            Assert.Equal(PngDataUrl, _service.Load(_modPath).Items[0].IconDataUrl);
        }

        [Fact]
        public void SetIcon_RejectsAnUnknownSeries()
        {
            WriteCustomSeries("da", "id = \"a\"\nname = \"A\"\n");
            Assert.Contains("not found", Assert.Throws<DesktopApiException>(() => _service.SetIcon(_modPath, "nope", PngDataUrl)).Message);
        }

        [Fact]
        public void SetIcon_RejectsANonPngIcon()
        {
            WriteCustomSeries("da", "id = \"a\"\nname = \"A\"\n");
            Assert.Contains("PNG", Assert.Throws<DesktopApiException>(() => _service.SetIcon(_modPath, "a", "data:text/plain;base64,AAAA")).Message);
        }

        // ── Save ─────────────────────────────────────────────────────────────

        [Fact]
        public void Save_WritesTheRequestedOrder_AppendingOmittedSeries()
        {
            WriteCustomSeries("da", "id = \"a\"\nname = \"A\"\n");
            WriteCustomSeries("db", "id = \"b\"\nname = \"B\"\n");
            WriteCustomSeries("dc", "id = \"c\"\nname = \"C\"\n");

            var result = _service.Save(_modPath, Order("series:2", "series:0"));

            Assert.True(result.HasSeriesOrder);
            Assert.Equal(new[] { "c", "a", "b" }, result.Items.Where(i => !i.IsExistingSeries).Select(i => i.SeriesId));
            var written = File.ReadAllText(Path.Combine(_modPath, "series-order.toml"));
            Assert.Contains("# Custom series display order", written);
            Assert.Contains("order = [", written);
            Assert.Contains("    \"c\",", written);
            Assert.Contains("    \"a\",", written);
            Assert.Contains("    \"b\",", written);
        }

        [Fact]
        public void Load_ParsesSeriesAndDefaultTrackDataFields()
        {
            WriteCustomSeries("da", RichToml);

            var fields = _service.Load(_modPath).Items[0].Fields;

            Assert.Equal("A", fields.Name);
            Assert.Equal("bgmmario", fields.SeriesPlaylist);
            Assert.Equal(100, fields.PlaylistIncidence);
            Assert.Equal(new[] { new SeriesGame("g1", "Game One") }, fields.Games);
            Assert.Equal("g1", fields.DefaultGame);
            Assert.Equal("", fields.DefaultAuthor);
            Assert.Equal("", fields.DefaultCopyright);
            Assert.Equal("original", fields.DefaultRecordType);
            Assert.Equal(1, fields.DefaultVolume);
        }

        [Fact]
        public void Save_EditsAndAddsGames_PreservingIdAndOtherTables()
        {
            WriteCustomSeries("da", RichToml);

            SaveEdited(f => f.Games = new() { new("g1", "Game One Renamed"), new("g2", "Game Two") });

            var toml = SeriesToml("da");
            Assert.Contains("name = \"Game One Renamed\"", toml);
            Assert.Contains("id = \"g2\"", toml);
            Assert.Contains("name = \"Game Two\"", toml);
            Assert.Contains("id = \"a\"", toml);
            Assert.Contains("[default-track-data]", toml);
            Assert.Equal(2, Regex.Matches(toml, @"\[\[games\]\]").Count);
            Assert.Equal(new[] { new SeriesGame("g1", "Game One Renamed"), new SeriesGame("g2", "Game Two") },
                _service.Load(_modPath).Items[0].Fields.Games);
        }

        [Fact]
        public void Save_InsertsGamesAfterTheSeriesTable_WhenThereWereNone()
        {
            WriteCustomSeries("da", "[series]\nid = \"a\"\nname = \"A\"\n\n[default-track-data]\ngame = \"x\"\n");
            Assert.Empty(_service.Load(_modPath).Items[0].Fields.Games);

            SaveEdited(f => f.Games = new() { new("g1", "G1") });

            var toml = SeriesToml("da");
            Assert.Contains("[[games]]", toml);
            Assert.Contains("id = \"g1\"", toml);
            Assert.True(toml.IndexOf("[[games]]") < toml.IndexOf("[default-track-data]"));
        }

        [Fact]
        public void Save_RemovesAllGames_WhenCleared()
        {
            WriteCustomSeries("da", RichToml);

            SaveEdited(f => f.Games = new());

            var toml = SeriesToml("da");
            Assert.DoesNotContain("[[games]]", toml);
            Assert.Contains("[series]", toml);
            Assert.Contains("[default-track-data]", toml);
        }

        [Fact]
        public void Save_WritesSeriesFields_PreservingIdAndOtherTables()
        {
            WriteCustomSeries("da", RichToml);

            SaveEdited(f =>
            {
                f.Name = "Renamed";
                f.SeriesPlaylist = "bgmother";
                f.PlaylistIncidence = 50;
            });

            var toml = SeriesToml("da");
            Assert.Contains("name = \"Renamed\"", toml);
            Assert.Contains("series-playlist = \"bgmother\"", toml);
            Assert.Contains("playlist-incidence = 50", toml);
            Assert.Contains("id = \"a\"", toml);
            Assert.Contains("[[games]]", toml);
            Assert.Contains("[default-track-data]", toml);
            var reloaded = _service.Load(_modPath).Items[0].Fields;
            Assert.Equal(("Renamed", "bgmother", 50), (reloaded.Name, reloaded.SeriesPlaylist, reloaded.PlaylistIncidence));
        }

        [Fact]
        public void Save_DropsSeriesPlaylist_WhenCleared()
        {
            WriteCustomSeries("da", RichToml);

            SaveEdited(f => f.SeriesPlaylist = "");

            Assert.DoesNotContain("series-playlist", SeriesToml("da"));
        }

        [Fact]
        public void Save_WritesDefaultTrackData()
        {
            WriteCustomSeries("da", RichToml);

            SaveEdited(f =>
            {
                f.DefaultGame = "g1";
                f.DefaultAuthor = "Nobuo";
                f.DefaultRecordType = "arrange";
                f.DefaultVolume = 0.8;
            });

            var toml = SeriesToml("da");
            Assert.Contains("author = \"Nobuo\"", toml);
            Assert.Contains("record-type = \"arrange\"", toml);
            Assert.Contains("volume = 0.8", toml);
            var reloaded = _service.Load(_modPath).Items[0].Fields;
            Assert.Equal(("Nobuo", "arrange", 0.8), (reloaded.DefaultAuthor, reloaded.DefaultRecordType, reloaded.DefaultVolume));
        }

        [Fact]
        public void Save_CreatesDefaultTrackData_WhenADefaultIsSet()
        {
            WriteCustomSeries("da", "[series]\nid = \"a\"\nname = \"A\"\n");

            SaveEdited(f => f.DefaultAuthor = "Composer");

            var toml = SeriesToml("da");
            Assert.Contains("[default-track-data]", toml);
            Assert.Contains("author = \"Composer\"", toml);
        }

        [Fact]
        public void Save_LeavesDefaultTrackDataOut_WhenNoDefaultsAreSet()
        {
            WriteCustomSeries("da", "[series]\nid = \"a\"\nname = \"A\"\n");

            SaveEdited(f => f.Name = "A2");

            Assert.DoesNotContain("[default-track-data]", SeriesToml("da"));
        }
    }
}
