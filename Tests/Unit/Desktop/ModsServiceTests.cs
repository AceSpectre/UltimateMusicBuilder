using Tests.Helpers;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Unit.Desktop
{
    public class ModsServiceTests : IDisposable
    {
        private const string Csv = "filename,title,game\nsong.flac,Song,Game\n";

        private readonly DesktopWorkspace _ws = new();
        private readonly ModsService _service;

        public ModsServiceTests()
        {
            _service = new ModsService(_ws.MusicOptions());
        }

        public void Dispose() => _ws.Dispose();

        [Fact]
        public void ListMods_ReturnsTheModFolders()
        {
            _ws.ModDir("persona");
            _ws.ModDir("mario");
            Assert.Equal(new[] { "mario", "persona" }, _service.ListMods().Select(m => m.Name).OrderBy(n => n));
        }

        [Fact]
        public void ListMods_IsEmptyWithoutTheModsFolder()
        {
            Directory.Delete(_ws.ModsRoot);
            Assert.Empty(_service.ListMods());
        }

        [Fact]
        public void ListModSeries_ReturnsOnlyFoldersWithTracksCsv()
        {
            _ws.WriteSeries("persona", "persona", Csv);
            _ws.ModDir("persona", "empty-dir");

            Assert.Equal(new[] { "persona" }, _service.ListModSeries(Path.Combine(_ws.ModsRoot, "persona")).Select(s => s.Name));
        }

        [Fact]
        public void ListModSeries_SkipsDotfilesAndSongsToValidate()
        {
            _ws.WriteSeries("persona", "persona", Csv);
            _ws.WriteSeries("persona", "songs-to-validate", Csv);
            _ws.WriteSeries("persona", ".hidden", Csv);

            Assert.Equal(new[] { "persona" }, _service.ListModSeries(Path.Combine(_ws.ModsRoot, "persona")).Select(s => s.Name));
        }

        [Fact]
        public void ListModSeries_IsEmptyOutsideTheModsFolder()
        {
            Assert.Empty(_service.ListModSeries(Path.Combine(_ws.Root, "elsewhere")));
        }

        [Fact]
        public void ListModSeries_AcceptsASymlinkToTheModsFolder()
        {
            if (OperatingSystem.IsWindows()) return;

            _ws.WriteSeries("persona", "persona", Csv);
            var alias = Path.Combine(_ws.Root, "mods-alias");
            Directory.CreateSymbolicLink(alias, _ws.ModsRoot);

            Assert.Equal(new[] { "persona" }, _service.ListModSeries(Path.Combine(alias, "persona")).Select(s => s.Name));
        }

        [Fact]
        public void ListModSeries_RejectsASymlinkOutsideTheModsFolder()
        {
            if (OperatingSystem.IsWindows()) return;

            var outside = Path.Combine(_ws.Root, "elsewhere");
            DesktopWorkspace.WriteFile(Path.Combine(outside, "persona"), "tracks.csv", Csv);
            var alias = Path.Combine(_ws.ModsRoot, "escaped");
            Directory.CreateSymbolicLink(alias, outside);

            Assert.Empty(_service.ListModSeries(alias));
        }

        [Fact]
        public void GetModStats_CountsSeriesAndTracks()
        {
            _ws.WriteSeries("persona", "a", Csv);
            _ws.WriteSeries("persona", "b", "filename\nx.flac\ny.flac\n");

            Assert.Equal(new UMB.CLI.Desktop.ModStats(2, 3), _service.GetModStats(Path.Combine(_ws.ModsRoot, "persona")));
        }
    }
}
