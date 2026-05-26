using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace Tests.Helpers
{
    /// <summary>
    /// Runs once when the test assembly loads. Verifies the developer has the
    /// vanilla game resources and external tools required for the suite to run
    /// against real PRC/MSBT files. If anything is missing, throws with a clear
    /// actionable message so the whole suite fails fast instead of producing
    /// confusing per-test errors.
    ///
    /// Suite WILL NOT run without:
    ///   - Resources/Game/  (user-extracted vanilla PRC/MSBT — not redistributable)
    ///   - tools/           (Nus3Audio, BgmProperty, VGAudioCli, UltimateTexCli,
    ///                       vgmstream-cli, paracobNET.dll)
    ///   - Resources/ParamLabels.csv, nusbank_ids.csv
    ///   - Tests/TestData/configured-mod/{dev,mario}/*.flac (the real audio bundled
    ///     in the repo's test-data folder)
    /// </summary>
    public static class SuiteEnvironmentCheck
    {
        public static string RepoRoot { get; private set; }

        [ModuleInitializer]
        public static void Initialize()
        {
            try
            {
                Verify();
            }
            catch (Exception ex)
            {
                // ModuleInitializer exceptions can be swallowed depending on runtime.
                // Cache on a static so the first test that runs surfaces it loudly.
                FailureMessage = ex.Message;
            }
        }

        public static string FailureMessage { get; private set; }

        /// <summary>
        /// Throws <see cref="InvalidOperationException"/> listing every missing
        /// resource. Called from <see cref="TestEnvironment"/> ctor so tests
        /// see the failure even if ModuleInitializer ran silently.
        /// </summary>
        public static void Verify()
        {
            RepoRoot = FindRepoRoot();
            if (RepoRoot == null)
            {
                throw new InvalidOperationException(
                    "Test suite cannot locate repo root. Could not find Sma5h.sln by " +
                    "walking up from " + AppContext.BaseDirectory + ". " +
                    "Run tests from inside the UltimateMusicBuilder working tree.");
            }

            var missing = new List<string>();

            // ── Resources/Game (user-extracted vanilla data) ───────────────
            string game = Path.Combine(RepoRoot, "Resources", "Game");
            RequireFile(missing, Path.Combine(game, "ui", "param", "database", "ui_bgm_db.prc"));
            RequireFile(missing, Path.Combine(game, "ui", "param", "database", "ui_gametitle_db.prc"));
            RequireFile(missing, Path.Combine(game, "ui", "param", "database", "ui_stage_db.prc"));
            RequireFile(missing, Path.Combine(game, "ui", "message", "msg_bgm+us_en.msbt"));
            RequireDir(missing, Path.Combine(game, "sound"));
            RequireDir(missing, Path.Combine(game, "stream"));

            // ── Resources (checked into repo) ──────────────────────────────
            RequireFile(missing, Path.Combine(RepoRoot, "Resources", "ParamLabels.csv"));
            RequireFile(missing, Path.Combine(RepoRoot, "Resources", "nusbank_ids.csv"));
            RequireFile(missing, Path.Combine(RepoRoot, "Resources", "template.nus3bank"));

            // ── External tools ─────────────────────────────────────────────
            string tools = Path.Combine(RepoRoot, "tools");
            RequireFile(missing, Path.Combine(tools, "Nus3Audio", "nus3audio.exe"));
            RequireFile(missing, Path.Combine(tools, "BgmProperty", "bgm-property.exe"));
            RequireFile(missing, Path.Combine(tools, "BgmProperty", "bgm_hashes.txt"));
            RequireFile(missing, Path.Combine(tools, "VGAudioCli.exe"));
            RequireFile(missing, Path.Combine(tools, "UltimateTexCli", "ultimate_tex_cli.exe"));
            RequireFile(missing, Path.Combine(tools, "paracobNET.dll"));
            RequireDir(missing, Path.Combine(tools, "vgmstream-cli"));

            // ── Bundled test audio (real FLAC files) ───────────────────────
            string testAudio = Path.Combine(AppContext.BaseDirectory, "TestData", "configured-mod");
            RequireNonEmpty(missing,
                Path.Combine(testAudio, "dev", TestEnvironment.DevTrackFiles[0]));
            RequireNonEmpty(missing,
                Path.Combine(testAudio, "mario", TestEnvironment.MarioTrackFiles[0]));

            if (missing.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("UltimateMusicBuilder test suite cannot run — environment is incomplete.");
                sb.AppendLine();
                sb.AppendLine($"Repo root: {RepoRoot}");
                sb.AppendLine();
                sb.AppendLine($"Missing {missing.Count} required item(s):");
                foreach (var m in missing)
                    sb.AppendLine($"  - {m}");
                sb.AppendLine();
                sb.AppendLine("Setup checklist:");
                sb.AppendLine("  1. Extract vanilla Smash Ultimate PRC/MSBT files into Resources/Game/");
                sb.AppendLine("     (see Resources/Game/README.txt). These files are NOT redistributable.");
                sb.AppendLine("  2. Confirm tools/ contains Nus3Audio, BgmProperty, VGAudioCli,");
                sb.AppendLine("     UltimateTexCli, vgmstream-cli, paracobNET.dll.");
                sb.AppendLine("  3. Confirm Tests/TestData/configured-mod/{dev,mario}/ contain the real");
                sb.AppendLine("     FLAC files (the build copies them to bin/Debug/net8.0/TestData/).");
                throw new InvalidOperationException(sb.ToString());
            }
        }

        /// <summary>Re-throws the cached ModuleInitializer failure, if any.</summary>
        public static void ThrowIfInitFailed()
        {
            if (FailureMessage != null)
                throw new InvalidOperationException(FailureMessage);
        }

        private static void RequireFile(List<string> missing, string path)
        {
            if (!File.Exists(path)) missing.Add($"FILE missing: {path}");
        }

        private static void RequireDir(List<string> missing, string path)
        {
            if (!Directory.Exists(path)) missing.Add($"DIR  missing: {path}");
        }

        private static void RequireNonEmpty(List<string> missing, string path)
        {
            if (!File.Exists(path)) { missing.Add($"FILE missing: {path}"); return; }
            if (new FileInfo(path).Length == 0)
                missing.Add($"FILE empty (expected real audio): {path}");
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Sma5h.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
