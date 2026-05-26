using Sma5h.Mods.Music.Helpers;
using Tests.Helpers;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Integration
{
    public class CleanupServiceTests : IDisposable
    {
        private readonly TestEnvironment _env;

        public CleanupServiceTests()
        {
            _env = new TestEnvironment();
        }

        public void Dispose() => _env.Dispose();

        private CleanupService CreateService()
        {
            return new CleanupService(
                _env.CreateMusicOptions(),
                TestEnvironment.CreateLogger<CleanupService>());
        }

        [Fact]
        public void Cleanup_RemovesMissingAudioEntries()
        {
            var modDir = _env.CreateConfiguredMod();
            File.Delete(Path.Combine(modDir, "dev",
                "flowerhead - Somewhat Good- Karts - 01 KARTS!.flac"));
            File.Delete(Path.Combine(modDir, "dev",
                "flowerhead - Somewhat Good- Karts - 02 Choose Your Racer.flac"));

            var service = CreateService();
            service.Run();

            var csvContent = File.ReadAllText(Path.Combine(modDir, "dev", "tracks.csv"));
            Assert.DoesNotContain("KARTS!.flac", csvContent);
            Assert.DoesNotContain("Choose Your Racer.flac", csvContent);
        }

        [Fact]
        public void Cleanup_PreservesExistingAudioEntries()
        {
            var modDir = _env.CreateConfiguredMod();
            File.Delete(Path.Combine(modDir, "dev",
                "flowerhead - Somewhat Good- Karts - 01 KARTS!.flac"));

            var service = CreateService();
            service.Run();

            var csvContent = File.ReadAllText(Path.Combine(modDir, "dev", "tracks.csv"));
            Assert.Contains("Flowey Speedway", csvContent);
            Assert.Contains("Time Trials", csvContent);
        }

        [Fact]
        public void Cleanup_CorrectRemainingCount()
        {
            var modDir = _env.CreateConfiguredMod();
            File.Delete(Path.Combine(modDir, "dev",
                "flowerhead - Somewhat Good- Karts - 01 KARTS!.flac"));

            var service = CreateService();
            service.Run();

            var lines = File.ReadAllLines(Path.Combine(modDir, "dev", "tracks.csv"));
            Assert.Equal(13, lines.Length); // header + 12 remaining tracks
        }

        [Fact]
        public void Cleanup_NoChangesWhenAllFilesPresent()
        {
            var modDir = _env.CreateConfiguredMod();
            var csvBefore = File.ReadAllText(Path.Combine(modDir, "dev", "tracks.csv"));

            var service = CreateService();
            service.Run();

            var csvAfter = File.ReadAllText(Path.Combine(modDir, "dev", "tracks.csv"));
            Assert.Equal(csvBefore, csvAfter);
        }

        [Fact]
        public void Cleanup_HandlesMultipleSeries()
        {
            var modDir = _env.CreateConfiguredMod();
            File.Delete(Path.Combine(modDir, "dev",
                "flowerhead - Somewhat Good- Karts - 13 Time Trials.flac"));
            File.Delete(Path.Combine(modDir, "mario",
                "flowerhead - Somewhat Good- Lofi - 06 7pm.flac"));

            var service = CreateService();
            service.Run();

            var devCsv = File.ReadAllText(Path.Combine(modDir, "dev", "tracks.csv"));
            Assert.DoesNotContain("Time Trials", devCsv);

            var marioCsv = File.ReadAllText(Path.Combine(modDir, "mario", "tracks.csv"));
            Assert.DoesNotContain("7pm", marioCsv);
        }

        [Fact]
        public void Cleanup_PrunesDeadSongRefsFromToml()
        {
            var modDir = _env.CreateConfiguredMod();

            // Add a playlist with explicit song references to series.toml
            var tomlPath = Path.Combine(modDir, "dev", "series.toml");
            var toml = File.ReadAllText(tomlPath);
            toml += "\n[[playlists]]\nid = \"bgmtest\"\nsongs = [\"flowerhead - Somewhat Good- Karts - 01 KARTS!\", \"nonexistent_song\"]\n";
            File.WriteAllText(tomlPath, toml);

            // Delete the referenced track
            File.Delete(Path.Combine(modDir, "dev",
                "flowerhead - Somewhat Good- Karts - 01 KARTS!.flac"));

            var service = CreateService();
            service.Run();

            var tomlAfter = File.ReadAllText(tomlPath);
            Assert.DoesNotContain("nonexistent_song", tomlAfter);
            Assert.DoesNotContain("KARTS!", tomlAfter);
        }

        [Fact]
        public void Cleanup_RemovesAllTracks_LeavesHeaderOnly()
        {
            var modDir = _env.CreateConfiguredMod();

            // Delete all mario tracks
            var marioDir = Path.Combine(modDir, "mario");
            foreach (var f in Directory.GetFiles(marioDir, "*.flac"))
                File.Delete(f);

            var service = CreateService();
            service.Run();

            var lines = File.ReadAllLines(Path.Combine(marioDir, "tracks.csv"));
            Assert.Single(lines); // header only
        }
    }
}
