using Tests.Helpers;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Unit.Desktop
{
    public class BuildValidationServiceTests : IDisposable
    {
        private const string Csv =
            "filename,title,game,volume\n" +
            "normal.nus3audio,Normal,game,1\n" +
            "boosted.nus3audio,Boosted,game,1.9\n" +
            "legacy.nus3audio,Legacy,game,2.7\n" +
            "blank.nus3audio,Blank,game,\n";

        private readonly DesktopWorkspace _ws = new();
        private readonly BuildValidationService _service;

        public BuildValidationServiceTests()
        {
            _service = new BuildValidationService(_ws.MusicOptions(), TestEnvironment.CreateLogger<BuildValidationService>());
        }

        public void Dispose() => _ws.Dispose();

        [Fact]
        public void Validate_FlagsVolumesAtOrAboveTheThreshold()
        {
            _ws.WriteSeries("persona", "persona", Csv);

            var result = _service.Validate("persona");
            var track = Assert.Single(result.SuspiciousVolumes);
            Assert.Equal(("persona", "persona", "legacy.nus3audio", "Legacy", 2.7f),
                (track.ModName, track.SeriesName, track.Filename, track.Title, track.Volume));
            Assert.Contains(result.Warnings, w => w.Contains("legacy.nus3audio"));
        }

        [Fact]
        public void Validate_FlagsTracksTheGlobalMultiplierPushesOverTheThreshold()
        {
            _ws.Options.Sma5hMusic.GlobalVolumeMultiplier = 2f;
            _ws.WriteSeries("persona", "persona", Csv);

            var flagged = _service.Validate("persona").SuspiciousVolumes;
            Assert.Equal(new[] { "normal.nus3audio", "boosted.nus3audio", "legacy.nus3audio", "blank.nus3audio" }, flagged.Select(t => t.Filename));
            Assert.Equal(5.4f, flagged.Single(t => t.Filename == "legacy.nus3audio").EffectiveVolume, precision: 3);
        }

        [Fact]
        public void Validate_WithoutAModNameChecksEveryMod()
        {
            _ws.WriteSeries("persona", "persona", Csv);
            _ws.WriteSeries("mario", "mario", Csv);

            Assert.Equal(new[] { "mario", "persona" }, _service.Validate(null).SuspiciousVolumes.Select(t => t.ModName).OrderBy(n => n));
        }

        [Fact]
        public void Validate_IgnoresCsvWithoutAVolumeColumn()
        {
            _ws.WriteSeries("persona", "persona", "filename,title,game\nsong.flac,Song,Game\n");
            Assert.Empty(_service.Validate("persona").SuspiciousVolumes);
        }
    }
}
