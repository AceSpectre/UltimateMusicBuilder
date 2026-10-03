using System.Collections.Generic;
using System.Text;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Unit
{
    /// <summary>
    /// Pure helpers behind the CLI services: the shared CliUtil statics plus the
    /// internal helpers exposed to this assembly via InternalsVisibleTo
    /// (VolumeConfigService.ParseVolume, TrackOrderService.ParseOrder,
    /// MergeService.AppendSongsField).
    /// </summary>
    public class ServiceHelperTests
    {
        // ── CliUtil.SanitizeFolderName ──────────────────────────────────────

        [Theory]
        [InlineData("My Series", "my-series")]
        [InlineData("  Padded  ", "padded")]
        [InlineData("UPPER", "upper")]
        [InlineData("multi word name", "multi-word-name")]
        public void SanitizeFolderName_LowercasesTrimsAndDashesSpaces(string input, string expected)
        {
            Assert.Equal(expected, CliUtil.SanitizeFolderName(input));
        }

        [Fact]
        public void SanitizeFolderName_ReplacesInvalidPathChars()
        {
            var result = CliUtil.SanitizeFolderName("a/b:c?");
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
                Assert.DoesNotContain(c, result);
            Assert.StartsWith("a_b", result);
        }

        // ── CliUtil.EscapeToml ──────────────────────────────────────────────

        [Theory]
        [InlineData("plain", "plain")]
        [InlineData("say \"hi\"", "say \\\"hi\\\"")]
        [InlineData("back\\slash", "back\\\\slash")]
        [InlineData(null, "")]
        public void EscapeToml_EscapesQuotesAndBackslashes(string input, string expected)
        {
            Assert.Equal(expected, CliUtil.EscapeToml(input));
        }

        // ── CliUtil.ToKebabCase ─────────────────────────────────────────────

        [Theory]
        [InlineData("ExistingSeries", "existing-series")]
        [InlineData("Name", "name")]
        [InlineData("already-kebab", "already-kebab")]
        [InlineData("SeriesPlaylist", "series-playlist")]
        public void ToKebabCase_InsertsDashesBeforeInnerCapitals(string input, string expected)
        {
            Assert.Equal(expected, CliUtil.ToKebabCase(input));
        }

        // ── VolumeConfigService.ParseVolume ─────────────────────────────────

        [Theory]
        [InlineData("1.5", 1.5f)]
        [InlineData("0.75", 0.75f)]
        [InlineData("", 1.0f)]
        [InlineData("   ", 1.0f)]
        [InlineData("not-a-number", 1.0f)]
        [InlineData(null, 1.0f)]
        public void ParseVolume_FallsBackToUnity(string input, float expected)
        {
            Assert.Equal(expected, VolumeConfigService.ParseVolume(input));
        }

        // ── CliUtil.MakeSafeFileName ────────────────────────────────────────

        [Fact]
        public void MakeSafeFileName_ReplacesInvalidChars()
        {
            var result = CliUtil.MakeSafeFileName("a/b\\c");
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
                Assert.DoesNotContain(c, result);
        }

        [Fact]
        public void MakeSafeFileName_LeavesCleanNameUntouched()
        {
            Assert.Equal("clean_name", CliUtil.MakeSafeFileName("clean_name"));
        }

        // ── TrackOrderService.ParseOrder ────────────────────────────────────

        [Fact]
        public void ParseOrder_ReturnsIntWhenPresentAndNumeric()
        {
            var row = new Dictionary<string, string> { ["order"] = "7" };
            Assert.Equal(7, TrackOrderService.ParseOrder(row));
        }

        [Fact]
        public void ParseOrder_NullWhenMissingOrNonNumeric()
        {
            Assert.Null(TrackOrderService.ParseOrder(new Dictionary<string, string>()));
            Assert.Null(TrackOrderService.ParseOrder(new Dictionary<string, string> { ["order"] = "abc" }));
        }

        // ── MergeService.AppendSongsField ───────────────────────────────────

        [Fact]
        public void AppendSongsField_NullOrWildcardWritesStar()
        {
            var sbNull = new StringBuilder();
            MergeService.AppendSongsField(sbNull, null);
            Assert.Contains("songs = \"*\"", sbNull.ToString());

            var sbStar = new StringBuilder();
            MergeService.AppendSongsField(sbStar, "*");
            Assert.Contains("songs = \"*\"", sbStar.ToString());
        }

        [Fact]
        public void AppendSongsField_ExplicitListWritesNames()
        {
            var sb = new StringBuilder();
            var songs = new List<object> { "a.nus3audio", "b.nus3audio" };
            MergeService.AppendSongsField(sb, songs);

            var output = sb.ToString();
            Assert.Contains("a.nus3audio", output);
            Assert.Contains("b.nus3audio", output);
            Assert.DoesNotContain("songs = \"*\"", output);
        }
    }
}
