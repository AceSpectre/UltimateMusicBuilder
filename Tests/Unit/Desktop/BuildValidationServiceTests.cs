using Tests.Helpers;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Unit.Desktop
{
    public class BuildValidationServiceTests : IDisposable
    {
        private const string Csv =
            "filename,title,game,volume_db\n" +
            "normal.nus3audio,Normal,game,0\n" +
            "boosted.nus3audio,Boosted,game,5.5\n" +
            "loud.nus3audio,Loud,game,8.6\n" +
            "blank.nus3audio,Blank,game,\n";

        private readonly DesktopWorkspace _ws = new();
        private readonly BuildValidationService _service;

        public BuildValidationServiceTests()
        {
            _service = new BuildValidationService(_ws.MusicOptions(), TestEnvironment.CreateLogger<BuildValidationService>());
        }

        public void Dispose() => _ws.Dispose();

        [Fact]
        public void Validate_FlagsBoostsAtOrAboveTheThreshold()
        {
            _ws.WriteSeries("persona", "persona", Csv);

            var result = _service.Validate("persona");
            var track = Assert.Single(result.SuspiciousVolumes);
            Assert.Equal(("persona", "persona", "loud.nus3audio", "Loud", 8.6f),
                (track.ModName, track.SeriesName, track.Filename, track.Title, track.EffectiveDb));
            Assert.Contains(result.Warnings, w => w.Contains("loud.nus3audio"));
        }

        [Fact]
        public void Validate_GivesVanillaSongsNoBoost_UnlessApplyVolumeToVanilla()
        {
            _ws.Options.Sma5hMusic.GlobalVolumeDb = 6f;
            _ws.WriteSeries("persona", "persona", "filename,title,game,volume_db\nui_bgm_quiet,Quiet,game,0\nui_bgm_loud,Loud,game,9\n");
            Assert.Empty(_service.Validate("persona").SuspiciousVolumes);

            _ws.Options.Sma5hMusic.ApplyVolumeToVanilla = true;
            Assert.Equal(2, _service.Validate("persona").SuspiciousVolumes.Count);
        }

        [Fact]
        public void Validate_DoesNotNeedAnOrderForVanillaRows()
        {
            _ws.WriteSeries("persona", "persona", "filename,title,game,order\nui_bgm_quiet,Quiet,game,\n");

            Assert.DoesNotContain(_service.Validate("persona").Warnings, w => w.Contains("order number"));
        }

        [Fact]
        public void Validate_AddsTheGlobalBoost()
        {
            _ws.Options.Sma5hMusic.GlobalVolumeDb = 6f;
            _ws.WriteSeries("persona", "persona", Csv);

            var flagged = _service.Validate("persona").SuspiciousVolumes;
            Assert.Equal(new[] { "normal.nus3audio", "boosted.nus3audio", "loud.nus3audio", "blank.nus3audio" }, flagged.Select(t => t.Filename));
            Assert.Equal(14.6f, flagged.Single(t => t.Filename == "loud.nus3audio").EffectiveDb, precision: 3);
        }

        [Fact]
        public void Validate_AddsTheSeriesBoost()
        {
            _ws.WriteSeries("persona", "persona", Csv, "[series]\nid = \"persona\"\n\n[default-track-data]\nvolume-db = 1.0\n");

            var flagged = _service.Validate("persona").SuspiciousVolumes;
            Assert.Equal(new[] { "boosted.nus3audio", "loud.nus3audio" }, flagged.Select(t => t.Filename));
            Assert.Equal(6.5f, flagged[0].EffectiveDb, precision: 3);
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
