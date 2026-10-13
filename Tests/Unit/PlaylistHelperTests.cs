using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Models;
using Tests.Helpers;
using Xunit;

namespace Tests.Unit
{
    public class PlaylistHelperTests
    {
        private static Dictionary<string, PlaylistEntry> Playlists(params PlaylistEntry[] playlists) => playlists.ToDictionary(p => p.Id);
        private static string[] Songs(PlaylistEntry playlist) => playlist.Tracks.Select(t => t.UiBgmId).ToArray();

        [Fact]
        public void Track_SetsEveryOrderSlot()
        {
            var orders = Enumerable.Range(0, 16).Select(i => (short)i).ToArray();
            var incidences = Enumerable.Range(0, 16).Select(i => (ushort)(i * 10)).ToArray();

            var track = PlaylistHelper.Track("ui_bgm_x", orders, incidences);

            Assert.Equal(orders, PlaylistHelper.Orders(track));
            Assert.Equal(incidences, PlaylistHelper.Incidences(track));
        }

        [Fact]
        public void Merge_AddsNewPlaylists_AndOnlyNewTracksToExistingOnes()
        {
            var playlists = Playlists(TestCatalog.Playlist("bgmmario", "a", "b"));

            Assert.Equal(1, PlaylistHelper.Merge(playlists, TestCatalog.Playlist("bgmmario", "b", "c")));
            Assert.Equal(1, PlaylistHelper.Merge(playlists, TestCatalog.Playlist("bgmnew", "d")));

            Assert.Equal(new[] { "a", "b", "c" }, Songs(playlists["bgmmario"]));
            Assert.Equal(new[] { "d" }, Songs(playlists["bgmnew"]));
        }

        [Fact]
        public void AddModSongs_AddsToSmashBattleAndTheSeriesStagePlaylists_AfterTheLastSong()
        {
            var playlists = Playlists(TestCatalog.Playlist("bgmsmashbtl", "a"), TestCatalog.Playlist("bgmmario", "b", "c"), TestCatalog.Playlist("bgmzelda"));
            var stages = new[] { new StageEntry { UiStageId = "s", UiSeriesId = "ui_series_mario", BgmSetId = "bgmmario" } };

            var added = PlaylistHelper.AddModSongs(playlists, stages, new[] { ("x", "ui_series_mario"), ("a", (string)null) });

            Assert.Equal(2, added);
            Assert.Equal(new[] { "b", "c", "x" }, Songs(playlists["bgmmario"]));
            Assert.Equal(2, playlists["bgmmario"].Tracks[2].Order5);
            Assert.Equal(new[] { "a", "x" }, Songs(playlists["bgmsmashbtl"]));
            Assert.Empty(playlists["bgmzelda"].Tracks);
        }

        [Fact]
        public void ApplyPlaylists_ReplacesPlaylistsAndCreatesNewOnes()
        {
            var playlists = Playlists(TestCatalog.Playlist("bgmmario", "a", "b"));

            PlaylistHelper.ApplyPlaylists(playlists, new[] { TestCatalog.Playlist("bgmmario", "c"), new PlaylistEntry("bgmmine", "Mine") });

            Assert.Equal(new[] { "c" }, Songs(playlists["bgmmario"]));
            Assert.Equal("Mine", playlists["bgmmine"].Title);
        }

        [Fact]
        public void AddModSongs_AfterApplyPlaylists_AddsNewModSongsButKeepsSavedOrders()
        {
            var playlists = Playlists(TestCatalog.Playlist("bgmsmashbtl", "a"));
            var saved = TestCatalog.Playlist("bgmsmashbtl", "x");
            saved.Tracks[0].Order2 = -1;
            PlaylistHelper.ApplyPlaylists(playlists, new[] { saved });

            PlaylistHelper.AddModSongs(playlists, Array.Empty<StageEntry>(), new[] { ("x", (string)null), ("y", (string)null) });

            Assert.Equal(new[] { "x", "y" }, Songs(playlists["bgmsmashbtl"]));
            Assert.Equal(-1, playlists["bgmsmashbtl"].Tracks[0].Order2);
        }

        [Fact]
        public void ApplyStages_ReassignsStagePlaylists()
        {
            var stage = new StageEntry { UiStageId = "ui_stage_x", BgmSetId = "bgmmario", BgmSettingNo = 0 };

            PlaylistHelper.ApplyStages(new[] { stage }, new[] { new StagePlaylistAssignment("ui_stage_x", "bgmmine", 7) });

            Assert.Equal(("bgmmine", (byte)7), (stage.BgmSetId, stage.BgmSettingNo));
        }
    }
}
