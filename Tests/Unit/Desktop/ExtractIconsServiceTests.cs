using Tests.Helpers;
using UMB.CLI.Desktop;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Unit.Desktop
{
    public class ExtractIconsServiceTests : IDisposable
    {
        private readonly DesktopWorkspace _ws = new();
        private readonly ExtractIconsService _service;
        private readonly string _compiled;

        public ExtractIconsServiceTests()
        {
            _service = new ExtractIconsService(_ws.MusicOptions(), TestEnvironment.CreateLogger<ExtractIconsService>());
            _compiled = Path.Combine(_ws.Root, "compiled");
        }

        public void Dispose() => _ws.Dispose();

        private string Series0Dir => Path.Combine(_compiled, "ui", "replace", "series", "series_0");

        [Fact]
        public void Analyze_RejectsModPathsOutsideTheModsFolder()
        {
            Assert.Equal("Invalid mod path.", Assert.Throws<DesktopApiException>(() =>
                _service.Analyze(_compiled, Path.Combine(_ws.Root, "outside"))).Message);
        }

        [Fact]
        public void Analyze_RequiresTheCompiledSeries0Folder()
        {
            Assert.Contains("series_0", Assert.Throws<DesktopApiException>(() =>
                _service.Analyze(_compiled, _ws.ModDir("mymod"))).Message);
        }

        [Fact]
        public void Analyze_MatchesBntxFilesToSeries_AndListsUnmatched()
        {
            var modPath = _ws.ModDir("mymod");
            DesktopWorkspace.WriteFile(_ws.ModDir("mymod", "mario"), "icon.png", "IMG");
            _ws.ModDir("mymod", "persona");
            foreach (var file in new[] { "series_0_mario.bntx", "series_0_persona.bntx", "series_0_ghost.bntx", "series_0_skip.png", "unrelated.txt" })
                DesktopWorkspace.WriteFile(Series0Dir, file, "B");

            var analysis = _service.Analyze(_compiled, modPath);

            Assert.Equal("mymod", analysis.ModName);
            Assert.Equal(new[] { "ghost" }, analysis.Unmatched);
            var byId = analysis.Matched.ToDictionary(m => m.SeriesId);
            Assert.Equal(new[] { "mario", "persona" }, byId.Keys.OrderBy(k => k));
            Assert.True(byId["mario"].HasExistingIcon);
            Assert.False(byId["persona"].HasExistingIcon);
        }

        [Fact]
        public void Extract_ExtractsNothing_WhenTheToolIsMissing()
        {
            Assert.Equal(new ExtractIconsResult(0, 0, 0), _service.Extract(_compiled, _ws.ModDir("mymod"), missingOnly: false));
        }
    }
}
