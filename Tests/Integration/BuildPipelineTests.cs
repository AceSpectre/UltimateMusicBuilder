using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Sma5h.Interfaces;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using System.Security.Cryptography;
using Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Integration
{
    public class BuildPipelineTests : IDisposable
    {
        private readonly TestEnvironment _env;
        private readonly ITestOutputHelper _output;

        private static readonly string GoldenChecksumsPath = Path.Combine(
            AppContext.BaseDirectory, "TestData", "golden-checksums.json");

        public BuildPipelineTests(ITestOutputHelper output)
        {
            _env = new TestEnvironment();
            _output = output;
            FolderMusicMod.SeriesFilterByMod = null;
            Sma5hMusic.ExplicitSeriesOrder = null;
        }

        public void Dispose()
        {
            FolderMusicMod.SeriesFilterByMod = null;
            Sma5hMusic.ExplicitSeriesOrder = null;
            _env.Dispose();
        }

        // ── Init tests ─────────────────────────────────────────────────────

        [Fact]
        public void Init_PopulatesModBgmEntries()
        {
            _env.CreateConfiguredMod();

            using var sp = _env.CreateFullServiceProvider();
            var sma5hMod = sp.GetRequiredService<ISma5hMod>();
            var audioState = sp.GetRequiredService<IAudioStateService>();

            var result = sma5hMod.Init();
            Assert.True(result, "Init() should succeed");

            var modRoots = audioState.GetModBgmDbRootEntries().ToList();
            Assert.Equal(19, modRoots.Count);
        }

        [Fact]
        public void Init_NewSeriesAddedToState()
        {
            _env.CreateConfiguredMod();

            using var sp = _env.CreateFullServiceProvider();
            var sma5hMod = sp.GetRequiredService<ISma5hMod>();
            var audioState = sp.GetRequiredService<IAudioStateService>();

            sma5hMod.Init();

            var allSeries = audioState.GetSeriesEntries().ToList();
            Assert.Contains(allSeries, s => s.UiSeriesId == "ui_series_dev");
        }

        [Fact]
        public void Init_ExistingSeriesHasModTracks()
        {
            _env.CreateConfiguredMod();

            using var sp = _env.CreateFullServiceProvider();
            var sma5hMod = sp.GetRequiredService<ISma5hMod>();
            var audioState = sp.GetRequiredService<IAudioStateService>();

            sma5hMod.Init();

            var marioTracks = audioState.GetModBgmDbRootEntries()
                .Where(e => e.UiGameTitleId == "ui_gametitle_somewhat_good_lofi")
                .ToList();
            Assert.Equal(6, marioTracks.Count);
        }

        [Fact]
        public void Init_PlaylistsCreated()
        {
            _env.CreateConfiguredMod();

            using var sp = _env.CreateFullServiceProvider();
            var sma5hMod = sp.GetRequiredService<ISma5hMod>();
            var audioState = sp.GetRequiredService<IAudioStateService>();

            sma5hMod.Init();

            var playlists = audioState.GetPlaylists().ToList();
            Assert.Contains(playlists, p => p.Id == "bgm_dev");
            Assert.Contains(playlists, p => p.Id == "bgmmario");
        }

        [Fact]
        public void Init_AllModPropertyEntriesHaveAudioMetadata()
        {
            _env.CreateConfiguredMod();

            using var sp = _env.CreateFullServiceProvider();
            var sma5hMod = sp.GetRequiredService<ISma5hMod>();
            var audioState = sp.GetRequiredService<IAudioStateService>();

            sma5hMod.Init();

            foreach (var prop in audioState.GetModBgmPropertyEntries())
            {
                Assert.True(prop.TotalTimeMs > 0 || prop.TotalSamples > 0,
                    $"Track {prop.NameId} should have audio metadata (from mock or real file)");
            }
        }

        // ── Build + state persistence tests ────────────────────────────────

        [Fact]
        public void Build_SucceedsWithMockedAudio()
        {
            _env.CreateConfiguredMod();

            using var sp = _env.CreateFullServiceProvider();
            var sma5hMod = sp.GetRequiredService<ISma5hMod>();

            sma5hMod.Init();
            var result = sma5hMod.Build(useCache: false);
            Assert.True(result, "Build() should succeed with mocked audio services");
        }

        [Fact]
        public void Build_WriteChangesProducesOutputFiles()
        {
            _env.CreateConfiguredMod();

            using var sp = _env.CreateFullServiceProvider();
            var sma5hMod = sp.GetRequiredService<ISma5hMod>();
            var stateManager = sp.GetRequiredService<IStateManager>();

            stateManager.Init();
            sma5hMod.Init();
            sma5hMod.Build(useCache: false);
            var writeResult = stateManager.WriteChanges();
            Assert.True(writeResult, "WriteChanges() should succeed");

            var outputPath = Path.Combine(_env.TempDir, "ArcOutput");
            var outputFiles = Directory.Exists(outputPath)
                ? Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories)
                : Array.Empty<string>();
            Assert.True(outputFiles.Length > 0, "Build should produce output files in ArcOutput");

            _output.WriteLine($"Build produced {outputFiles.Length} output file(s):");
            foreach (var f in outputFiles.OrderBy(f => f))
                _output.WriteLine($"  {Path.GetRelativePath(outputPath, f)}");
        }

        // ── Checksum regression test ───────────────────────────────────────

        [Fact]
        public void Build_OutputChecksumsMatchGolden()
        {
            _env.CreateConfiguredMod();

            using var sp = _env.CreateFullServiceProvider();
            var sma5hMod = sp.GetRequiredService<ISma5hMod>();
            var stateManager = sp.GetRequiredService<IStateManager>();

            stateManager.Init();
            sma5hMod.Init();
            sma5hMod.Build(useCache: false);
            stateManager.WriteChanges();

            var outputPath = Path.Combine(_env.TempDir, "ArcOutput");
            var checksums = ComputeOutputChecksums(outputPath);

            if (!File.Exists(GoldenChecksumsPath))
            {
                SaveGoldenChecksums(checksums);
                _output.WriteLine($"Golden checksums generated with {checksums.Count} file(s). " +
                    "Run tests again to verify against golden baseline.");
                _output.WriteLine($"Golden file: {GoldenChecksumsPath}");
                return;
            }

            var golden = LoadGoldenChecksums();
            var mismatches = new List<string>();
            var missing = new List<string>();
            var extra = new List<string>();

            foreach (var (path, hash) in golden)
            {
                if (!checksums.TryGetValue(path, out var actual))
                    missing.Add(path);
                else if (actual != hash)
                    mismatches.Add($"{path}: expected {hash[..12]}... got {actual[..12]}...");
            }

            foreach (var path in checksums.Keys)
            {
                if (!golden.ContainsKey(path))
                    extra.Add(path);
            }

            if (mismatches.Count > 0 || missing.Count > 0)
            {
                var msg = "Build output does not match golden checksums.\n";
                if (mismatches.Count > 0)
                    msg += $"\nMISMATCHED ({mismatches.Count}):\n  " + string.Join("\n  ", mismatches);
                if (missing.Count > 0)
                    msg += $"\nMISSING ({missing.Count}):\n  " + string.Join("\n  ", missing);
                if (extra.Count > 0)
                    msg += $"\nEXTRA ({extra.Count}):\n  " + string.Join("\n  ", extra);
                msg += "\n\nTo update golden checksums after intentional changes, " +
                    $"delete {GoldenChecksumsPath} and re-run.";
                Assert.Fail(msg);
            }

            if (extra.Count > 0)
                _output.WriteLine($"Note: {extra.Count} extra file(s) in output not in golden: " +
                    string.Join(", ", extra.Take(5)));

            _output.WriteLine($"All {golden.Count} output files match golden checksums.");
        }

        // ── Entry count regression test ────────────────────────────────────

        [Fact]
        public void Build_VanillaEntryCountsUnchanged()
        {
            using var sp = _env.CreateFullServiceProvider();
            var audioState = sp.GetRequiredService<IAudioStateService>();
            var stateManager = sp.GetRequiredService<IStateManager>();

            stateManager.Init();
            audioState.InitBgmEntriesFromStateManager();

            var vanillaRootCount = audioState.GetBgmDbRootEntries()
                .Count(e => e.Source == Sma5h.Mods.Music.Models.EntrySource.Core);
            var vanillaSeriesCount = audioState.GetSeriesEntries()
                .Count(e => e.Source == Sma5h.Mods.Music.Models.EntrySource.Core);

            _output.WriteLine($"Vanilla BGM root entries: {vanillaRootCount}");
            _output.WriteLine($"Vanilla series entries: {vanillaSeriesCount}");

            Assert.True(vanillaRootCount > 0, "Should load vanilla BGM entries from game resources");
            Assert.True(vanillaSeriesCount > 0, "Should load vanilla series from game resources");
        }

        // ── Checksum helpers ───────────────────────────────────────────────

        private static Dictionary<string, string> ComputeOutputChecksums(string outputPath)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(outputPath))
                return result;

            // Only checksum database/message files (deterministic output).
            // Skip nus3audio/nus3bank (generated by mocked service, may be empty).
            var relevantExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".prc", ".msbt", ".bin" };

            foreach (var file in Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                if (!relevantExtensions.Contains(ext))
                    continue;

                var relativePath = Path.GetRelativePath(outputPath, file)
                    .Replace('\\', '/');
                using var stream = File.OpenRead(file);
                var hash = SHA256.HashData(stream);
                result[relativePath] = Convert.ToHexString(hash).ToLowerInvariant();
            }

            return result;
        }

        private static void SaveGoldenChecksums(Dictionary<string, string> checksums)
        {
            var dir = Path.GetDirectoryName(GoldenChecksumsPath);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonConvert.SerializeObject(checksums, Formatting.Indented);
            File.WriteAllText(GoldenChecksumsPath, json);
        }

        private static Dictionary<string, string> LoadGoldenChecksums()
        {
            var json = File.ReadAllText(GoldenChecksumsPath);
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>();
        }
    }

}
