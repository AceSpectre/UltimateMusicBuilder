using Tests.Helpers;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Unit.Desktop
{
    public class VolumeCheckServiceTests : IDisposable
    {
        private const string Csv =
            "filename,title,game,volume\n" +
            "normal.nus3audio,Normal,game,1\n" +
            "boosted.nus3audio,Boosted,game,1.9\n" +
            "legacy.nus3audio,Legacy,game,2.7\n" +
            "blank.nus3audio,Blank,game,\n";

        private readonly DesktopWorkspace _ws = new();
        private readonly VolumeCheckService _service;

        public VolumeCheckServiceTests()
        {
            _service = new VolumeCheckService(_ws.MusicOptions());
        }

        public void Dispose() => _ws.Dispose();

        [Fact]
        public void Check_FlagsVolumesAtOrAboveTheThreshold()
        {
            _ws.WriteSeries("persona", "persona", Csv);

            var track = Assert.Single(_service.Check("persona"));
            Assert.Equal(("persona", "persona", "legacy.nus3audio", "Legacy", 2.7f),
                (track.ModName, track.SeriesName, track.Filename, track.Title, track.Volume));
        }

        [Fact]
        public void Check_WithoutAModNameChecksEveryMod()
        {
            _ws.WriteSeries("persona", "persona", Csv);
            _ws.WriteSeries("mario", "mario", Csv);

            Assert.Equal(new[] { "mario", "persona" }, _service.Check(null).Select(t => t.ModName).OrderBy(n => n));
        }

        [Fact]
        public void Check_IgnoresCsvWithoutAVolumeColumn()
        {
            _ws.WriteSeries("persona", "persona", "filename,title,game\nsong.flac,Song,Game\n");
            Assert.Empty(_service.Check("persona"));
        }
    }
}
