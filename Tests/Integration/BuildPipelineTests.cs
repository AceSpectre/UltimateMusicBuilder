using Microsoft.Extensions.DependencyInjection;
using Sma5h.Interfaces;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Integration
{
    [Collection("CwdSensitive")]
    public class BuildPipelineTests : IDisposable
    {
        private readonly TestEnvironment _env;
        private readonly ITestOutputHelper _output;

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

        // ── Baseline regression tests ──────────────────────────────────────

        private string BaselineDir(string scenario)
            => Path.Combine(BaselineGenerator.BaselineRoot(_env.RepoRoot), scenario);

        [Fact]
        public void Build_OutputMatchesDefaultBaseline()
        {
            _env.CreateConfiguredMod();
            RunBuild();

            BuildBaselineFallback.AssertMatches(
                "default-build",
                Path.Combine(_env.TempDir, "ArcOutput"),
                BaselineDir("default-build"),
                _env.RepoRoot,
                _output);
        }

        [Fact]
        public void Build_SeriesOrderReflectedInArcOutput()
        {
            // Builds with an additional "gamma" custom series ordered ahead of "dev"
            // via series-order.toml — same setup BaselineGenerator.SetupSeriesOrdered uses.
            BaselineGenerator.SetupSeriesOrdered(_env);
            BaselineGenerator.RunBuild(_env);

            BuildBaselineFallback.AssertMatches(
                "series-ordered",
                Path.Combine(_env.TempDir, "ArcOutput"),
                BaselineDir("series-ordered"),
                _env.RepoRoot,
                _output);
        }

        [Fact]
        public void Build_TrackOrderReflectedInArcOutput()
        {
            BaselineGenerator.SetupTrackOrdered(_env);
            BaselineGenerator.RunBuild(_env);

            BuildBaselineFallback.AssertMatches(
                "track-ordered",
                Path.Combine(_env.TempDir, "ArcOutput"),
                BaselineDir("track-ordered"),
                _env.RepoRoot,
                _output);
        }

        private void RunBuild() => BaselineGenerator.RunBuild(_env);

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

    }

}
