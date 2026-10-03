using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sma5h.Interfaces;
using Sma5h.ResourceProviders;
using Tests.Helpers;
using UMB.CLI.Desktop;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Unit.Desktop
{
    /// <summary>
    /// Reads the real Resources/Game dump when it is present; those tests pass without checking
    /// anything when it isn't (e.g. in CI).
    /// </summary>
    public class VanillaCatalogServiceTests : IDisposable
    {
        private readonly DesktopWorkspace _ws = new();

        public void Dispose() => _ws.Dispose();

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Sma5h.sln")))
                dir = dir.Parent;
            return dir?.FullName;
        }

        /// <summary>A catalog over the repo's game files, or null when they aren't there.</summary>
        private VanillaCatalogService RepoCatalog()
        {
            var root = RepoRoot();
            if (root == null || !File.Exists(Path.Combine(root, "Resources", "Game", "ui", "param", "database", "ui_bgm_db.prc")))
                return null;

            _ws.Options.GameResourcesPath = Path.Combine(root, "Resources", "Game");
            _ws.Options.ResourcesPath = Path.Combine(root, "Resources");
            var services = new ServiceCollection()
                .AddSingleton<IResourceProvider>(new PrcResourceProvider(_ws.CoreOptions(), TestEnvironment.CreateLogger<PrcResourceProvider>()))
                .AddSingleton<IResourceProvider>(new MsbtResourceProvider(_ws.CoreOptions(), TestEnvironment.CreateLogger<MsbtResourceProvider>()))
                .BuildServiceProvider();
            return new VanillaCatalogService(_ws.CoreOptions(), services);
        }

        [Fact]
        public void Get_IsNullWithoutGameFiles()
        {
            Assert.Null(new VanillaCatalogService(_ws.CoreOptions(), new Mock<IServiceProvider>().Object).Get());
        }

        [Fact]
        public void GetPlaylistInfo_ReportsMissingGameFiles()
        {
            var service = new VanillaCatalogService(_ws.CoreOptions(), new Mock<IServiceProvider>().Object);
            Assert.Contains("Game resources not found", Assert.Throws<DesktopApiException>(service.GetPlaylistInfo).Message);
        }

        [Fact]
        public void Catalog_ResolvesSongTitles()
        {
            if (RepoCatalog()?.Get() is not { } catalog) return;

            Assert.True(catalog.BgmTitles.Count > 100);
            Assert.All(catalog.BgmTitles.Values, title => Assert.NotEmpty(title));
        }

        [Fact]
        public void Catalog_ListsGameTitlesWithBareIdsAndSeries()
        {
            if (RepoCatalog()?.Get() is not { } catalog) return;

            Assert.True(catalog.GameTitles.Count > 50);
            Assert.All(catalog.GameTitles, g =>
            {
                Assert.DoesNotMatch("^ui_gametitle_", g.Id);
                Assert.True(g.SeriesId == "" || g.SeriesId.StartsWith("ui_series_"));
            });
            Assert.Contains(catalog.GameTitles, g => g.SeriesId == "ui_series_mariokart");
        }

        [Fact]
        public void Catalog_ListsSongsWithInfoIdsBySeries()
        {
            if (RepoCatalog()?.Get() is not { } catalog) return;

            Assert.True(catalog.Songs.Count > 100);
            Assert.All(catalog.Songs, s =>
            {
                Assert.StartsWith("info_", s.InfoId);
                Assert.StartsWith("ui_bgm_", s.BgmId);
            });
            Assert.Contains(catalog.Songs, s => s.SeriesId == "ui_series_mariokart");
        }

        [Fact]
        public void PlaylistInfo_ListsPlaylistsAndStages()
        {
            if (RepoCatalog()?.GetPlaylistInfo() is not { } info) return;

            Assert.Contains(info.Playlists, p => p.Id == "bgmmario" && p.Name == "Mario" && p.SongCount > 0);
            var battlefield = Assert.Single(info.Stages, s => s.UiStageId == "ui_stage_battle_field");
            Assert.Equal("Battlefield", battlefield.Name);
            Assert.NotEmpty(battlefield.Songs);
            Assert.Equal(battlefield.Songs.OrderBy(s => s.Order).Select(s => s.BgmId), battlefield.Songs.Select(s => s.BgmId));
            // Hidden "(H)" stages sort last.
            Assert.Equal(info.Stages.OrderBy(s => s.Hidden).Select(s => s.UiStageId), info.Stages.Select(s => s.UiStageId));
        }
    }
}
