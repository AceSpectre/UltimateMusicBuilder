using Microsoft.Extensions.Options;
using Moq;
using Sma5h;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Models;
using UMB.CLI.Services;

namespace Tests.Helpers
{
    /// <summary>A <see cref="VanillaCatalogService"/> serving a fixed catalog, without game files.</summary>
    public static class TestCatalog
    {
        public static VanillaCatalogService Service(VanillaCatalogService.Catalog catalog = null)
        {
            var service = new Mock<VanillaCatalogService>((IOptionsMonitor<Sma5hOptions>)null, (IServiceProvider)null);
            service.Setup(s => s.Get()).Returns(catalog);
            return service.Object;
        }

        /// <summary>The "mario" series: games smb and smb3 (plus one no song uses), two sound test songs and a hidden one.</summary>
        public static VanillaCatalogService.Catalog Mario() => new()
        {
            BgmTitles = new() { ["ui_bgm_a01_smb_chijyou"] = "Ground Theme", ["ui_bgm_a05_smb3_hikousen"] = "Airship", ["ui_bgm_hidden"] = "Hidden" },
            GameTitles = new()
            {
                new("smb", "Super Mario Bros.", "ui_series_mario"),
                new("smb3", "Super Mario Bros. 3", "ui_series_mario"),
                new("mario_for_doc", "mario_for_doc", "ui_series_mario"),
                new("loz", "The Legend of Zelda", "ui_series_zelda")
            },
            Series = new() { new("mario", "Super Mario Bros.", "bgmmario") },
            Songs = new()
            {
                new("ui_bgm_a01_smb_chijyou", "info_a01_smb_chijyou", "Ground Theme", "ui_series_mario", "a01_smb_chijyou", "smb",
                    "Koji Kondo", "Nintendo", "original", "", "", 0),
                new("ui_bgm_a05_smb3_hikousen", "info_a05_smb3_hikousen", "Airship", "ui_series_mario", "a05_smb3_hikousen", "smb3",
                    "", "", "arrange", "sf_situationlink", "info_a01_smb_chijyou", 1),
                new("ui_bgm_hidden", "info_hidden", "Hidden", "ui_series_mario", "hidden", "smb", "", "", "original", "", "", -1)
            },
            Playlists = new()
            {
                Playlist("bgmmario", "ui_bgm_a01_smb_chijyou", "ui_bgm_a05_smb3_hikousen"),
                Playlist("bgmsmashbtl", "ui_bgm_a01_smb_chijyou")
            },
            Stages = new()
            {
                new() { UiStageId = "ui_stage_mario_castle64", UiSeriesId = "ui_series_mario", BgmSetId = "bgmmario", BgmSettingNo = 1 },
                new() { UiStageId = "ui_stage_battle_field", UiSeriesId = "", BgmSetId = "bgmsmashbtl", BgmSettingNo = 0 }
            }
        };

        /// <summary>A playlist playing its songs in the given order on every order slot.</summary>
        public static PlaylistEntry Playlist(string id, params string[] bgmIds)
        {
            var playlist = new PlaylistEntry(id);
            playlist.Tracks.AddRange(bgmIds.Select((bgmId, i) => PlaylistHelper.Track(bgmId, (short)i, 100)));
            return playlist;
        }
    }
}
