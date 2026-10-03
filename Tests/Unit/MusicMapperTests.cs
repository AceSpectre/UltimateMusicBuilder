using Sma5h.Data.Ui.Param.Database.PrcUiBgmDatabaseModels;
using Sma5h.Mods.Music.Models;
using Sma5h.Mods.Music.Models.PlaylistEntryModels;
using Sma5h.Mods.Music.MusicMods.MusicModModels;
using System.Collections.Generic;
using Xunit;

namespace Tests.Unit
{
    public class MusicMapperTests
    {
        [Fact]
        public void PlaylistTrack_KeepsIncidence5And15Separate()
        {
            var prc = new PrcBgmPlaylistEntry { Incidence5 = 5, Incidence15 = 15 };

            var entry = MusicMapper.ToEntry(prc);
            var roundTrip = MusicMapper.ToPrc(entry);

            Assert.Equal(5, entry.Incidence5);
            Assert.Equal(15, entry.Incidence15);
            Assert.Equal(5, roundTrip.Incidence5);
            Assert.Equal(15, roundTrip.Incidence15);
        }

        [Fact]
        public void BgmDbRootToConfig_KeepsMovieEditSeparateFromOriginal()
        {
            var entry = new BgmDbRootEntry("ui_bgm_a") { IsSelectableMovieEdit = true, IsSelectableOriginal = false };

            var config = MusicMapper.ToConfig(entry);

            Assert.True(config.IsSelectableMovieEdit);
            Assert.False(config.IsSelectableOriginal);
        }

        [Fact]
        public void BgmDbRootConfigToEntry_CopiesLabelsAndKeepsIgnoredFields()
        {
            var config = new BgmDbRootConfig { NameId = "ignored", Title = new Dictionary<string, string> { ["us_en"] = "Song" }, Author = null };
            var entry = new BgmDbRootEntry("ui_bgm_a") { NameId = "kept" };

            MusicMapper.Map(config, entry);

            Assert.Equal("kept", entry.NameId);
            Assert.Equal("Song", entry.Title["us_en"]);
            Assert.NotSame(config.Title, entry.Title);
            Assert.Empty(entry.Author);
        }

        [Fact]
        public void GameConfigToEntry_MapsTitleToMsbtTitle()
        {
            var config = new GameConfig { NameId = "game", Title = new Dictionary<string, string> { ["us_en"] = "Game" } };
            var entry = new GameTitleEntry("ui_gametitle_a");

            MusicMapper.Map(config, entry);

            Assert.Equal("Game", entry.MSBTTitle["us_en"]);
            Assert.Equal("ui_gametitle_a", entry.UiGameTitleId);
        }
    }
}
