using Microsoft.Extensions.Options;
using Moq;
using Sma5h;
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

        /// <summary>The "mario" series: games smb and smb3, two sound test songs and a hidden one.</summary>
        public static VanillaCatalogService.Catalog Mario() => new()
        {
            BgmTitles = new() { ["ui_bgm_a01_smb_chijyou"] = "Ground Theme", ["ui_bgm_a05_smb3_hikousen"] = "Airship", ["ui_bgm_hidden"] = "Hidden" },
            GameTitles = new()
            {
                new("smb", "Super Mario Bros.", "ui_series_mario"),
                new("smb3", "Super Mario Bros. 3", "ui_series_mario"),
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
            }
        };
    }
}
