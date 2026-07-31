using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using UMB.CLI.Services;
using UMB.CLI.Views;
using Xunit;

namespace Tests.Unit
{
    /// <summary>
    /// Pure helpers behind the CLI services: the shared CliUtil statics plus the
    /// internal helpers exposed to this assembly via InternalsVisibleTo
    /// (VolumeConfigService.ParseVolume, TrackOrderService.ParseOrder/ComposeMergedList,
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

        // ── TrackOrderService.ComposeMergedList ─────────────────────────────
        // Built on an uninitialized instance so the service's DI ctor is bypassed;
        // priorities 2-4 never touch the logger or the filesystem.

        private static TrackViewModel Vm(string bgmId) => new TrackViewModel { BgmId = bgmId };

        private static List<TrackViewModel> Compose(
            List<TrackViewModel> vanilla, List<TrackViewModel> mods,
            List<Dictionary<string, string>> rawRows, string[] headers)
        {
            var svc = (TrackOrderService)FormatterServices.GetUninitializedObject(typeof(TrackOrderService));
            var noSuchFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "no-song-order-" + System.Guid.NewGuid().ToString("N") + ".toml");
            return svc.ComposeMergedList(vanilla, mods, rawRows, headers, noSuchFile);
        }

        [Fact]
        public void ComposeMergedList_Priority2_InsertsModsByOrderColumn()
        {
            var vanilla = new List<TrackViewModel> { Vm("v0"), Vm("v1") };
            var mods = new List<TrackViewModel> { Vm("m0"), Vm("m1") };
            var rawRows = new List<Dictionary<string, string>>
            {
                new() { ["order"] = "1" },
                new() { ["order"] = "5" }, // clamps to end
            };

            var merged = Compose(vanilla, mods, rawRows, new[] { "order" });

            Assert.Equal(new[] { "v0", "m0", "v1", "m1" }, merged.ConvertAll(v => v.BgmId));
        }

        [Fact]
        public void ComposeMergedList_Priority3_VanillaThenModsWhenNoOrderColumn()
        {
            var vanilla = new List<TrackViewModel> { Vm("v0") };
            var mods = new List<TrackViewModel> { Vm("m0"), Vm("m1") };

            var merged = Compose(vanilla, mods,
                new List<Dictionary<string, string>> { new(), new() },
                new[] { "filename" });

            Assert.Equal(new[] { "v0", "m0", "m1" }, merged.ConvertAll(v => v.BgmId));
        }

        [Fact]
        public void ComposeMergedList_Fallback_ModOnlySortedByOrder()
        {
            var mods = new List<TrackViewModel> { Vm("m0"), Vm("m1") };
            var rawRows = new List<Dictionary<string, string>>
            {
                new() { ["order"] = "5" },
                new() { ["order"] = "1" },
            };

            var merged = Compose(new List<TrackViewModel>(), mods, rawRows, new[] { "order" });

            Assert.Equal(new[] { "m1", "m0" }, merged.ConvertAll(v => v.BgmId));
        }

        [Fact]
        public void ComposeMergedList_Fallback_ModOnlyVerbatimWhenNoOrder()
        {
            var mods = new List<TrackViewModel> { Vm("m0"), Vm("m1") };

            var merged = Compose(new List<TrackViewModel>(), mods,
                new List<Dictionary<string, string>> { new(), new() },
                new[] { "filename" });

            Assert.Equal(new[] { "m0", "m1" }, merged.ConvertAll(v => v.BgmId));
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
