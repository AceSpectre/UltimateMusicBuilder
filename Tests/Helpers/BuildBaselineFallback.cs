using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;
using Xunit.Abstractions;

namespace Tests.Helpers
{
    /// <summary>
    /// Two-tier baseline assertion for build-pipeline tests.
    ///
    /// Tier 1: <see cref="BaselineComparer"/> against the committed
    /// <c>Tests/TestData/baselines/&lt;scenario&gt;/</c> directory. This is
    /// the preferred path — diffs report per-file mismatches.
    ///
    /// Tier 2 (fallback): if the detailed baseline directory is absent —
    /// e.g. because the PRC/MSBT/BIN files were stripped from the repo for
    /// copyright reasons (they embed partial dumps of Smash Ultimate) — fall
    /// back to a single combined SHA256 of the entire ArcOutput tree,
    /// compared against the committed checksum in
    /// <c>Tests/TestData/baseline-checksums.json</c>. A warning is logged
    /// whenever the fallback is used so the failure granularity drop is
    /// visible in CI output.
    /// </summary>
    public static class BuildBaselineFallback
    {
        public static string ChecksumsFileName => "baseline-checksums.json";

        public static string ChecksumsPath(string repoRoot)
            => Path.Combine(repoRoot, "Tests", "TestData", ChecksumsFileName);

        /// <summary>
        /// Verifies <paramref name="actualOutputDir"/> against the committed
        /// baseline for <paramref name="scenario"/>. Uses the detailed
        /// per-file baseline when present, otherwise the combined-checksum
        /// fallback.
        /// </summary>
        public static void AssertMatches(
            string scenario,
            string actualOutputDir,
            string detailedBaselineDir,
            string repoRoot,
            ITestOutputHelper output)
        {
            if (HasDetailedBaseline(detailedBaselineDir))
            {
                var report = BaselineComparer.Compare(actualOutputDir, detailedBaselineDir);
                report.AssertMatches(output);
                return;
            }

            output?.WriteLine(
                $"WARNING: Detailed baseline {detailedBaselineDir} is missing — falling back to " +
                "combined-checksum comparison. The detailed PRC/MSBT/BIN files embed partial " +
                "Smash Ultimate data and may be intentionally gitignored. Per-file diffs are " +
                "unavailable; expect coarse pass/fail only.");

            var checksumsPath = ChecksumsPath(repoRoot);
            if (!File.Exists(checksumsPath))
            {
                Xunit.Assert.Fail(
                    $"Combined-checksum fallback unavailable: {checksumsPath} does not exist. " +
                    "Regenerate via `UMB_REGEN_BASELINES=1 dotnet test --filter Category=RegenBaselines`.");
            }

            var committed = LoadChecksums(checksumsPath);
            if (!committed.TryGetValue(scenario, out var expected))
            {
                Xunit.Assert.Fail(
                    $"Combined-checksum fallback has no entry for scenario '{scenario}' in " +
                    $"{checksumsPath}. Regenerate the checksums file.");
            }

            var actual = ComputeCombinedChecksum(actualOutputDir);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                Xunit.Assert.Fail(
                    $"Build output combined checksum does not match committed value for '{scenario}'.\n" +
                    $"  expected: {expected}\n" +
                    $"  actual:   {actual}\n" +
                    $"  output:   {actualOutputDir}\n\n" +
                    "To update committed checksums after intentional build changes:\n" +
                    "  UMB_REGEN_BASELINES=1 dotnet test --filter Category=RegenBaselines");
            }

            output?.WriteLine($"Combined checksum match for '{scenario}': {actual[..12]}...");
        }

        public static bool HasDetailedBaseline(string baselineDir)
        {
            if (!Directory.Exists(baselineDir)) return false;
            // Heuristic: a real baseline has at least one .prc or .msbt file.
            // An empty placeholder folder still triggers the fallback.
            foreach (var file in Directory.EnumerateFiles(baselineDir, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                if (string.Equals(ext, ".prc", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ext, ".msbt", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ext, ".bin", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Deterministic combined hash of every file under <paramref name="outputDir"/>.
        /// Walk is sorted by forward-slash relative path (Ordinal) so different
        /// filesystems produce identical output. Each entry contributes the
        /// path bytes (UTF-8) followed by the file content.
        /// </summary>
        public static string ComputeCombinedChecksum(string outputDir)
        {
            if (!Directory.Exists(outputDir))
                return string.Empty;

            var entries = Directory.EnumerateFiles(outputDir, "*", SearchOption.AllDirectories)
                .Select(f => (
                    Rel: Path.GetRelativePath(outputDir, f).Replace('\\', '/'),
                    Full: f))
                .OrderBy(e => e.Rel, StringComparer.Ordinal)
                .ToList();

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var (rel, full) in entries)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(rel));
                hash.AppendData(new byte[] { 0 });
                hash.AppendData(File.ReadAllBytes(full));
            }
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }

        public static Dictionary<string, string> LoadChecksums(string checksumsPath)
        {
            if (!File.Exists(checksumsPath))
                return new Dictionary<string, string>(StringComparer.Ordinal);
            var json = File.ReadAllText(checksumsPath);
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        public static void SaveChecksums(string checksumsPath, Dictionary<string, string> map)
        {
            var dir = Path.GetDirectoryName(checksumsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonConvert.SerializeObject(map, Formatting.Indented);
            File.WriteAllText(checksumsPath, json);
        }
    }
}
