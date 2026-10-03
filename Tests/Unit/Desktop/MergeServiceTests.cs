using System.Text.RegularExpressions;
using Tests.Helpers;
using UMB.CLI.Desktop;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Unit.Desktop
{
    public class MergeServiceTests : IDisposable
    {
        private readonly DesktopWorkspace _ws = new();
        private readonly MergeService _service;

        public MergeServiceTests()
        {
            _service = new MergeService(_ws.MusicOptions(), TestEnvironment.CreateLogger<MergeService>());
        }

        public void Dispose() => _ws.Dispose();

        private static string MinimalToml(string id, string name) => $"[series]\nid = \"{id}\"\nname = \"{name}\"\n";

        /// <summary>A series folder with series.toml, tracks.csv and dummy audio files.</summary>
        private string Series(string mod, string series, string toml, string csv, params string[] audio)
        {
            var dir = _ws.WriteSeries(mod, series, csv, toml);
            foreach (var file in audio)
                DesktopWorkspace.WriteFile(dir, file, file.ToUpperInvariant());
            return dir;
        }

        // ── Analyze ──────────────────────────────────────────────────────────

        [Fact]
        public void Analyze_GroupsSeriesAcrossModsAndFlagsConflicts()
        {
            var a = _ws.ModDir("modA");
            var b = _ws.ModDir("modB");
            Series("modA", "persona", MinimalToml("persona", "Persona"), "filename\nx.flac\n");
            Series("modA", "mario", MinimalToml("mario", "Mario"), "filename\ny.flac\n");
            Series("modB", "persona", MinimalToml("persona", "Persona"), "filename\nz.flac\n");
            Series("modB", "zelda", MinimalToml("zelda", "Zelda"), "filename\nw.flac\n");

            var result = _service.Analyze(new() { a, b });

            Assert.Equal(new[] { "modA", "modB" }, result.ModNames);
            Assert.Equal(3, result.TotalSeries);
            Assert.Equal(new[] { "mario", "persona", "zelda" }, result.Series.Select(s => s.Name));
            var conflict = Assert.Single(result.Conflicts);
            Assert.Equal("persona", conflict.SeriesName);
            Assert.Equal(new[] { "modA", "modB" }, conflict.Mods);
        }

        [Fact]
        public void Analyze_SkipsDotfilesAndFoldersWithoutSeriesToml()
        {
            var a = _ws.ModDir("modA");
            Series("modA", "real", MinimalToml("real", "Real"), "filename\nx.flac\n");
            Series("modA", ".hidden", MinimalToml("h", "H"), "filename\nh.flac\n");
            _ws.ModDir("modA", "notoml");

            Assert.Equal(new[] { "real" }, _service.Analyze(new() { a }).Series.Select(s => s.Name));
        }

        [Fact]
        public void Analyze_RejectsPathsOutsideTheModsFolder()
        {
            Assert.StartsWith("Invalid mod path", Assert.Throws<DesktopApiException>(() =>
                _service.Analyze(new() { Path.Combine(_ws.Root, "outside") })).Message);
        }

        [Fact]
        public void Analyze_RejectsMissingMods()
        {
            Assert.StartsWith("Mod not found", Assert.Throws<DesktopApiException>(() =>
                _service.Analyze(new() { Path.Combine(_ws.ModsRoot, "ghost") })).Message);
        }

        // ── ValidateOutputName ───────────────────────────────────────────────

        [Theory]
        [InlineData("", "Name cannot be empty.")]
        [InlineData("   ", "Name cannot be empty.")]
        [InlineData("bad/name", "Name contains invalid characters.")]
        [InlineData("a:b", "Name contains invalid characters.")]
        [InlineData("fresh", null)]
        public void ValidateOutputName_ReportsTheProblem(string name, string expected)
        {
            Assert.Equal(expected, _service.ValidateOutputName(name));
        }

        [Fact]
        public void ValidateOutputName_RejectsAnExistingMod()
        {
            _ws.ModDir("taken");
            Assert.Equal("A mod with that name already exists.", _service.ValidateOutputName("taken"));
        }

        // ── Execute ──────────────────────────────────────────────────────────

        [Fact]
        public void Execute_CopiesASingleSourceSeries()
        {
            var a = _ws.ModDir("modA");
            Series("modA", "persona", MinimalToml("persona", "Persona"), "filename,title\na.flac,A\nb.flac,B\n", "a.flac", "b.flac");

            var result = _service.Execute(new() { a }, "merged", null);

            Assert.Equal((1, 2, 0), (result.TotalSeries, result.TotalTracks, result.ConflictsResolved));
            var outSeries = Path.Combine(result.OutputPath, "persona");
            foreach (var file in new[] { "series.toml", "tracks.csv", "a.flac", "b.flac" })
                Assert.True(File.Exists(Path.Combine(outSeries, file)), file);
        }

        [Fact]
        public void Execute_MergesAConflict_DedupingTracksAndUnioningGamesAndPlaylists()
        {
            var a = _ws.ModDir("modA");
            var b = _ws.ModDir("modB");
            var tomlA =
                "[series]\nid = \"persona\"\nname = \"Persona A\"\n\n" +
                "[[games]]\nid = \"p3\"\nname = \"Persona 3\"\n\n" +
                "[[playlists]]\nid = \"bgmjack\"\nincidence = 100\nsongs = \"*\"\n";
            var tomlB =
                "[series]\nid = \"persona\"\nname = \"Persona B\"\n\n" +
                "[[games]]\nid = \"p5\"\nname = \"Persona 5\"\n\n" +
                "[[playlists]]\nid = \"bgmjack\"\nincidence = 100\nsongs = \"*\"\n\n" +
                "[[playlists]]\nid = \"bgmextra\"\nincidence = 50\nsongs = \"*\"\n";
            Series("modA", "persona", tomlA, "filename,title\na.flac,Track A\n", "a.flac");
            Series("modB", "persona", tomlB, "filename,title\na.flac,Track A2\nb.flac,Track B\n", "a.flac", "b.flac");

            var result = _service.Execute(new() { a, b }, "merged", a);

            Assert.Equal((1, 1, 2), (result.ConflictsResolved, result.TotalSeries, result.TotalTracks));
            var outSeries = Path.Combine(result.OutputPath, "persona");
            var toml = File.ReadAllText(Path.Combine(outSeries, "series.toml"));
            foreach (var expected in new[] { "id = \"persona\"", "name = \"Persona A\"", "id = \"p3\"", "id = \"p5\"", "id = \"bgmjack\"", "id = \"bgmextra\"" })
                Assert.Contains(expected, toml);
            var csv = File.ReadAllText(Path.Combine(outSeries, "tracks.csv"));
            Assert.Contains("b.flac", csv);
            Assert.Contains("order", File.ReadLines(Path.Combine(outSeries, "tracks.csv")).First());
            Assert.Single(Regex.Matches(csv, @"a\.flac"));
        }

        [Fact]
        public void Execute_MergesSeriesOrder_PriorityFirstWithoutDuplicates()
        {
            var a = _ws.ModDir("modA");
            var b = _ws.ModDir("modB");
            Series("modA", "persona", MinimalToml("persona", "Persona"), "filename\na.flac\n");
            Series("modB", "mario", MinimalToml("mario", "Mario"), "filename\nb.flac\n");
            DesktopWorkspace.WriteFile(a, "series-order.toml", "order = [\n  \"x\",\n  \"y\"\n]\n");
            DesktopWorkspace.WriteFile(b, "series-order.toml", "order = [\n  \"y\",\n  \"z\"\n]\n");

            var result = _service.Execute(new() { a, b }, "merged", a);

            var order = File.ReadAllText(Path.Combine(result.OutputPath, "series-order.toml"));
            Assert.Equal(new[] { "x", "y", "z" }, Regex.Matches(order, @"^\s+""([^""]+)"",", RegexOptions.Multiline).Select(m => m.Groups[1].Value));
        }

        [Fact]
        public void Execute_RejectsAnInvalidOutputName()
        {
            var a = _ws.ModDir("modA");
            Series("modA", "persona", MinimalToml("persona", "Persona"), "filename\na.flac\n");

            Assert.Equal("Name contains invalid characters.",
                Assert.Throws<DesktopApiException>(() => _service.Execute(new() { a }, "bad/name", null)).Message);
        }

        [Fact]
        public void Execute_RejectsModsWithoutSeries()
        {
            var a = _ws.ModDir("emptyA");
            var b = _ws.ModDir("emptyB");

            Assert.Equal("No series folders found in the selected mods.",
                Assert.Throws<DesktopApiException>(() => _service.Execute(new() { a, b }, "merged", null)).Message);
        }
    }
}
