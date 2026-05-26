using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;
using Xunit.Abstractions;

namespace Tests.Helpers
{
    /// <summary>
    /// Compares a live ArcOutput directory against a committed baseline.
    /// PRC/MSBT/BIN files are SHA256-checked byte-exact. Nus3audio/nus3bank
    /// files are checked against a JSON manifest of {relPath: size} — keeping
    /// the test data small while still catching missing/extra/resized output.
    /// </summary>
    public static class BaselineComparer
    {
        public const string ManifestFileName = "nus3-manifest.json";

        private static readonly HashSet<string> HashedExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".prc", ".msbt", ".bin" };

        private static readonly HashSet<string> ManifestExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".nus3audio", ".nus3bank" };

        public static BaselineReport Compare(string actualOutputDir, string baselineDir)
        {
            var report = new BaselineReport(actualOutputDir, baselineDir);

            if (!Directory.Exists(baselineDir))
            {
                report.SetupError = $"Baseline directory does not exist: {baselineDir}. " +
                    "Run `dotnet test --filter Category=RegenBaselines` to generate it.";
                return report;
            }

            // ── PRC/MSBT/BIN: hash compare ─────────────────────────────────
            var actualHashes = ComputeHashes(actualOutputDir);
            var baselineHashes = ComputeHashes(baselineDir);

            foreach (var (relPath, hash) in baselineHashes)
            {
                if (!actualHashes.TryGetValue(relPath, out var actual))
                    report.MissingHashed.Add(relPath);
                else if (actual != hash)
                    report.MismatchedHashed.Add((relPath, hash, actual));
            }
            foreach (var relPath in actualHashes.Keys)
            {
                if (!baselineHashes.ContainsKey(relPath))
                    report.ExtraHashed.Add(relPath);
            }

            // ── Nus3audio/Nus3bank: manifest compare ───────────────────────
            var manifestPath = Path.Combine(baselineDir, ManifestFileName);
            var actualManifest = BuildManifest(actualOutputDir);

            if (File.Exists(manifestPath))
            {
                var baselineManifest = LoadManifest(manifestPath);
                foreach (var (relPath, expected) in baselineManifest)
                {
                    if (!actualManifest.TryGetValue(relPath, out var actual))
                        report.MissingNus3.Add(relPath);
                    else if (actual.Size != expected.Size)
                        report.MismatchedNus3.Add((relPath, expected.Size, actual.Size));
                }
                foreach (var relPath in actualManifest.Keys)
                {
                    if (!baselineManifest.ContainsKey(relPath))
                        report.ExtraNus3.Add(relPath);
                }
            }
            else if (actualManifest.Count > 0)
            {
                report.SetupError = (report.SetupError == null ? "" : report.SetupError + " ")
                    + $"Baseline manifest missing at {manifestPath} but live output produced "
                    + $"{actualManifest.Count} nus3 file(s).";
            }

            return report;
        }

        public static void WriteHashedAndManifest(string sourceOutputDir, string baselineDir)
        {
            Directory.CreateDirectory(baselineDir);

            // Copy hashed files verbatim
            foreach (var file in EnumerateRelevant(sourceOutputDir, HashedExtensions))
            {
                var rel = ToRelative(sourceOutputDir, file);
                var dest = Path.Combine(baselineDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite: true);
            }

            // Write manifest of nus3 sizes
            var manifest = BuildManifest(sourceOutputDir);
            var manifestPath = Path.Combine(baselineDir, ManifestFileName);
            File.WriteAllText(manifestPath,
                JsonConvert.SerializeObject(manifest, Formatting.Indented));
        }

        public static Dictionary<string, ManifestEntry> BuildManifest(string outputDir)
        {
            var result = new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(outputDir)) return result;

            foreach (var file in EnumerateRelevant(outputDir, ManifestExtensions))
            {
                var rel = ToRelative(outputDir, file);
                result[rel] = new ManifestEntry { Size = new FileInfo(file).Length };
            }
            return result;
        }

        private static Dictionary<string, string> ComputeHashes(string root)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(root)) return result;

            foreach (var file in EnumerateRelevant(root, HashedExtensions))
            {
                var rel = ToRelative(root, file);
                if (string.Equals(Path.GetFileName(file), ManifestFileName,
                    StringComparison.OrdinalIgnoreCase))
                    continue;
                using var stream = File.OpenRead(file);
                var hash = SHA256.HashData(stream);
                result[rel] = Convert.ToHexString(hash).ToLowerInvariant();
            }
            return result;
        }

        private static Dictionary<string, ManifestEntry> LoadManifest(string manifestPath)
        {
            var json = File.ReadAllText(manifestPath);
            return JsonConvert.DeserializeObject<Dictionary<string, ManifestEntry>>(json)
                ?? new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> EnumerateRelevant(string root, HashSet<string> extensions)
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (extensions.Contains(Path.GetExtension(file)))
                    yield return file;
            }
        }

        private static string ToRelative(string root, string file)
            => Path.GetRelativePath(root, file).Replace('\\', '/');

        public class ManifestEntry
        {
            public long Size { get; set; }
        }
    }

    public class BaselineReport
    {
        public string ActualDir { get; }
        public string BaselineDir { get; }
        public string SetupError { get; set; }

        public List<string> MissingHashed { get; } = new();
        public List<(string Path, string Expected, string Actual)> MismatchedHashed { get; } = new();
        public List<string> ExtraHashed { get; } = new();

        public List<string> MissingNus3 { get; } = new();
        public List<(string Path, long Expected, long Actual)> MismatchedNus3 { get; } = new();
        public List<string> ExtraNus3 { get; } = new();

        public BaselineReport(string actualDir, string baselineDir)
        {
            ActualDir = actualDir;
            BaselineDir = baselineDir;
        }

        public bool IsClean => SetupError == null
            && MissingHashed.Count == 0 && MismatchedHashed.Count == 0 && ExtraHashed.Count == 0
            && MissingNus3.Count == 0 && MismatchedNus3.Count == 0 && ExtraNus3.Count == 0;

        public void AssertMatches(ITestOutputHelper output = null)
        {
            if (IsClean)
            {
                output?.WriteLine($"Baseline match: {BaselineDir}");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Build output does not match baseline {BaselineDir}.");
            sb.AppendLine($"Actual output: {ActualDir}");
            if (SetupError != null)
            {
                sb.AppendLine();
                sb.AppendLine("SETUP:");
                sb.AppendLine("  " + SetupError);
            }
            if (MismatchedHashed.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"HASHED MISMATCH ({MismatchedHashed.Count}):");
                foreach (var (p, e, a) in MismatchedHashed)
                    sb.AppendLine($"  {p}: expected {e[..12]}... got {a[..12]}...");
            }
            if (MissingHashed.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"HASHED MISSING ({MissingHashed.Count}):");
                foreach (var p in MissingHashed) sb.AppendLine($"  {p}");
            }
            if (ExtraHashed.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"HASHED EXTRA ({ExtraHashed.Count}):");
                foreach (var p in ExtraHashed) sb.AppendLine($"  {p}");
            }
            if (MismatchedNus3.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"NUS3 SIZE MISMATCH ({MismatchedNus3.Count}):");
                foreach (var (p, e, a) in MismatchedNus3)
                    sb.AppendLine($"  {p}: expected {e} bytes, got {a}");
            }
            if (MissingNus3.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"NUS3 MISSING ({MissingNus3.Count}):");
                foreach (var p in MissingNus3) sb.AppendLine($"  {p}");
            }
            if (ExtraNus3.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"NUS3 EXTRA ({ExtraNus3.Count}):");
                foreach (var p in ExtraNus3) sb.AppendLine($"  {p}");
            }
            sb.AppendLine();
            sb.AppendLine("To regenerate baselines after intentional changes:");
            sb.AppendLine("  dotnet test --filter Category=RegenBaselines");

            Xunit.Assert.Fail(sb.ToString());
        }
    }
}
