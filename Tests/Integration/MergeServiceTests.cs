using Spectre.Console;
using Spectre.Console.Testing;
using Tests.Helpers;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Integration
{
    /// <summary>
    /// Drives MergeService.Run() through Spectre's TestConsole. Each test
    /// builds two synthetic mods in <c>_env.ModPath</c>, pushes the keys
    /// needed for the MultiSelectionPrompt + TextPrompt + (conditional)
    /// priority SelectionPrompt, then asserts the merged output.
    /// </summary>
    [Collection("CwdSensitive")]
    public class MergeServiceTests : IDisposable
    {
        private readonly TestEnvironment _env;
        private readonly IAnsiConsole _originalConsole;

        public MergeServiceTests()
        {
            _env = new TestEnvironment();
            _originalConsole = AnsiConsole.Console;
        }

        public void Dispose()
        {
            AnsiConsole.Console = _originalConsole;
            _env.Dispose();
        }

        private MergeService CreateService()
        {
            return new MergeService(
                _env.CreateMusicOptions(),
                TestEnvironment.CreateLogger<MergeService>());
        }

        private static TestConsole SetupConsole(string outputName, bool conflictPrompt)
        {
            var console = new TestConsole();
            console.Profile.Capabilities.Interactive = true;
            console.Interactive();

            // MultiSelectionPrompt: toggle item-1, down, toggle item-2, enter.
            console.Input.PushKey(ConsoleKey.Spacebar);
            console.Input.PushKey(ConsoleKey.DownArrow);
            console.Input.PushKey(ConsoleKey.Spacebar);
            console.Input.PushKey(ConsoleKey.Enter);

            // TextPrompt: output name
            console.Input.PushTextWithEnter(outputName);

            // Priority SelectionPrompt (only fires when there's a conflict):
            // press Enter to pick the highlighted item (first selected mod).
            if (conflictPrompt)
                console.Input.PushKey(ConsoleKey.Enter);

            AnsiConsole.Console = console;
            return console;
        }

        // ── Helpers to build synthetic mods ────────────────────────────────

        private string BuildMod(string modName, string seriesId, string seriesName,
            string trackFilename, byte[] audioBytes,
            string playlistName = null)
        {
            var modDir = Path.Combine(_env.ModPath, modName);
            var seriesDir = Path.Combine(modDir, seriesId);
            Directory.CreateDirectory(seriesDir);

            playlistName ??= seriesName;
            File.WriteAllText(Path.Combine(seriesDir, "series.toml"),
                $"[series]\n" +
                $"id = \"{seriesId}\"\n" +
                $"name = \"{seriesName}\"\n" +
                $"playlist-incidence = 100\n" +
                $"series-playlist = \"{playlistName}\"\n" +
                "\n[[games]]\n" +
                $"id = \"{seriesId}\"\n" +
                $"name = \"{seriesName}\"\n" +
                "\n[default-track-data]\n" +
                $"game = \"{seriesId}\"\n");

            File.WriteAllText(Path.Combine(seriesDir, "tracks.csv"),
                "filename,game,title,author,copyright,record_type,special_category,volume,info1,in_soundtest\n" +
                $"{trackFilename},{seriesId},Title from {modName},,,,original,,1.0,,True\n");

            File.WriteAllBytes(Path.Combine(seriesDir, trackFilename), audioBytes);
            return modDir;
        }

        // ── Tests ──────────────────────────────────────────────────────────

        [Fact]
        public void Merge_PriorityModWinsOnAudioCollision()
        {
            // Both mods carry the same series ("clash") containing the same
            // filename but with different bytes. Test console selects both
            // and picks mod-a as the priority via the conflict prompt.
            BuildMod("mod-a", "clash", "Clash", "track.nus3audio",
                new byte[] { 0xAA, 0xAA, 0xAA, 0xAA });
            BuildMod("mod-b", "clash", "Clash B", "track.nus3audio",
                new byte[] { 0xBB, 0xBB, 0xBB, 0xBB });

            SetupConsole("merged", conflictPrompt: true);
            CreateService().Run();

            var merged = Path.Combine(_env.ModPath, "merged", "clash", "track.nus3audio");
            Assert.True(File.Exists(merged));
            var bytes = File.ReadAllBytes(merged);
            Assert.Equal(new byte[] { 0xAA, 0xAA, 0xAA, 0xAA }, bytes);
        }

        [Fact]
        public void Merge_PriorityModSeriesTomlWins()
        {
            // mod-a series.toml: name="Clash A", series-playlist="bgm_clash_a"
            // mod-b series.toml: name="Clash B", series-playlist="bgm_clash_b"
            // After merge with priority=mod-a, series.toml should reflect mod-a.
            BuildMod("mod-a", "clash", "Clash A", "track-a.nus3audio",
                new byte[] { 0x01 }, playlistName: "bgm_clash_a");
            BuildMod("mod-b", "clash", "Clash B", "track-b.nus3audio",
                new byte[] { 0x02 }, playlistName: "bgm_clash_b");

            SetupConsole("merged", conflictPrompt: true);
            CreateService().Run();

            var mergedToml = File.ReadAllText(
                Path.Combine(_env.ModPath, "merged", "clash", "series.toml"));
            Assert.Contains("name = \"Clash A\"", mergedToml);
            Assert.Contains("bgm_clash_a", mergedToml);
            Assert.DoesNotContain("bgm_clash_b", mergedToml);
        }

        [Fact]
        public void Merge_TrackOrderColumnSequential()
        {
            BuildMod("mod-a", "clash", "Clash", "track-a.nus3audio",
                new byte[] { 0x01 });
            BuildMod("mod-b", "clash", "Clash B", "track-b.nus3audio",
                new byte[] { 0x02 });

            SetupConsole("merged", conflictPrompt: true);
            CreateService().Run();

            var csv = File.ReadAllLines(
                Path.Combine(_env.ModPath, "merged", "clash", "tracks.csv"));
            // header + 2 rows merged
            Assert.Equal(3, csv.Length);
            Assert.Contains("order", csv[0]);
            Assert.EndsWith(",0", csv[1]);
            Assert.EndsWith(",1", csv[2]);
        }

        [Fact]
        public void Merge_NoConflictsSkipsPriorityPrompt()
        {
            // Each mod has its own unique series — no conflict, so no
            // priority prompt is fired. Confirm both series appear in output.
            BuildMod("mod-a", "series-a", "Series A", "a.nus3audio",
                new byte[] { 0x01 }, playlistName: "bgm_a");
            BuildMod("mod-b", "series-b", "Series B", "b.nus3audio",
                new byte[] { 0x02 }, playlistName: "bgm_b");

            SetupConsole("merged", conflictPrompt: false);
            CreateService().Run();

            Assert.True(Directory.Exists(Path.Combine(_env.ModPath, "merged", "series-a")));
            Assert.True(Directory.Exists(Path.Combine(_env.ModPath, "merged", "series-b")));
        }
    }
}
