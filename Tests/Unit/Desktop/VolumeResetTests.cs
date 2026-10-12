using Moq;
using Sma5h.Mods.Music.Interfaces;
using Tests.Helpers;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Unit.Desktop
{
    public class VolumeResetTests : IDisposable
    {
        private readonly DesktopWorkspace _ws = new();
        private readonly VolumeConfigService _service;

        public VolumeResetTests()
        {
            _service = new VolumeConfigService(_ws.MusicOptions(), new Mock<ILufsAnalysisService>().Object,
                new Mock<IAudioDecodeService>().Object, TestEnvironment.CreateLogger<VolumeConfigService>());
        }

        public void Dispose() => _ws.Dispose();

        [Fact]
        public void ResetAllVolumes_SetsEveryTrackInEveryModToZero()
        {
            var persona = _ws.WriteSeries("persona", "persona", "filename,title,volume_db\na.nus3audio,A,8.6\nb.nus3audio,B,0\n");
            var mario = _ws.WriteSeries("mario", "mario", "filename,title,volume_db\nc.nus3audio,C,-6\n");

            _service.ResetAllVolumes();
            Assert.Equal("filename,title,volume_db\na.nus3audio,A,0.0\nb.nus3audio,B,0\n", ReadCsv(persona));
            Assert.Equal("filename,title,volume_db\nc.nus3audio,C,0.0\n", ReadCsv(mario));
        }

        [Fact]
        public void ResetAllVolumes_ResetsTheSeriesBoost()
        {
            var series = _ws.WriteSeries("persona", "persona", "filename,title\n", "[series]\nid = \"persona\"\n\n[default-track-data]\nvolume-db = -6.0\n");

            _service.ResetAllVolumes();
            Assert.Equal(0, SeriesToml.ReadVolumeDb(series));
        }

        [Fact]
        public void ResetAllVolumes_LeavesCsvWithoutAVolumeColumnUntouched()
        {
            const string csv = "filename,title\na.nus3audio,A\n";
            var series = _ws.WriteSeries("persona", "persona", csv);

            _service.ResetAllVolumes();
            Assert.Equal(csv, ReadCsv(series));
        }

        [Fact]
        public void ResetAllVolumes_DoesNothingWithoutTheModsFolder()
        {
            Directory.Delete(_ws.ModsRoot);
            _service.ResetAllVolumes();
        }

        [Fact]
        public void ResetAllVolumes_SkipsDotSeriesFolders()
        {
            const string csv = "filename,title,volume_db\na.nus3audio,A,8.6\n";
            var hidden = _ws.WriteSeries("persona", ".hidden", csv);

            _service.ResetAllVolumes();
            Assert.Equal(csv, ReadCsv(hidden));
        }

        private static string ReadCsv(string seriesDir) =>
            File.ReadAllText(Path.Combine(seriesDir, "tracks.csv")).Replace("\r\n", "\n");
    }
}
