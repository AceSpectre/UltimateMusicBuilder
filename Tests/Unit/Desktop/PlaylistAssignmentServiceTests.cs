using Tests.Helpers;
using UMB.CLI.Desktop;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Unit.Desktop
{
    public class PlaylistAssignmentServiceTests : IDisposable
    {
        private const string MarioCsv = "filename,game,title\na.flac,g,Song A\nb.flac,g,Song B\n";
        private const string CupheadCsv = "filename,game,title\nc.flac,gc,Song C\n";

        private const string MarioToml =
            "[series]\nid = \"mario\"\nname = \"Mario\"\nexisting-series = true\n\n" +
            "[[games]]\nid = \"g\"\nname = \"G\"\n\n" +
            "[[playlists]]\nid = \"bgmsmashmenu\"\nincidence = 10\nsongs = [\"a\"]\n\n" +
            "[default-track-data]\ngame = \"g\"\n";

        private const string CupheadToml = "[series]\nid = \"cuphead\"\nname = \"Cuphead\"\n\n[[games]]\nid = \"gc\"\nname = \"GC\"\n";
        private const string LegacyCupheadToml = "[series]\nid = \"cuphead\"\nname = \"Cuphead\"\nseries-playlist = \"bgm_cuphead\"\n\n[[games]]\nid = \"gc\"\nname = \"GC\"\n";

        private static readonly short[] Order0 = Enumerable.Repeat((short)0, 16).ToArray();
        private static readonly ushort[] Incidence100 = Enumerable.Repeat((ushort)100, 16).ToArray();

        private readonly DesktopWorkspace _ws = new();
        private readonly PlaylistAssignmentService _service;

        public PlaylistAssignmentServiceTests()
        {
            _service = new PlaylistAssignmentService(_ws.MusicOptions(), TestCatalog.Service(TestCatalog.Mario()));
        }

        public void Dispose() => _ws.Dispose();

        /// <summary>A mod editing the vanilla mario series and adding cuphead; returns the mod path.</summary>
        private string SeedMod(string cupheadToml = CupheadToml)
        {
            var mario = _ws.WriteSeries("mymod", "mario", MarioCsv, MarioToml);
            var cuphead = _ws.WriteSeries("mymod", "cuphead", CupheadCsv, cupheadToml);
            foreach (var audio in new[] { Path.Combine(mario, "a.flac"), Path.Combine(mario, "b.flac"), Path.Combine(cuphead, "c.flac") })
                File.WriteAllText(audio, "");
            return Path.Combine(_ws.ModsRoot, "mymod");
        }

        private static ManagedPlaylist Playlist(ManagePlaylistsData data, string id) => data.Playlists.Single(p => p.Id == id);
        private static string[] Songs(ManagedPlaylist playlist) => playlist.Tracks.Select(t => t.BgmId).ToArray();

        private static ManagedPlaylist Saved(string id, string kind, params string[] songs) =>
            new(id, id, kind, new(), songs.Select(s => new PlaylistTrack(s, Order0, Incidence100)).ToList(), null);

        [Fact]
        public void Load_ShowsGamePlaylistsWithTheModSongsTheBuildAdds()
        {
            var data = _service.Load(SeedMod());

            var mario = Playlist(data, "bgmmario");
            Assert.Equal(("Mario", "vanilla", null), (mario.Name, mario.Kind, mario.DefaultTracks));
            Assert.Equal(new[] { "ui_bgm_a01_smb_chijyou", "ui_bgm_a05_smb3_hikousen", "ui_bgm_a", "ui_bgm_b" }, Songs(mario));
            Assert.Equal(new[] { "ui_bgm_a01_smb_chijyou", "ui_bgm_c", "ui_bgm_a", "ui_bgm_b" }, Songs(Playlist(data, "bgmsmashbtl")));
        }

        [Fact]
        public void Load_HidesTheGameTitlePlaylists()
        {
            Assert.DoesNotContain(_service.Load(SeedMod()).Playlists, p => p.Id.StartsWith("bgm_gametitle_"));
        }

        [Fact]
        public void Load_ShowsALegacySeriesPlaylistFieldAsASeriesFlag()
        {
            var data = _service.Load(SeedMod(LegacyCupheadToml));

            Assert.True(data.LegacyBlocks);
            var cuphead = Playlist(data, "bgm_cuphead");
            Assert.Equal(("Cuphead", "series"), (cuphead.Name, cuphead.Kind));
            Assert.Equal(new[] { new SeriesRef("cuphead", "Cuphead") }, cuphead.Series);
            Assert.Equal(new[] { "ui_bgm_c" }, Songs(cuphead));
        }

        [Fact]
        public void Load_ReportsLegacyBlocks_ShowingThemAsTheyBuild()
        {
            var data = _service.Load(SeedMod());

            Assert.True(data.LegacyBlocks);
            var menu = Playlist(data, "bgmsmashmenu");
            Assert.Equal(("custom", 0), (menu.Kind, menu.Series.Count));
            Assert.Equal(new PlaylistTrack("ui_bgm_a", Order0, Enumerable.Repeat((ushort)10, 16).ToArray()), menu.Tracks.Single(), new TrackComparer());
        }

        [Fact]
        public void Load_ShowsAWildcardLegacyBlockAsASeriesFlag()
        {
            var modPath = SeedMod();
            File.AppendAllText(Path.Combine(modPath, "cuphead", "series.toml"), "\n[[playlists]]\nid = \"bgm_cuphead_mix\"\nincidence = 100\n");

            var mix = Playlist(_service.Load(modPath), "bgm_cuphead_mix");

            Assert.Equal("series", mix.Kind);
            Assert.Equal(new[] { new SeriesRef("cuphead", "Cuphead") }, mix.Series);
        }

        [Fact]
        public void Load_ListsVanillaAndModSongs()
        {
            var songs = _service.Load(SeedMod()).Songs;

            Assert.Contains(new PlaylistSong("ui_bgm_a01_smb_chijyou", "Ground Theme", "Super Mario Bros.", false), songs);
            Assert.Contains(new PlaylistSong("ui_bgm_c", "Song C", "Cuphead", true), songs);
        }

        [Fact]
        public void Load_ListsStagesWithTheirGamePlaylist()
        {
            var castle = _service.Load(SeedMod()).Stages.Single(s => s.Id == "ui_stage_mario_castle64");

            Assert.Equal(new ManagedStage("ui_stage_mario_castle64", "Peach's Castle", false, "Super Mario Bros.", "bgmmario", 1, "bgmmario", 1), castle);
        }

        [Fact]
        public void Load_FlagsTheModSongsTheBuildAlwaysAdds()
        {
            var battle = Playlist(_service.Load(SeedMod()), "bgmsmashbtl");

            Assert.Equal(new[] { "ui_bgm_c", "ui_bgm_a", "ui_bgm_b" }, battle.Tracks.Where(t => t.AutoAdded).Select(t => t.BgmId));
        }

        [Fact]
        public void Save_RoundTripsPlaylistsAndOrderSlots()
        {
            var modPath = SeedMod();
            var orders = Enumerable.Range(0, 16).Select(i => (short)(i == 3 ? -1 : 0)).ToArray();
            var playlist = new ManagedPlaylist("bgmmine", "Mine", "custom", new(), new()
            {
                new("ui_bgm_c", orders, Incidence100),
                new("ui_bgm_a01_smb_chijyou", Order0, Incidence100)
            }, null);

            var mine = Playlist(_service.Save(modPath, new() { playlist }, new()), "bgmmine");

            Assert.Equal(("Mine", "custom"), (mine.Name, mine.Kind));
            Assert.Equal(playlist.Tracks, mine.Tracks, new TrackComparer());
            var toml = File.ReadAllText(Path.Combine(modPath, "playlists.toml"));
            Assert.Contains("{ song = \"ui_bgm_a01_smb_chijyou\", order = 0, incidence = 100 },", toml);
            Assert.Contains("order = [0, 0, 0, -1, 0,", toml);
        }

        [Fact]
        public void Save_ReplacesAGamePlaylist_GivingItsDefaultForAReset()
        {
            var modPath = SeedMod();

            var mario = Playlist(_service.Save(modPath, new() { Saved("bgmmario", "vanilla", "ui_bgm_b") }, new()), "bgmmario");

            Assert.Equal(new[] { "ui_bgm_b", "ui_bgm_a" }, Songs(mario));
            Assert.Equal(new[] { "ui_bgm_a01_smb_chijyou", "ui_bgm_a05_smb3_hikousen", "ui_bgm_a", "ui_bgm_b" }, mario.DefaultTracks.Select(t => t.BgmId));
            Assert.DoesNotContain("name =", File.ReadAllText(Path.Combine(modPath, "playlists.toml")));
        }

        [Fact]
        public void Save_ASavedPlaylistStillGetsModSongs_RemovedOrNew()
        {
            var modPath = SeedMod();
            _service.Save(modPath, new() { Saved("bgmsmashbtl", "vanilla", "ui_bgm_a01_smb_chijyou") }, new());

            Assert.Equal(new[] { "ui_bgm_a01_smb_chijyou", "ui_bgm_c", "ui_bgm_a", "ui_bgm_b" }, Songs(Playlist(_service.Load(modPath), "bgmsmashbtl")));
        }

        [Fact]
        public void Save_ASavedSeriesPlaylistStillHoldsEverySongOfTheSeries()
        {
            var modPath = SeedMod();
            var mix = Saved("bgm_cuphead", "series", "ui_bgm_a01_smb_chijyou") with { Series = new() { new("cuphead", "Cuphead") } };
            _service.Save(modPath, new() { mix }, new());

            var cuphead = Playlist(_service.Load(modPath), "bgm_cuphead");
            Assert.Equal(new[] { "ui_bgm_a01_smb_chijyou", "ui_bgm_c" }, Songs(cuphead));
            Assert.Equal(new[] { false, true }, cuphead.Tracks.Select(t => t.AutoAdded));
        }

        [Fact]
        public void Save_MigratesALegacySeriesPlaylistField_ToAFlagOnlyPlaylist()
        {
            var modPath = SeedMod(LegacyCupheadToml);
            var cuphead = Playlist(_service.Load(modPath), "bgm_cuphead");

            var result = _service.Save(modPath, new() { cuphead with { Tracks = null } }, new());

            Assert.DoesNotContain("series-playlist", File.ReadAllText(Path.Combine(modPath, "cuphead", "series.toml")));
            Assert.Equal("[[playlists]]\nid = \"bgm_cuphead\"\nname = \"Cuphead\"\nseries = [\"cuphead\"]\n", File.ReadAllText(Path.Combine(modPath, "playlists.toml")));
            Assert.Equal(new[] { "ui_bgm_c" }, Songs(Playlist(result, "bgm_cuphead")));
        }

        [Fact]
        public void Save_MigratesLegacyBlocks_KeepingTheOtherTables()
        {
            var modPath = SeedMod();
            var data = _service.Load(modPath);

            var result = _service.Save(modPath, data.Playlists.Where(p => p.Kind == "custom").ToList(), new());

            Assert.False(result.LegacyBlocks);
            var toml = File.ReadAllText(Path.Combine(modPath, "mario", "series.toml"));
            Assert.DoesNotContain("[[playlists]]", toml);
            foreach (var expected in new[] { "id = \"mario\"", "[[games]]", "[default-track-data]" })
                Assert.Contains(expected, toml);
            Assert.Equal(new[] { "ui_bgm_a" }, Songs(Playlist(result, "bgmsmashmenu")));
        }

        [Fact]
        public void Save_AssignsStagePlaylists()
        {
            var result = _service.Save(SeedMod(), new() { Saved("bgmmine", "custom", "ui_bgm_c") }, new() { new("ui_stage_battle_field", "bgmmine", 4) });

            var stage = result.Stages.Single(s => s.Id == "ui_stage_battle_field");
            Assert.Equal(("bgmmine", 4, "bgmsmashbtl", 0), (stage.PlaylistId, stage.Order, stage.VanillaPlaylistId, stage.VanillaOrder));
        }

        [Fact]
        public void Save_IgnoresStagesGivenAGameTitlePlaylist()
        {
            var result = _service.Save(SeedMod(), new(), new() { new("ui_stage_battle_field", "bgm_gametitle_cuphead", 4) });

            Assert.Equal("bgmsmashbtl", result.Stages.Single(s => s.Id == "ui_stage_battle_field").PlaylistId);
        }

        [Fact]
        public void Save_WithNothingToSave_RemovesPlaylistsToml()
        {
            var modPath = SeedMod();
            _service.Save(modPath, new(), new() { new("ui_stage_battle_field", "bgmmario", 0) });

            _service.Save(modPath, new(), new());

            Assert.False(File.Exists(Path.Combine(modPath, "playlists.toml")));
        }

        [Fact]
        public void Load_WithoutGameResources_Throws()
        {
            var service = new PlaylistAssignmentService(_ws.MusicOptions(), TestCatalog.Service());

            Assert.Equal("Game resources not found in Resources/Game.", Assert.Throws<DesktopApiException>(() => service.Load(SeedMod())).Message);
        }

        [Fact]
        public void RejectsModPathsOutsideTheModsFolder()
        {
            var outside = Path.Combine(_ws.Root, "outside");

            Assert.Equal("Invalid mod path.", Assert.Throws<DesktopApiException>(() => _service.Load(outside)).Message);
            Assert.Equal("Invalid mod path.", Assert.Throws<DesktopApiException>(() => _service.Save(outside, new(), new())).Message);
        }

        private class TrackComparer : IEqualityComparer<PlaylistTrack>
        {
            public bool Equals(PlaylistTrack x, PlaylistTrack y) =>
                x.BgmId == y.BgmId && x.Orders.SequenceEqual(y.Orders) && x.Incidences.SequenceEqual(y.Incidences);

            public int GetHashCode(PlaylistTrack track) => track.BgmId.GetHashCode();
        }
    }
}
