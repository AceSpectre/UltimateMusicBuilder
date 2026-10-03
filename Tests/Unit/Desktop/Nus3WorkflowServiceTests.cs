using System.Text.Json;
using Tests.Helpers;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Unit.Desktop
{
    public class Nus3WorkflowServiceTests : IDisposable
    {
        private readonly DesktopWorkspace _ws = new();
        private readonly Nus3WorkflowService _service;
        private readonly string _seriesDir;

        public Nus3WorkflowServiceTests()
        {
            var converter = new Nus3ConvertService(_ws.MusicOptions(), TestEnvironment.CreateLogger<Nus3ConvertService>());
            _service = new Nus3WorkflowService(converter);
            _seriesDir = _ws.ModDir("mod", "series");
        }

        public void Dispose() => _ws.Dispose();

        private string ValidateDir => Path.Combine(_seriesDir, "songs-to-validate");

        // With no source file the cache can't be invalidated, so a seeded entry is always used.
        private void SeedCache(string filename, object entry) =>
            DesktopWorkspace.WriteFile(ValidateDir, ".analysis-cache.json", JsonSerializer.Serialize(new Dictionary<string, object> { [filename] = entry }));

        [Fact]
        public void ListSources_ListsAudioFilesWithPrettifiedNames()
        {
            foreach (var file in new[] { "mass-destruction.flac", "song_two.mp3", "clip.wav", "track.ogg", "notes.txt" })
                DesktopWorkspace.WriteFile(_seriesDir, file, "x");
            _ws.ModDir("mod", "series", "subdir");

            var tracks = _service.ListSources(_seriesDir).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();

            Assert.Equal(new[] { "clip.wav", "mass-destruction.flac", "song_two.mp3", "track.ogg" }, tracks.Select(t => t.Id));
            var md = tracks.Single(t => t.Id == "mass-destruction.flac");
            Assert.Equal(("Mass Destruction", "mass-destruction.flac", "—", 0.0, false), (md.Name, md.Src, md.Duration, md.DurationSeconds, md.Converted));
        }

        [Fact]
        public void ListSources_HidesAcceptedTracks()
        {
            DesktopWorkspace.WriteFile(_seriesDir, "done.flac", "a");
            DesktopWorkspace.WriteFile(_seriesDir, "done.nus3audio", "x");
            DesktopWorkspace.WriteFile(_seriesDir, "pending.flac", "b");

            Assert.Equal(new[] { "pending.flac" }, _service.ListSources(_seriesDir).Select(t => t.Id));
        }

        [Fact]
        public void ListSources_MarksStagedTracksConverted()
        {
            DesktopWorkspace.WriteFile(_seriesDir, "staged.flac", "a");
            DesktopWorkspace.WriteFile(ValidateDir, "staged.nus3audio", "x");

            Assert.True(_service.ListSources(_seriesDir).Single(t => t.Id == "staged.flac").Converted);
        }

        [Fact]
        public void ListSources_IsEmptyForAMissingFolder()
        {
            Assert.Empty(_service.ListSources(Path.Combine(_ws.Root, "nope")));
        }

        [Fact]
        public void LoadConversions_IsEmptyWithoutAFile()
        {
            Assert.Empty(_service.LoadConversions(_seriesDir));
        }

        [Fact]
        public void LoadConversions_ReadsTheRecords()
        {
            DesktopWorkspace.WriteFile(ValidateDir, ".conversions.json", "{\"a.flac\":{\"mode\":\"loop\"}}");

            var conversions = _service.LoadConversions(_seriesDir);

            Assert.Equal("loop", Assert.Single(conversions, c => c.Key == "a.flac").Value.Mode);
        }

        [Fact]
        public void GetTrackDuration_UsesTheCache()
        {
            SeedCache("a.flac", new { mtimeMs = 0, size = 0, duration = "1:30", durationSeconds = 90 });

            Assert.Equal(90, _service.GetTrackDuration(Path.Combine(_seriesDir, "a.flac")));
        }

        [Fact]
        public void AnalyzeLoopPoints_UsesCachedCandidates()
        {
            SeedCache("song-name.flac", new
            {
                mtimeMs = 0, size = 0, duration = "0:30", durationSeconds = 30,
                candidates = new[] { new { rank = 1, score = 99, loopStart = 1, loopEnd = 2, seam = "smooth" } }
            });

            var result = _service.AnalyzeLoopPoints(Path.Combine(_seriesDir, "song-name.flac"), new());

            var candidate = Assert.Single(result.Candidates);
            Assert.Equal((1, 99.0, 1.0, 2.0, "smooth"), (candidate.Rank, candidate.Score, candidate.LoopStart, candidate.LoopEnd, candidate.Seam));
            Assert.Equal(("Song Name", "0:30", false), (result.Track.Name, result.Track.Duration, result.Track.Converted));
        }

        [Fact]
        public void ExtractWaveformPeaks_UsesCachedPeaks()
        {
            SeedCache("a.flac", new { mtimeMs = 0, size = 0, duration = "0:30", durationSeconds = 30, peaks = new[] { 0.1, 0.2, 0.3 } });

            Assert.Equal(new[] { 0.1, 0.2, 0.3 }, _service.ExtractWaveformPeaks(Path.Combine(_seriesDir, "a.flac"), 140));
        }

        [Fact]
        public void RejectTrack_DeletesTheStagedFileAndForgetsItsDecision()
        {
            DesktopWorkspace.WriteFile(ValidateDir, "w.nus3audio", "x");
            DesktopWorkspace.WriteFile(ValidateDir, ".conversions.json", "{\"w.flac\":{\"mode\":\"loop\"},\"k.flac\":{\"mode\":\"loop\"}}");

            _service.RejectTrack(_seriesDir, "w.flac");

            Assert.False(File.Exists(Path.Combine(ValidateDir, "w.nus3audio")));
            Assert.Equal(new[] { "k.flac" }, _service.LoadConversions(_seriesDir).Keys);
            Assert.Equal("{\"k.flac\":{\"mode\":\"loop\"}}", File.ReadAllText(Path.Combine(ValidateDir, ".conversions.json")));
        }
    }
}
