using System.Text;
using System.Text.Json;
using Tests.Helpers;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Integration
{
    /// <summary>
    /// Drives <see cref="AcceptNus3Service.RunBatch(string)"/> — the non-interactive
    /// entry point used by the desktop app. Reads
    /// { "seriesPath": "...", "deleteSources": true } and runs the same AcceptCore
    /// orchestration the interactive Run() does, so no Spectre console is needed.
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

        private string WriteJson(object input)
        {
            var path = Path.Combine(_env.TempDir, "accept-batch-" + Guid.NewGuid().ToString("N")[..8] + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(input));
            return path;
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
        public void RunBatch_MovesNus3AudioFilesIntoSeriesFolder()
        {
            var modDir = SetupModWithValidateFolder("dev",
                ("track1.nus3audio", ".flac"),
                ("track2.nus3audio", ".flac"));
            var seriesDir = Path.Combine(modDir, "dev");
            var json = WriteJson(new { seriesPath = seriesDir, deleteSources = false });

            CreateService().RunBatch(json);

            Assert.True(File.Exists(Path.Combine(seriesDir, "track1.nus3audio")));
            Assert.True(File.Exists(Path.Combine(seriesDir, "track2.nus3audio")));
            Assert.False(Directory.Exists(Path.Combine(seriesDir, "songs-to-validate")));
        }

        [Fact]
        public void RunBatch_DeletesSourceFilesWhenTrue()
        {
            var modDir = SetupModWithValidateFolder("dev", ("track1.nus3audio", ".flac"));
            var seriesDir = Path.Combine(modDir, "dev");
            var json = WriteJson(new { seriesPath = seriesDir, deleteSources = true });

            CreateService().RunBatch(json);

            Assert.True(File.Exists(Path.Combine(seriesDir, "track1.nus3audio")));
            Assert.False(File.Exists(Path.Combine(seriesDir, "track1.flac")),
                "Source .flac should be deleted when deleteSources is true");
        }

        [Fact]
        public void RunBatch_KeepsSourceFilesWhenFalse()
        {
            var modDir = SetupModWithValidateFolder("dev", ("track1.nus3audio", ".flac"));
            var seriesDir = Path.Combine(modDir, "dev");
            var json = WriteJson(new { seriesPath = seriesDir, deleteSources = false });

            CreateService().RunBatch(json);

            Assert.True(File.Exists(Path.Combine(seriesDir, "track1.nus3audio")));
            Assert.True(File.Exists(Path.Combine(seriesDir, "track1.flac")),
                "Source .flac should be preserved when deleteSources is false");
        }

        [Fact]
        public void RunBatch_UpdatesCsvFilenameExtension()
        {
            var modDir = SetupModWithValidateFolder("dev", ("track1.nus3audio", ".flac"));
            var seriesDir = Path.Combine(modDir, "dev");
            var json = WriteJson(new { seriesPath = seriesDir, deleteSources = false });

            CreateService().RunBatch(json);

            var csv = File.ReadAllText(Path.Combine(seriesDir, "tracks.csv"));
            Assert.Contains("track1.nus3audio", csv);
            Assert.DoesNotContain("track1.flac", csv);
        }

        [Fact]
        public void RunBatch_CleansUpValidateFolderAfterMove()
        {
            var modDir = SetupModWithValidateFolder("dev",
                ("track1.nus3audio", ".flac"),
                ("track2.nus3audio", ".flac"));
            var seriesDir = Path.Combine(modDir, "dev");
            var json = WriteJson(new { seriesPath = seriesDir, deleteSources = false });

            CreateService().RunBatch(json);

            Assert.False(Directory.Exists(Path.Combine(seriesDir, "songs-to-validate")));
        }

        // ── Validation / early-return branches ──────────────────────────────

        [Fact]
        public void RunBatch_NullPath_ReturnsWithoutThrowing()
        {
            var ex = Record.Exception(() => CreateService().RunBatch(null));
            Assert.Null(ex);
        }

        [Fact]
        public void RunBatch_MissingJsonFile_ReturnsWithoutThrowing()
        {
            var missing = Path.Combine(_env.TempDir, "nope.json");
            var ex = Record.Exception(() => CreateService().RunBatch(missing));
            Assert.Null(ex);
        }

        [Fact]
        public void RunBatch_MissingSeriesPath_ReturnsWithoutThrowing()
        {
            var json = WriteJson(new { deleteSources = true });
            var ex = Record.Exception(() => CreateService().RunBatch(json));
            Assert.Null(ex);
        }

        [Fact]
        public void RunBatch_NoValidateFolder_NoOp()
        {
            var modDir = Path.Combine(_env.ModPath, "test-mod");
            var seriesDir = Path.Combine(modDir, "dev");
            Directory.CreateDirectory(seriesDir);
            var json = WriteJson(new { seriesPath = seriesDir, deleteSources = false });

            CreateService().RunBatch(json);

            Assert.False(Directory.Exists(Path.Combine(seriesDir, "songs-to-validate")));
        }

        [Fact]
        public void RunBatch_ValidateFolderEmpty_LeavesSeriesUntouched()
        {
            var modDir = Path.Combine(_env.ModPath, "test-mod");
            var seriesDir = Path.Combine(modDir, "dev");
            var validateDir = Path.Combine(seriesDir, "songs-to-validate");
            Directory.CreateDirectory(validateDir); // exists but holds no .nus3audio
            var json = WriteJson(new { seriesPath = seriesDir, deleteSources = false });

            CreateService().RunBatch(json);

            // Guard warns and returns; the empty validate folder is left in place.
            Assert.True(Directory.Exists(validateDir));
            Assert.Empty(Directory.GetFiles(seriesDir, "*.nus3audio"));
        }
    }
}
