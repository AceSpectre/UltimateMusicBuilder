using Microsoft.Extensions.Logging;
using Moq;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using Tests.Helpers;
using Xunit;

namespace Tests.Integration
{
    public class FolderMusicModTests : IDisposable
    {
        private readonly TestEnvironment _env;

        public FolderMusicModTests()
        {
            _env = new TestEnvironment();
            FolderMusicMod.SeriesFilterByMod = null;
        }

        public void Dispose() => _env.Dispose();

        private FolderMusicMod CreateMod(string modDir)
        {
            return new FolderMusicMod(
                TestEnvironment.CreateLogger<IMusicMod>(),
                TestEnvironment.CreateMockAudioMetadata().Object,
                modDir);
        }

        // ── New series (dev/) ──────────────────────────────────────────────

        [Fact]
        public void NewSeries_CreatesSeriesEntry()
        {
            var modDir = _env.CreateConfiguredMod();
            var mod = CreateMod(modDir);
            var entries = mod.GetMusicModEntries();

            var series = entries.SeriesEntries;
            Assert.Single(series);
            Assert.Equal("ui_series_dev", series[0].UiSeriesId);
            Assert.Equal("dev", series[0].NameId);
            Assert.Equal("Somewhat Good: Karts", series[0].MSBTTitle["en_us"]);
        }

        [Fact]
        public void NewSeries_SetsIconPath()
        {
            var modDir = _env.CreateConfiguredMod();
            var mod = CreateMod(modDir);
            var entries = mod.GetMusicModEntries();

            var series = entries.SeriesEntries.Single();
            Assert.NotNull(series.IconPath);
            Assert.True(File.Exists(series.IconPath));
        }

        [Fact]
        public void NewSeries_CreatesGameTitleEntry()
        {
            var modDir = _env.CreateConfiguredMod();
            var mod = CreateMod(modDir);
            var entries = mod.GetMusicModEntries();

            var devGames = entries.GameTitleEntries
                .Where(g => g.UiSeriesId == "ui_series_dev").ToList();
            Assert.Single(devGames);
            Assert.Equal("ui_gametitle_somewhat_good_karts", devGames[0].UiGameTitleId);
            Assert.Equal("Somewhat Good: Karts", devGames[0].MSBTTitle["en_us"]);
        }

        [Fact]
        public void NewSeries_Creates13BgmDbRootEntries()
        {
            var modDir = _env.CreateConfiguredMod();
            var mod = CreateMod(modDir);
            var entries = mod.GetMusicModEntries();

            var devRoots = entries.BgmDbRootEntries
                .Where(e => e.UiGameTitleId == "ui_gametitle_somewhat_good_karts").ToList();
            Assert.Equal(13, devRoots.Count);
        }

        [Fact]
        public void NewSeries_BgmDbRootHasCorrectMetadata()
        {
            var modDir = _env.CreateConfiguredMod();
            var mod = CreateMod(modDir);
            var entries = mod.GetMusicModEntries();

            var first = entries.BgmDbRootEntries
                .First(e => e.Title.ContainsKey("en_us") && e.Title["en_us"] == "KARTS!");
            Assert.Equal("record_original", first.RecordType);
            Assert.Equal("Flowerhead", first.Author["en_us"]);
            Assert.Contains("CC BY 4.0", first.Copyright["en_us"]);
            Assert.StartsWith("ui_bgm_", first.UiBgmId);
        }

        [Fact]
        public void NewSeries_AllEntryTypesHaveMatchingCounts()
        {
            var modDir = _env.CreateConfiguredMod();
            var mod = CreateMod(modDir);
            var entries = mod.GetMusicModEntries();

            var devRootCount = entries.BgmDbRootEntries
                .Count(e => e.UiGameTitleId == "ui_gametitle_somewhat_good_karts");
            var devStreamSetCount = entries.BgmStreamSetEntries
                .Count(e => e.MusicMod == mod);

            Assert.Equal(13, devRootCount);
            Assert.True(entries.BgmStreamSetEntries.Count >= 13);
            Assert.True(entries.BgmAssignedInfoEntries.Count >= 13);
            Assert.True(entries.BgmStreamPropertyEntries.Count >= 13);
            Assert.True(entries.BgmPropertyEntries.Count >= 13);
        }

        [Fact]
        public void NewSeries_CreatesPlaylistEntries()
        {
            var modDir = _env.CreateConfiguredMod();
            var mod = CreateMod(modDir);
            var entries = mod.GetMusicModEntries();

            var gametitlePlaylist = entries.PlaylistEntries
                .FirstOrDefault(p => p.Id == "bgm_gametitle_dev");
            Assert.NotNull(gametitlePlaylist);
            Assert.Equal(13, gametitlePlaylist.Tracks.Count);

            var seriesPlaylist = entries.PlaylistEntries
                .FirstOrDefault(p => p.Id == "bgm_dev");
            Assert.NotNull(seriesPlaylist);
            Assert.Equal(13, seriesPlaylist.Tracks.Count);
        }

        [Fact]
        public void NewSeries_PlaylistIncidenceSetCorrectly()
        {
            var modDir = _env.CreateConfiguredMod();
            var mod = CreateMod(modDir);
            var entries = mod.GetMusicModEntries();

            var playlist = entries.PlaylistEntries.First(p => p.Id == "bgm_dev");
            foreach (var track in playlist.Tracks)
                Assert.Equal(100, track.Incidence0);
        }

        // ── Existing series (mario/) ───────────────────────────────────────

        [Fact]
        public void ExistingSeries_NoSeriesEntryCreated()
        {
            var modDir = _env.CreateConfiguredMod();
            var mod = CreateMod(modDir);
            var entries = mod.GetMusicModEntries();

            Assert.DoesNotContain(entries.SeriesEntries, s => s.UiSeriesId == "ui_series_mario");
        }

        [Fact]
        public void ExistingSeries_CreatesGameTitleEntry()
        {
            var modDir = _env.CreateConfiguredMod();
            var mod = CreateMod(modDir);
            var entries = mod.GetMusicModEntries();

            var marioGames = entries.GameTitleEntries
                .Where(g => g.UiSeriesId == "ui_series_mario").ToList();
            Assert.Single(marioGames);
            Assert.Equal("ui_gametitle_somewhat_good_lofi", marioGames[0].UiGameTitleId);
        }

        [Fact]
        public void ExistingSeries_Creates6BgmDbRootEntries()
        {
            var modDir = _env.CreateConfiguredMod();
            var mod = CreateMod(modDir);
            var entries = mod.GetMusicModEntries();

            var marioRoots = entries.BgmDbRootEntries
                .Where(e => e.UiGameTitleId == "ui_gametitle_somewhat_good_lofi").ToList();
            Assert.Equal(6, marioRoots.Count);
        }

        [Fact]
        public void ExistingSeries_CreatesPlaylistEntries()
        {
            var modDir = _env.CreateConfiguredMod();
            var mod = CreateMod(modDir);
            var entries = mod.GetMusicModEntries();

            var gametitlePlaylist = entries.PlaylistEntries
                .FirstOrDefault(p => p.Id == "bgm_gametitle_mario");
            Assert.NotNull(gametitlePlaylist);
            Assert.Equal(6, gametitlePlaylist.Tracks.Count);

            var seriesPlaylist = entries.PlaylistEntries
                .FirstOrDefault(p => p.Id == "bgmmario");
            Assert.NotNull(seriesPlaylist);
            Assert.Equal(6, seriesPlaylist.Tracks.Count);
        }

        // ── Combined totals ────────────────────────────────────────────────

        [Fact]
        public void CombinedMod_TotalEntries()
        {
            var modDir = _env.CreateConfiguredMod();
            var mod = CreateMod(modDir);
            var entries = mod.GetMusicModEntries();

            Assert.Equal(19, entries.BgmDbRootEntries.Count);
            Assert.Equal(19, entries.BgmStreamSetEntries.Count);
            Assert.Equal(19, entries.BgmAssignedInfoEntries.Count);
            Assert.Equal(19, entries.BgmStreamPropertyEntries.Count);
            Assert.Equal(19, entries.BgmPropertyEntries.Count);
            Assert.Equal(2, entries.GameTitleEntries.Count);
            Assert.Equal(1, entries.SeriesEntries.Count);
        }

        [Fact]
        public void CombinedMod_AllToneIdsUnique()
        {
            var modDir = _env.CreateConfiguredMod();
            var mod = CreateMod(modDir);
            var entries = mod.GetMusicModEntries();

            var toneIds = entries.BgmPropertyEntries.Select(e => e.NameId).ToList();
            Assert.Equal(toneIds.Count, toneIds.Distinct().Count());
        }

        [Fact]
        public void CombinedMod_AllUiBgmIdsUnique()
        {
            var modDir = _env.CreateConfiguredMod();
            var mod = CreateMod(modDir);
            var entries = mod.GetMusicModEntries();

            var ids = entries.BgmDbRootEntries.Select(e => e.UiBgmId).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }

        // ── ID format validation ───────────────────────────────────────────

        [Fact]
        public void AllGeneratedIds_HaveCorrectPrefixes()
        {
            var modDir = _env.CreateConfiguredMod();
            var mod = CreateMod(modDir);
            var entries = mod.GetMusicModEntries();

            foreach (var e in entries.BgmDbRootEntries)
                Assert.StartsWith(MusicConstants.InternalIds.UI_BGM_ID_PREFIX, e.UiBgmId);
            foreach (var e in entries.BgmStreamSetEntries)
                Assert.StartsWith(MusicConstants.InternalIds.STREAM_SET_PREFIX, e.StreamSetId);
            foreach (var e in entries.BgmAssignedInfoEntries)
                Assert.StartsWith(MusicConstants.InternalIds.INFO_ID_PREFIX, e.InfoId);
            foreach (var e in entries.BgmStreamPropertyEntries)
                Assert.StartsWith(MusicConstants.InternalIds.STREAM_PREFIX, e.StreamId);
        }

        [Fact]
        public void AllGeneratedIds_WithinMaxSize()
        {
            var modDir = _env.CreateConfiguredMod();
            var mod = CreateMod(modDir);
            var entries = mod.GetMusicModEntries();

            foreach (var e in entries.BgmDbRootEntries)
                Assert.True(e.UiBgmId.Length <= MusicConstants.GameResources.DbRootIdMaximumSize,
                    $"UiBgmId '{e.UiBgmId}' exceeds max ({MusicConstants.GameResources.DbRootIdMaximumSize})");
            foreach (var e in entries.BgmStreamSetEntries)
                Assert.True(e.StreamSetId.Length <= MusicConstants.GameResources.StreamSetIdMaximumSize,
                    $"StreamSetId '{e.StreamSetId}' exceeds max ({MusicConstants.GameResources.StreamSetIdMaximumSize})");
        }

        // ── Audio metadata ─────────────────────────────────────────────────

        [Fact]
        public void AudioMetadataPopulatedFromMock()
        {
            var modDir = _env.CreateConfiguredMod();
            var mod = CreateMod(modDir);
            var entries = mod.GetMusicModEntries();

            foreach (var prop in entries.BgmPropertyEntries)
            {
                Assert.Equal(100000u, prop.TotalTimeMs);
                Assert.Equal(4800000u, prop.TotalSamples);
            }
        }

        // ── Edge cases ─────────────────────────────────────────────────────

        [Fact]
        public void MissingAudioFile_SkipsTrack()
        {
            var modDir = _env.CreateConfiguredMod();
            File.Delete(Path.Combine(modDir, "dev",
                "flowerhead - Somewhat Good- Karts - 01 KARTS!.flac"));

            var mod = CreateMod(modDir);
            var entries = mod.GetMusicModEntries();

            var devRoots = entries.BgmDbRootEntries
                .Where(e => e.UiGameTitleId == "ui_gametitle_somewhat_good_karts").ToList();
            Assert.Equal(12, devRoots.Count);
        }

        [Fact]
        public void MissingSeriesToml_SkipsSubfolder()
        {
            var modDir = _env.CreateConfiguredMod();
            File.Delete(Path.Combine(modDir, "dev", "series.toml"));

            var mod = CreateMod(modDir);
            var entries = mod.GetMusicModEntries();

            Assert.DoesNotContain(entries.SeriesEntries, s => s.UiSeriesId == "ui_series_dev");
            Assert.Equal(6, entries.BgmDbRootEntries.Count);
        }

        [Fact]
        public void SeriesFilter_OnlyProcessesAllowedSeries()
        {
            var modDir = _env.CreateConfiguredMod();
            FolderMusicMod.SeriesFilterByMod = new Dictionary<string, HashSet<string>>
            {
                [modDir] = new HashSet<string> { "dev" }
            };

            var mod = CreateMod(modDir);
            var entries = mod.GetMusicModEntries();

            Assert.Equal(13, entries.BgmDbRootEntries.Count);
            Assert.DoesNotContain(entries.GameTitleEntries,
                g => g.UiGameTitleId == "ui_gametitle_somewhat_good_lofi");

            FolderMusicMod.SeriesFilterByMod = null;
        }
    }
}
