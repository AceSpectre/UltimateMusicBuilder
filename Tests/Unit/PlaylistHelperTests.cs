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
        public void RequiredSongs_AreTheSeriesPlaylistSongs_ThenSmashBattleAndTheSeriesStagePlaylists()
        {
            var stages = new[] { new StageEntry { UiStageId = "s", UiSeriesId = "ui_series_mario", BgmSetId = "bgmmario" } };
            var seriesPlaylist = TestCatalog.Playlist("bgm_gametitle_dev", "d");
            seriesPlaylist.Tracks[0].Incidence0 = 40;

            var required = PlaylistHelper.RequiredSongs(stages, new[] { seriesPlaylist }, new[] { ("x", "ui_series_mario"), ("y", (string)null) }, 100);

            Assert.Equal(new[]
            {
                new RequiredSong("bgm_gametitle_dev", "d", 40),
                new RequiredSong("bgmsmashbtl", "x", 100),
                new RequiredSong("bgmmario", "x", 100),
                new RequiredSong("bgmsmashbtl", "y", 100)
            }, required);
        }

        [Theory]
        [InlineData(true, 40)]
        [InlineData(false, 40)]
        public void RequiredSongs_FollowTheSmashBattleSettings(bool addToSmashBattle, ushort incidence)
        {
            var stages = new[] { new StageEntry { UiStageId = "s", UiSeriesId = "ui_series_mario", BgmSetId = "bgmmario" } };
            var options = new Sma5h.Mods.Music.Sma5hMusicOptions.Sma5hMusicOptionsSection { AddModSongsToSmashBattle = addToSmashBattle, SmashBattleIncidence = incidence };

            var required = PlaylistHelper.RequiredSongs(stages, Array.Empty<PlaylistEntry>(), new[] { ("x", "ui_series_mario") }, PlaylistHelper.SmashBattleIncidence(options));

            var expected = new List<RequiredSong> { new("bgmmario", "x", 100) };
            if (addToSmashBattle) expected.Insert(0, new("bgmsmashbtl", "x", incidence));
            Assert.Equal(expected, required);
        }

        [Fact]
        public void ApplyModPlaylists_AddsMissingRequiredSongsAfterTheLastSong()
        {
            var playlists = Playlists(TestCatalog.Playlist("bgmsmashbtl", "a"), TestCatalog.Playlist("bgmmario", "b", "c"));

            var added = PlaylistHelper.ApplyModPlaylists(playlists, Array.Empty<StageEntry>(),
                new[] { new RequiredSong("bgmmario", "x", 50), new RequiredSong("bgmsmashbtl", "a", 100), new RequiredSong("bgmgone", "x", 100) },
                Array.Empty<PlaylistEntry>(), Array.Empty<StagePlaylistAssignment>());

            Assert.Equal(1, added);
            Assert.Equal(new[] { "b", "c", "x" }, Songs(playlists["bgmmario"]));
            Assert.Equal((2, 50), (playlists["bgmmario"].Tracks[2].Order5, playlists["bgmmario"].Tracks[2].Incidence9));
            Assert.Equal(new[] { "a" }, Songs(playlists["bgmsmashbtl"]));
        }

        [Fact]
        public void ApplyModPlaylists_SavedPlaylistsReplaceTheirs_KeepingRequiredSongsAndSavedOrders()
        {
            var playlists = Playlists(TestCatalog.Playlist("bgmsmashbtl", "a", "x"));
            var saved = TestCatalog.Playlist("bgmsmashbtl", "x");
            saved.Tracks[0].Order2 = -1;

            PlaylistHelper.ApplyModPlaylists(playlists, Array.Empty<StageEntry>(),
                new[] { new RequiredSong("bgmsmashbtl", "x", 100), new RequiredSong("bgmsmashbtl", "y", 100) },
                new[] { saved, new PlaylistEntry("bgmmine", "Mine") }, Array.Empty<StagePlaylistAssignment>());

            Assert.Equal(new[] { "x", "y" }, Songs(playlists["bgmsmashbtl"]));
            Assert.Equal(-1, playlists["bgmsmashbtl"].Tracks[0].Order2);
            Assert.Equal("Mine", playlists["bgmmine"].Title);
        }

        [Fact]
        public void ApplyModPlaylists_ReassignsStagesAfterTheRequiredSongs()
        {
            var playlists = Playlists(TestCatalog.Playlist("bgmmario"), new PlaylistEntry("bgmmine"));
            var stage = new StageEntry { UiStageId = "ui_stage_x", UiSeriesId = "ui_series_mario", BgmSetId = "bgmmario", BgmSettingNo = 0 };
            var required = PlaylistHelper.RequiredSongs(new[] { stage }, Array.Empty<PlaylistEntry>(), new[] { ("x", "ui_series_mario") }, 100);

            PlaylistHelper.ApplyModPlaylists(playlists, new[] { stage }, required, Array.Empty<PlaylistEntry>(),
                new[] { new StagePlaylistAssignment("ui_stage_x", "bgmmine", 7) });

            Assert.Equal(("bgmmine", (byte)7), (stage.BgmSetId, stage.BgmSettingNo));
            Assert.Equal(new[] { "x" }, Songs(playlists["bgmmario"]));
            Assert.Empty(playlists["bgmmine"].Tracks);
        }
    
        [Fact]
        public void ApplyModPlaylists_IgnoresGameTitlePlaylistOverridesAndStages()
        {
            var playlists = Playlists(TestCatalog.Playlist("bgm_gametitle_dev", "d"));
            var stage = new StageEntry { UiStageId = "ui_stage_x", BgmSetId = "bgmmario", BgmSettingNo = 0 };

            PlaylistHelper.ApplyModPlaylists(playlists, new[] { stage }, Array.Empty<RequiredSong>(),
                new[] { TestCatalog.Playlist("bgm_gametitle_dev", "x") }, new[] { new StagePlaylistAssignment("ui_stage_x", "bgm_gametitle_dev", 1) });

            Assert.Equal(new[] { "d" }, Songs(playlists["bgm_gametitle_dev"]));
            Assert.Equal("bgmmario", stage.BgmSetId);
        }
    }
}
