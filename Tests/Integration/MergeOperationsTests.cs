using Sma5h.Mods.Music.Helpers;
using Tests.Helpers;
using Xunit;

namespace Tests.Integration
{
    /// <summary>
    /// Tests merge-like operations at the file level. MergeService.Run() requires
    /// Spectre.Console prompts, so we test the underlying file operations directly.
    /// </summary>
    public class MergeOperationsTests : IDisposable
    {
        private readonly TestEnvironment _env;

        public MergeOperationsTests()
        {
            _env = new TestEnvironment();
        }

        public void Dispose() => _env.Dispose();

        [Fact]
        public void MergeTwoMods_NonConflictingSeries_CopiesBoth()
        {
            var mod1 = _env.CreateConfiguredMod("mod-a");
            var mod2Dir = Path.Combine(_env.ModPath, "mod-b");
            Directory.CreateDirectory(Path.Combine(mod2Dir, "custom-series"));
            File.WriteAllText(Path.Combine(mod2Dir, "custom-series", "series.toml"),
                "[series]\nid = \"custom\"\nname = \"Custom\"\nplaylist-incidence = 100\nseries-playlist = \"bgm_custom\"\n\n[[games]]\nid = \"custom\"\nname = \"Custom\"\n\n[default-track-data]\ngame = \"custom\"\n");
            File.WriteAllText(Path.Combine(mod2Dir, "custom-series", "tracks.csv"),
                "filename,game,title,author,copyright,record_type,special_category,volume,info1,in_soundtest\ntest.nus3audio,custom,Test,Author,Copyright,original,,1,,True\n");
            File.WriteAllBytes(Path.Combine(mod2Dir, "custom-series", "test.nus3audio"),
                Array.Empty<byte>());

            var outputDir = Path.Combine(_env.ModPath, "merged");
            Directory.CreateDirectory(outputDir);

            CopySeriesFolder(Path.Combine(mod1, "dev"), Path.Combine(outputDir, "dev"));
            CopySeriesFolder(Path.Combine(mod1, "mario"), Path.Combine(outputDir, "mario"));
            CopySeriesFolder(Path.Combine(mod2Dir, "custom-series"),
                Path.Combine(outputDir, "custom-series"));

            Assert.True(File.Exists(Path.Combine(outputDir, "dev", "series.toml")));
            Assert.True(File.Exists(Path.Combine(outputDir, "mario", "series.toml")));
            Assert.True(File.Exists(Path.Combine(outputDir, "custom-series", "series.toml")));
            Assert.True(File.Exists(Path.Combine(outputDir, "dev", "tracks.csv")));
            Assert.True(File.Exists(Path.Combine(outputDir, "custom-series", "tracks.csv")));
        }

        [Fact]
        public void MergedMod_CanBeLoadedByFolderMusicMod()
        {
            var mod1 = _env.CreateConfiguredMod("mod-a");
            var outputDir = Path.Combine(_env.ModPath, "merged");
            Directory.CreateDirectory(outputDir);

            CopySeriesFolder(Path.Combine(mod1, "dev"), Path.Combine(outputDir, "dev"));

            // Copy stub audio files too
            foreach (var f in Directory.GetFiles(Path.Combine(mod1, "dev"), "*.flac"))
                File.Copy(f, Path.Combine(outputDir, "dev", Path.GetFileName(f)));

            var mod = new Sma5h.Mods.Music.MusicMods.FolderMusicMod.FolderMusicMod(
                TestEnvironment.CreateLogger<Sma5h.Mods.Music.Interfaces.IMusicMod>(),
                TestEnvironment.CreateMockAudioMetadata().Object,
                outputDir);

            var entries = mod.GetMusicModEntries();
            Assert.Equal(13, entries.BgmDbRootEntries.Count);
        }

        [Fact]
        public void MergedSeriesOrderToml_CombinesEntries()
        {
            var orderA = Path.Combine(_env.TempDir, "order-a.toml");
            File.WriteAllText(orderA, "order = [\n    \"series-a\",\n    \"series-b\",\n]\n");

            var orderB = Path.Combine(_env.TempDir, "order-b.toml");
            File.WriteAllText(orderB, "order = [\n    \"series-c\",\n    \"series-b\",\n]\n");

            // Simulate merge: read both, combine, priority keeps first occurrence
            var combinedOrder = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in new[] { orderA, orderB })
            {
                var text = File.ReadAllText(file);
                foreach (var line in text.Split('\n'))
                {
                    var trimmed = line.Trim().Trim('"', ',', ' ');
                    if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("order")
                        && !trimmed.StartsWith("#") && !trimmed.StartsWith("[")
                        && !trimmed.StartsWith("]") && seen.Add(trimmed))
                    {
                        combinedOrder.Add(trimmed);
                    }
                }
            }

            Assert.Equal(3, combinedOrder.Count);
            Assert.Equal("series-a", combinedOrder[0]);
            Assert.Equal("series-b", combinedOrder[1]);
            Assert.Equal("series-c", combinedOrder[2]);
        }

        private static void CopySeriesFolder(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source))
            {
                var ext = Path.GetExtension(file).ToLower();
                if (ext is ".toml" or ".csv" or ".png")
                    File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
            }
        }
    }
}
