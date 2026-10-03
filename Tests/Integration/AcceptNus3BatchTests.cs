using System.Text;
using Tests.Helpers;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Integration
{
    /// <summary>
    /// Drives <see cref="AcceptNus3Service.Accept"/> — the non-interactive accept used by
    /// the desktop app; it runs the same AcceptCore orchestration as the interactive Run().
    /// </summary>
    [Collection("CwdSensitive")]
    public class AcceptNus3BatchTests : IDisposable
    {
        private readonly TestEnvironment _env;

        public AcceptNus3BatchTests()
        {
            _env = new TestEnvironment();
        }

        public void Dispose() => _env.Dispose();

        private AcceptNus3Service CreateService()
        {
            var options = _env.CreateMusicOptions();
            var scaffold = new ScaffoldService(
                options,
                TestEnvironment.CreateMockAudioStateService().Object,
                TestEnvironment.CreateLogger<ScaffoldService>());
            return new AcceptNus3Service(
                options,
                TestEnvironment.CreateLogger<AcceptNus3Service>(),
                scaffold);
        }

        private string SetupModWithValidateFolder(string seriesName = "dev",
            params (string nus3FileName, string preExistingSourceExt)[] tracks)
        {
            var modDir = Path.Combine(_env.ModPath, "test-mod");
            var seriesDir = Path.Combine(modDir, seriesName);
            Directory.CreateDirectory(seriesDir);

            File.WriteAllText(Path.Combine(seriesDir, "series.toml"),
                $"[series]\nid = \"{seriesName}\"\nname = \"{seriesName}\"\n" +
                $"playlist-incidence = 100\nseries-playlist = \"bgm_{seriesName}\"\n" +
                $"\n[[games]]\nid = \"{seriesName}\"\nname = \"{seriesName}\"\n" +
                $"\n[default-track-data]\ngame = \"{seriesName}\"\n");

            var csvRows = new StringBuilder();
            csvRows.AppendLine("filename,game,title,author,copyright,record_type,special_category,volume,info1,in_soundtest");
            foreach (var (nus3FileName, srcExt) in tracks)
            {
                var basename = Path.GetFileNameWithoutExtension(nus3FileName);
                csvRows.AppendLine($"{basename}{srcExt},{seriesName},{basename},,,,original,,1.0,,True");
                File.WriteAllBytes(Path.Combine(seriesDir, basename + srcExt), new byte[] { 0xFF, 0xFE });
            }
            File.WriteAllText(Path.Combine(seriesDir, "tracks.csv"), csvRows.ToString());

            var validateDir = Path.Combine(seriesDir, "songs-to-validate");
            Directory.CreateDirectory(validateDir);
            foreach (var (nus3FileName, _) in tracks)
                File.WriteAllBytes(Path.Combine(validateDir, nus3FileName),
                    new byte[] { 0x4E, 0x55, 0x53, 0x33 }); // NUS3 magic

            return modDir;
        }

        // ── Happy paths ─────────────────────────────────────────────────────

        [Fact]
        public void Accept_MovesNus3AudioFilesIntoSeriesFolder()
        {
            var modDir = SetupModWithValidateFolder("dev",
                ("track1.nus3audio", ".flac"),
                ("track2.nus3audio", ".flac"));
            var seriesDir = Path.Combine(modDir, "dev");

            CreateService().Accept(seriesDir, false);

            Assert.True(File.Exists(Path.Combine(seriesDir, "track1.nus3audio")));
            Assert.True(File.Exists(Path.Combine(seriesDir, "track2.nus3audio")));
            Assert.False(Directory.Exists(Path.Combine(seriesDir, "songs-to-validate")));
        }

        [Fact]
        public void Accept_DeletesSourceFilesWhenTrue()
        {
            var modDir = SetupModWithValidateFolder("dev", ("track1.nus3audio", ".flac"));
            var seriesDir = Path.Combine(modDir, "dev");

            CreateService().Accept(seriesDir, true);

            Assert.True(File.Exists(Path.Combine(seriesDir, "track1.nus3audio")));
            Assert.False(File.Exists(Path.Combine(seriesDir, "track1.flac")),
                "Source .flac should be deleted when deleteSources is true");
        }

        [Fact]
        public void Accept_KeepsSourceFilesWhenFalse()
        {
            var modDir = SetupModWithValidateFolder("dev", ("track1.nus3audio", ".flac"));
            var seriesDir = Path.Combine(modDir, "dev");

            CreateService().Accept(seriesDir, false);

            Assert.True(File.Exists(Path.Combine(seriesDir, "track1.nus3audio")));
            Assert.True(File.Exists(Path.Combine(seriesDir, "track1.flac")),
                "Source .flac should be preserved when deleteSources is false");
        }

        [Fact]
        public void Accept_UpdatesCsvFilenameExtension()
        {
            var modDir = SetupModWithValidateFolder("dev", ("track1.nus3audio", ".flac"));
            var seriesDir = Path.Combine(modDir, "dev");

            CreateService().Accept(seriesDir, false);

            var csv = File.ReadAllText(Path.Combine(seriesDir, "tracks.csv"));
            Assert.Contains("track1.nus3audio", csv);
            Assert.DoesNotContain("track1.flac", csv);
        }

        [Fact]
        public void Accept_CleansUpValidateFolderAfterMove()
        {
            var modDir = SetupModWithValidateFolder("dev",
                ("track1.nus3audio", ".flac"),
                ("track2.nus3audio", ".flac"));
            var seriesDir = Path.Combine(modDir, "dev");

            CreateService().Accept(seriesDir, false);

            Assert.False(Directory.Exists(Path.Combine(seriesDir, "songs-to-validate")));
        }

        // ── Validation / early-return branches ──────────────────────────────

        [Fact]
        public void Accept_NoValidateFolder_NoOp()
        {
            var modDir = Path.Combine(_env.ModPath, "test-mod");
            var seriesDir = Path.Combine(modDir, "dev");
            Directory.CreateDirectory(seriesDir);

            CreateService().Accept(seriesDir, false);

            Assert.False(Directory.Exists(Path.Combine(seriesDir, "songs-to-validate")));
        }

        [Fact]
        public void Accept_ValidateFolderEmpty_LeavesSeriesUntouched()
        {
            var modDir = Path.Combine(_env.ModPath, "test-mod");
            var seriesDir = Path.Combine(modDir, "dev");
            var validateDir = Path.Combine(seriesDir, "songs-to-validate");
            Directory.CreateDirectory(validateDir); // exists but holds no .nus3audio
            CreateService().Accept(seriesDir, false);

            // Guard warns and returns; the empty validate folder is left in place.
            Assert.True(Directory.Exists(validateDir));
            Assert.Empty(Directory.GetFiles(seriesDir, "*.nus3audio"));
        }
    }
}
