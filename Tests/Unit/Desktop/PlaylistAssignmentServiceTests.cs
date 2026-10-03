using System.Text.RegularExpressions;
using Tests.Helpers;
using UMB.CLI.Desktop;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Unit.Desktop
{
    public class PlaylistAssignmentServiceTests : IDisposable
    {
        private const string MarioCsv = "filename,game,title\na.flac,g,Song A\nb.flac,g,Song B\n";
        private const string ZeldaCsv = "filename,game,title\nc.flac,g,Song C\n";

        private const string MarioToml =
            "[series]\nid = \"mario\"\nname = \"Mario\"\nexisting-series = true\nseries-playlist = \"bgmmario\"\n\n" +
            "[[games]]\nid = \"g\"\nname = \"G\"\n\n" +
            "[[playlists]]\nid = \"bgmsmashmenu\"\nincidence = 10\nsongs = [\"a\"]\n\n" +
            "[default-track-data]\ngame = \"g\"\n";

        private const string ZeldaToml = "[series]\nid = \"zelda\"\nname = \"Zelda\"\n\n[[games]]\nid = \"gz\"\nname = \"GZ\"\n";

        private readonly DesktopWorkspace _ws = new();
        private readonly PlaylistAssignmentService _service;

        public PlaylistAssignmentServiceTests()
        {
            _service = new PlaylistAssignmentService(_ws.MusicOptions());
        }

        public void Dispose() => _ws.Dispose();

        /// <summary>A two-series mod (mario + zelda); returns the mod path.</summary>
        private string SeedMod(string marioToml = MarioToml)
        {
            _ws.WriteSeries("mymod", "mario", MarioCsv, marioToml);
            _ws.WriteSeries("mymod", "zelda", ZeldaCsv, ZeldaToml);
            return Path.Combine(_ws.ModsRoot, "mymod");
        }

        private static PlaylistTarget Playlist(ManagePlaylistsData data, string id) => data.Playlists.Single(p => p.Id == id);

        [Fact]
        public void Load_ListsGamePlaylistsWithFriendlyNames()
        {
            var data = _service.Load(SeedMod());

            Assert.Equal("Mario", Playlist(data, "bgmmario").Name);
            Assert.Equal("Smash Menu", Playlist(data, "bgmsmashmenu").Name);
        }

        [Fact]
        public void Load_ListsEveryModSong()
        {
            var data = _service.Load(SeedMod());

            Assert.Equal(new[] { "a.flac", "b.flac", "c.flac" }, data.Songs.Select(s => s.Filename).OrderBy(f => f));
            Assert.Equal(new ModSong("mario", "Mario", "a.flac", "Song A"), data.Songs.Single(s => s.Filename == "a.flac"));
        }

        [Fact]
        public void Load_ReadsAPlaylistBlockAsPerSongAssignments()
        {
            var menu = Playlist(_service.Load(SeedMod()), "bgmsmashmenu");

            Assert.Equal(1, menu.AssignedCount);
            Assert.Equal(new[] { new PlaylistSongAssignment("mario", "Mario", "a.flac", "Song A", 10) }, menu.Assignments);
        }

        [Fact]
        public void Load_WildcardSongsMeansTheWholeSeries()
        {
            var menu = Playlist(_service.Load(SeedMod(MarioToml.Replace("songs = [\"a\"]", "songs = \"*\""))), "bgmsmashmenu");

            Assert.Equal(new[] { "a.flac", "b.flac" }, menu.Assignments.Select(a => a.Filename).OrderBy(f => f));
        }

        [Fact]
        public void Save_WritesAssignments_ThatRoundTrip()
        {
            var result = _service.Save(SeedMod(), new()
            {
                new("bgmsmashmenu", "mario", "a.flac", 10),
                new("bgmzelda", "zelda", "c.flac", 80)
            });

            Assert.Equal(new[] { "a.flac" }, Playlist(result, "bgmsmashmenu").Assignments.Select(a => a.Filename));
            Assert.Equal(new[] { new PlaylistSongAssignment("zelda", "Zelda", "c.flac", "Song C", 80) }, Playlist(result, "bgmzelda").Assignments);
        }

        [Fact]
        public void Save_WritesOneBlockPerIncidence()
        {
            var modPath = SeedMod();

            _service.Save(modPath, new()
            {
                new("bgmsmashmenu", "mario", "a.flac", 10),
                new("bgmsmashmenu", "mario", "b.flac", 50)
            });

            var toml = File.ReadAllText(Path.Combine(modPath, "mario", "series.toml"));
            Assert.Equal(2, Regex.Matches(toml, @"\[\[playlists\]\]").Count);
            Assert.Contains("incidence = 10", toml);
            Assert.Contains("incidence = 50", toml);
            Assert.Equal(new[]
            {
                new PlaylistSongAssignment("mario", "Mario", "a.flac", "Song A", 10),
                new PlaylistSongAssignment("mario", "Mario", "b.flac", "Song B", 50)
            }, Playlist(_service.Load(modPath), "bgmsmashmenu").Assignments);
        }

        [Fact]
        public void Save_RemovesPlaylistBlocksFromSeriesWithoutAssignments()
        {
            var modPath = SeedMod();

            _service.Save(modPath, new());

            Assert.DoesNotContain("[[playlists]]", File.ReadAllText(Path.Combine(modPath, "mario", "series.toml")));
            Assert.All(_service.Load(modPath).Playlists, p => Assert.Equal(0, p.AssignedCount));
        }

        [Fact]
        public void Save_KeepsTheOtherTables()
        {
            var modPath = SeedMod();

            _service.Save(modPath, new() { new("bgmsmashmenu", "mario", "a.flac", 25) });

            var toml = File.ReadAllText(Path.Combine(modPath, "mario", "series.toml"));
            foreach (var expected in new[] { "id = \"mario\"", "series-playlist = \"bgmmario\"", "[[games]]", "[default-track-data]", "incidence = 25" })
                Assert.Contains(expected, toml);
        }

        [Fact]
        public void RejectsModPathsOutsideTheModsFolder()
        {
            var outside = Path.Combine(_ws.Root, "outside");

            Assert.Equal("Invalid mod path.", Assert.Throws<DesktopApiException>(() => _service.Load(outside)).Message);
            Assert.Equal("Invalid mod path.", Assert.Throws<DesktopApiException>(() => _service.Save(outside, new())).Message);
        }
    }
}
