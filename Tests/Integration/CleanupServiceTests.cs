using Sma5h.Mods.Music.Helpers;
using Tests.Helpers;
using UMB.CLI.Desktop;
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

        private CleanupService CreateService(VanillaCatalogService.Catalog catalog = null)
        {
            return new CleanupService(
                _env.CreateMusicOptions(),
                TestCatalog.Service(catalog ?? TestCatalog.Mario()),
                TestEnvironment.CreateLogger<CleanupService>());
        }

        private const string KartsSong = "ui_bgm_flowerhead___somewhat_good__karts___01_karts";

        private static string TomlWithSongs(params string[] songs) =>
            "[[playlists]]\nid = \"bgmmine\"\ntracks = [\n" +
            string.Concat(songs.Select(s => $"    {{ song = \"{s}\", order = 0, incidence = 100 }},\n")) + "]\n";

        [Fact]
        public void Cleanup_RemovesStalePlaylistsTomlSongs_KeepingVanillaAndModSongs()
        {
            var modDir = _env.CreateConfiguredMod();
            var path = Path.Combine(modDir, "playlists.toml");
            File.WriteAllText(path, TomlWithSongs("ui_bgm_a01_smb_chijyou", KartsSong, "ui_bgm_gone"));

            CreateService().Run();

            Assert.Equal(TomlWithSongs("ui_bgm_a01_smb_chijyou", KartsSong), File.ReadAllText(path));
        }

        [Fact]
        public void Cleanup_LeavesPlaylistsTomlWithoutGameResources()
        {
            var modDir = _env.CreateConfiguredMod();
            var path = Path.Combine(modDir, "playlists.toml");
            File.WriteAllText(path, TomlWithSongs("ui_bgm_gone"));

            new CleanupService(_env.CreateMusicOptions(), TestCatalog.Service(), TestEnvironment.CreateLogger<CleanupService>()).Run();

            Assert.Contains("ui_bgm_gone", File.ReadAllText(path));
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
        public void Cleanup_KeepsVanillaSongEdits()
        {
            var modDir = _env.CreateConfiguredMod();
            File.AppendAllText(Path.Combine(modDir, "mario", "tracks.csv"),
                "ui_bgm_a01_smb_chijyou,somewhat_good_lofi,Overworld,,,original,,0,,True\n");

            CreateService().Run();

            Assert.Contains("ui_bgm_a01_smb_chijyou", File.ReadAllText(Path.Combine(modDir, "mario", "tracks.csv")));
        }

        [Fact]
        public void Preview_ListsEveryEntryExecuteRemoves()
        {
            var modDir = _env.CreateConfiguredMod();
            const string karts = "flowerhead - Somewhat Good- Karts - 01 KARTS!.flac";
            File.Delete(Path.Combine(modDir, "dev", karts));
            File.AppendAllText(Path.Combine(modDir, "dev", "series.toml"), "\n[[playlists]]\nid = \"bgm_dev\"\nsongs = [\"01 gone\", \"" + karts + "\"]\n");
            File.WriteAllText(Path.Combine(modDir, "playlists.toml"), TomlWithSongs("ui_bgm_a01_smb_chijyou", KartsSong, "ui_bgm_gone"));
            var service = CreateService();

            var expected = new[]
            {
                new CleanupItem("dev/tracks.csv", karts),
                new CleanupItem("dev/series.toml", "01 gone"),
                new CleanupItem("dev/series.toml", karts),
                new CleanupItem("playlists.toml", $"bgmmine: {KartsSong}"),
                new CleanupItem("playlists.toml", "bgmmine: ui_bgm_gone"),
            };
            Assert.Equal(expected, service.Preview(modDir));
            Assert.Equal(expected, service.Execute(modDir));
            Assert.Empty(service.Preview(modDir));
        }

        [Fact]
        public void Execute_KeepsEveryColumnOfTheRowsLeft()
        {
            var modDir = _env.CreateConfiguredMod();
            var csv = Path.Combine(modDir, "dev", "tracks.csv");
            File.WriteAllText(csv, "filename,title,order,extra\nflowerhead - Somewhat Good- Karts - 01 KARTS!.flac,Karts,0,x\ngone.flac,Gone,1,y\n");

            CreateService().Execute(modDir);

            Assert.Equal("filename,title,order,extra\nflowerhead - Somewhat Good- Karts - 01 KARTS!.flac,Karts,0,x\n", File.ReadAllText(csv));
        }

        [Fact]
        public void Preview_ChangesNothing()
        {
            var modDir = _env.CreateConfiguredMod();
            File.Delete(Path.Combine(modDir, "dev", "flowerhead - Somewhat Good- Karts - 01 KARTS!.flac"));
            var csv = Path.Combine(modDir, "dev", "tracks.csv");
            var before = File.ReadAllText(csv);

            Assert.Single(CreateService().Preview(modDir));
            Assert.Equal(before, File.ReadAllText(csv));
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
