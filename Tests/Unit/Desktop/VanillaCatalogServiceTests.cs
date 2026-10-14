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

        /// <summary>A catalog over the repo's game files, or null when they aren't there.</summary>
        private VanillaCatalogService RepoCatalog()
        {
            var root = SuiteEnvironmentCheck.RepoRoot;
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
        public void Catalog_ListsSoundTestSeriesWithNamesAndPlaylists()
        {
            if (RepoCatalog()?.Get() is not { } catalog) return;

            var mario = catalog.Series.Single(s => s.Id == "mario");
            Assert.Equal(("Mario", "bgmmario"), (mario.Name, mario.Playlist));
            Assert.DoesNotContain(catalog.Series, s => s.Id.StartsWith("ui_series_"));
        }

        [Fact]
        public void Catalog_ListsSongDetailsInTracksCsvForm()
        {
            if (RepoCatalog()?.Get() is not { } catalog) return;

            var song = catalog.Songs.Single(s => s.BgmId == "ui_bgm_a01_smb_chijyou");
            Assert.Equal(("a01_smb_chijyou", "mariobros", "arrange"), (song.ToneId, song.GameId, song.RecordType));
            Assert.NotEmpty(song.Author);
            Assert.True(song.TestDispOrder >= 0);
            Assert.All(catalog.Songs.SkipWhile(s => s.TestDispOrder >= 0), s => Assert.True(s.TestDispOrder < 0));
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
        public void Catalog_LoadsPlaylistsAndStages()
        {
            if (RepoCatalog()?.Get() is not { } catalog) return;

            Assert.Contains(catalog.Playlists, p => p.Id == "bgmmario" && p.Tracks.Count > 0);
            Assert.Equal("bgmsmashbtl", Assert.Single(catalog.Stages, s => s.UiStageId == "ui_stage_battle_field").BgmSetId);
        }
    }
}
