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
        public void ResetAllVolumes_SetsEveryTrackInEveryModToOne()
        {
            var persona = _ws.WriteSeries("persona", "persona", "filename,title,volume\na.nus3audio,A,2.7\nb.nus3audio,B,1\n");
            var mario = _ws.WriteSeries("mario", "mario", "filename,title,volume\nc.nus3audio,C,0.5\n");

            Assert.Equal(2, _service.ResetAllVolumes());
            Assert.Equal("filename,title,volume\na.nus3audio,A,1\nb.nus3audio,B,1\n", ReadCsv(persona));
            Assert.Equal("filename,title,volume\nc.nus3audio,C,1\n", ReadCsv(mario));
        }

        [Fact]
        public void ResetAllVolumes_LeavesCsvWithoutAVolumeColumnUntouched()
        {
            const string csv = "filename,title\na.nus3audio,A\n";
            var series = _ws.WriteSeries("persona", "persona", csv);

            Assert.Equal(0, _service.ResetAllVolumes());
            Assert.Equal(csv, ReadCsv(series));
        }

        [Fact]
        public void ResetAllVolumes_IsZeroWithoutTheModsFolder()
        {
            Directory.Delete(_ws.ModsRoot);
            Assert.Equal(0, _service.ResetAllVolumes());
        }

        private static string ReadCsv(string seriesDir) =>
            File.ReadAllText(Path.Combine(seriesDir, "tracks.csv")).Replace("\r\n", "\n");
    }
}
