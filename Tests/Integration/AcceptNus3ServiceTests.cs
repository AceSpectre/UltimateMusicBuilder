using Spectre.Console;
using Spectre.Console.Testing;
using Tests.Helpers;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Integration
{
    /// <summary>
    /// Invokes <see cref="AcceptNus3Service.Run()"/> end-to-end via Spectre
    /// TestConsole. Validates the orchestration: nus3audio files move from
    /// songs-to-validate/ into the series folder, source extensions are
    /// rewritten in tracks.csv, and the validate folder is cleaned up.
    /// </summary>
    [Collection("CwdSensitive")]
    public class AcceptNus3ServiceTests : IDisposable
    {
        private readonly TestEnvironment _env;
        private readonly IAnsiConsole _originalConsole;

        public AcceptNus3ServiceTests()
        {
            _env = new TestEnvironment();
            _originalConsole = AnsiConsole.Console;
        }

        public void Dispose()
        {
            AnsiConsole.Console = _originalConsole;
            _env.Dispose();
        }

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

        /// <summary>
        /// Push the keystrokes Spectre needs:
        ///   - PromptModAndSeries auto-selects when 1 mod / 1 series exist
        ///   - "Delete original source audio files?" SelectionPrompt — Enter
        ///     accepts the first option ("Yes — delete source files"), or
        ///     DownArrow + Enter to pick "No — keep source files".
        /// </summary>
        private static void SetupConsole(bool deleteSources)
        {
            var console = new TestConsole();
            console.Profile.Capabilities.Interactive = true;
            console.Interactive();
            if (!deleteSources)
                console.Input.PushKey(ConsoleKey.DownArrow);
            console.Input.PushKey(ConsoleKey.Enter);
            AnsiConsole.Console = console;
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

            var csvRows = new System.Text.StringBuilder();
            csvRows.AppendLine("filename,game,title,author,copyright,record_type,special_category,volume,info1,in_soundtest");
            foreach (var (nus3FileName, srcExt) in tracks)
            {
                var basename = Path.GetFileNameWithoutExtension(nus3FileName);
                csvRows.AppendLine($"{basename}{srcExt},{seriesName},{basename},,,,original,,1.0,,True");
                // Place pre-existing source file
                File.WriteAllBytes(Path.Combine(seriesDir, basename + srcExt),
                    new byte[] { 0xFF, 0xFE });
            }
            File.WriteAllText(Path.Combine(seriesDir, "tracks.csv"), csvRows.ToString());

            var validateDir = Path.Combine(seriesDir, "songs-to-validate");
            Directory.CreateDirectory(validateDir);
            foreach (var (nus3FileName, _) in tracks)
            {
                File.WriteAllBytes(Path.Combine(validateDir, nus3FileName),
                    new byte[] { 0x4E, 0x55, 0x53, 0x33 }); // NUS3 magic
            }

            return modDir;
        }

        // ── Tests ──────────────────────────────────────────────────────────

        [Fact]
        public void Accept_MovesNus3AudioFilesIntoSeriesFolder()
        {
            var modDir = SetupModWithValidateFolder("dev",
                ("track1.nus3audio", ".flac"),
                ("track2.nus3audio", ".flac"));

            SetupConsole(deleteSources: false);
            CreateService().Run();

            var seriesDir = Path.Combine(modDir, "dev");
            Assert.True(File.Exists(Path.Combine(seriesDir, "track1.nus3audio")));
            Assert.True(File.Exists(Path.Combine(seriesDir, "track2.nus3audio")));
            Assert.False(Directory.Exists(Path.Combine(seriesDir, "songs-to-validate")));
        }

        [Fact]
        public void Accept_DeletesSourceFilesWhenChosen()
        {
            var modDir = SetupModWithValidateFolder("dev",
                ("track1.nus3audio", ".flac"));

            SetupConsole(deleteSources: true);
            CreateService().Run();

            var seriesDir = Path.Combine(modDir, "dev");
            Assert.True(File.Exists(Path.Combine(seriesDir, "track1.nus3audio")));
            Assert.False(File.Exists(Path.Combine(seriesDir, "track1.flac")),
                "Source .flac should be deleted when Yes is selected");
        }

        [Fact]
        public void Accept_KeepsSourceFilesWhenChosenNo()
        {
            var modDir = SetupModWithValidateFolder("dev",
                ("track1.nus3audio", ".flac"));

            SetupConsole(deleteSources: false);
            CreateService().Run();

            var seriesDir = Path.Combine(modDir, "dev");
            Assert.True(File.Exists(Path.Combine(seriesDir, "track1.nus3audio")));
            Assert.True(File.Exists(Path.Combine(seriesDir, "track1.flac")),
                "Source .flac should be preserved when No is selected");
        }

        [Fact]
        public void Accept_UpdatesCsvFilenameExtension()
        {
            var modDir = SetupModWithValidateFolder("dev",
                ("track1.nus3audio", ".flac"));

            SetupConsole(deleteSources: false);
            CreateService().Run();

            var csv = File.ReadAllText(Path.Combine(modDir, "dev", "tracks.csv"));
            Assert.Contains("track1.nus3audio", csv);
            Assert.DoesNotContain("track1.flac", csv);
        }

        [Fact]
        public void Accept_CleansUpValidateFolderAfterMove()
        {
            var modDir = SetupModWithValidateFolder("dev",
                ("track1.nus3audio", ".flac"),
                ("track2.nus3audio", ".flac"));

            SetupConsole(deleteSources: false);
            CreateService().Run();

            var validateDir = Path.Combine(modDir, "dev", "songs-to-validate");
            Assert.False(Directory.Exists(validateDir));
        }

        [Fact]
        public void Accept_NoOpWhenValidateFolderMissing()
        {
            var modDir = Path.Combine(_env.ModPath, "test-mod");
            Directory.CreateDirectory(Path.Combine(modDir, "dev"));
            File.WriteAllText(Path.Combine(modDir, "dev", "series.toml"),
                "[series]\nid = \"dev\"\nname = \"dev\"\nplaylist-incidence = 100\nseries-playlist = \"bgm_dev\"\n\n[[games]]\nid = \"dev\"\nname = \"dev\"\n\n[default-track-data]\ngame = \"dev\"\n");

            SetupConsole(deleteSources: false);
            CreateService().Run();

            // Service should warn and return without throwing; no validate folder
            // existed, no nus3 files were created, no exception.
            Assert.False(Directory.Exists(Path.Combine(modDir, "dev", "songs-to-validate")));
        }
    }
}
