using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Integration
{
    /// <summary>
    /// Spawns the real built UMB.CLI.exe in <c>serve</c> (daemon) mode and drives it
    /// over stdin/stdout. This is the only way to exercise the static bootstrap in
    /// Program.cs — UMB_WORKSPACE resolution, the VGAudioCli assembly-load resolver,
    /// ConfigureServices, the RunDaemon read-loop (newline-delimited JSON, __DONE__
    /// sentinel, __shutdown__) and RunAction's dispatch + unknown-command branch.
    /// Uses config-volume-save because it is a pure-CSV action needing no game
    /// resources or external encoders.
    /// </summary>
    [Collection("CwdSensitive")]
    public class DaemonProcessTests : IDisposable
    {
        private readonly TestEnvironment _env;
        private readonly ITestOutputHelper _output;

        public DaemonProcessTests(ITestOutputHelper output)
        {
            _env = new TestEnvironment();
            _output = output;
        }

        public void Dispose() => _env.Dispose();

        private static string CliExePath() => Path.Combine(
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "UMB.CLI.exe" : "UMB.CLI");

        private static string Request(int id, string action, string arg) =>
            JsonSerializer.Serialize(new
            {
                id,
                action,
                args = arg == null ? null : new[] { arg }
            });

        /// <summary>
        /// Starts the daemon with the given workspace, sends every request line,
        /// then shuts it down and returns the combined stdout.
        /// </summary>
        private string RunDaemonSession(string workspace, IEnumerable<string> requestLines)
        {
            var exe = CliExePath();
            Assert.True(File.Exists(exe), $"Built CLI not found at {exe}");

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "serve",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workspace,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
            };
            psi.Environment["UMB_WORKSPACE"] = workspace;

            using var proc = Process.Start(psi);

            var stdout = new StringBuilder();
            var outTask = Task.Run(() =>
            {
                string line;
                while ((line = proc.StandardOutput.ReadLine()) != null)
                    lock (stdout) { stdout.AppendLine(line); }
            });
            var errTask = Task.Run(() => proc.StandardError.ReadToEnd());

            foreach (var line in requestLines)
            {
                proc.StandardInput.WriteLine(line);
                proc.StandardInput.Flush();
            }
            proc.StandardInput.WriteLine(JsonSerializer.Serialize(new { action = "__shutdown__" }));
            proc.StandardInput.Flush();
            proc.StandardInput.Close();

            if (!proc.WaitForExit(60000))
            {
                proc.Kill(entireProcessTree: true);
                throw new TimeoutException("Daemon did not exit within 60s.");
            }
            outTask.Wait(5000);
            var err = errTask.Result;
            if (!string.IsNullOrWhiteSpace(err))
                _output.WriteLine("STDERR:\n" + err);

            lock (stdout)
            {
                _output.WriteLine("STDOUT:\n" + stdout);
                return stdout.ToString();
            }
        }

        [Fact]
        public void Daemon_ProcessesBatchRequest_AndReportsDone()
        {
            var workspace = Path.Combine(_env.TempDir, "ws");
            Directory.CreateDirectory(Path.Combine(workspace, "Resources"));

            var seriesDir = Path.Combine(workspace, "series");
            Directory.CreateDirectory(seriesDir);
            File.WriteAllText(Path.Combine(seriesDir, "tracks.csv"),
                "filename,title,volume\na.nus3audio,a,1.0\n");

            var inputJson = Path.Combine(_env.TempDir, "save.json");
            File.WriteAllText(inputJson, JsonSerializer.Serialize(new
            {
                seriesPath = seriesDir,
                overrides = new[] { new { originalIndex = 0, volume = 1.75f } }
            }));

            var stdout = RunDaemonSession(workspace, new[]
            {
                Request(1, "config-volume-save", inputJson),
                Request(2, "bogus-command", null),
            });

            // RunDaemon prints a per-request sentinel once each action completes; an unknown
            // command is a usage error (2).
            Assert.Contains("__DONE__\t1\t0", stdout);
            Assert.Contains("__DONE__\t2\t2", stdout);
            // RunAction's default branch handled the unknown command.
            Assert.Contains("Unknown command: bogus-command", stdout);
            // The save action actually mutated the CSV via VolumeConfigService.RunSaveBatch.
            var csv = File.ReadAllText(Path.Combine(seriesDir, "tracks.csv"));
            Assert.Contains("a.nus3audio,a,1.75", csv);
        }

        [Fact]
        public void Daemon_HandlesNonAsciiPaths_InRequestsAndLogs()
        {
            // Windows defaults redirected stdio to the OEM code page; a request path with a
            // non-ASCII character used to arrive corrupted, so the action silently failed.
            var workspace = Path.Combine(_env.TempDir, "ws");
            Directory.CreateDirectory(Path.Combine(workspace, "Resources"));

            var seriesDir = Path.Combine(_env.TempDir, "José", "Pokémon→série");
            Directory.CreateDirectory(seriesDir);
            File.WriteAllText(Path.Combine(seriesDir, "tracks.csv"),
                "filename,title,volume\na.nus3audio,a,1.0\n");

            var inputJson = Path.Combine(_env.TempDir, "José", "save.json");
            File.WriteAllText(inputJson, JsonSerializer.Serialize(new
            {
                seriesPath = seriesDir,
                overrides = new[] { new { originalIndex = 0, volume = 1.5f } }
            }));

            var stdout = RunDaemonSession(workspace, new[] { Request(1, "config-volume-save", inputJson) });

            Assert.Contains("__DONE__\t1\t0", stdout);
            Assert.Contains("a.nus3audio,a,1.5", File.ReadAllText(Path.Combine(seriesDir, "tracks.csv")));
            Assert.Contains("Pokémon→série", stdout);
        }

        [Fact]
        public void Daemon_ReportsFailedAction_WithCode1_AfterItsLogLines()
        {
            var workspace = Path.Combine(_env.TempDir, "ws");
            Directory.CreateDirectory(Path.Combine(workspace, "Resources"));
            var missingInput = Path.Combine(_env.TempDir, "missing.json");

            var stdout = RunDaemonSession(workspace, new[] { Request(1, "config-volume-analyze", missingInput) });

            Assert.Contains("__DONE__\t1\t1", stdout);
            // The error is written synchronously, so it precedes the request's __DONE__.
            var errorAt = stdout.IndexOf("fail:", StringComparison.Ordinal);
            Assert.InRange(errorAt, 0, stdout.IndexOf("__DONE__\t1\t1", StringComparison.Ordinal));
        }

        [Fact]
        public void Daemon_AnswersMalformedRequest_AndKeepsServing()
        {
            var workspace = Path.Combine(_env.TempDir, "ws");
            Directory.CreateDirectory(Path.Combine(workspace, "Resources"));

            var stdout = RunDaemonSession(workspace, new[]
            {
                "this is not json",
                Request(2, "bogus-command", null),
            });

            Assert.Contains("__DONE__\t0\t2", stdout);
            Assert.Contains("__DONE__\t2\t2", stdout);
        }

        [Theory]
        [InlineData("bogus-command", 2)]
        [InlineData("config-volume-analyze", 1)] // no input file → logged error
        public void OneShot_ExitCodeReflectsOutcome(string action, int expectedCode)
        {
            var workspace = Path.Combine(_env.TempDir, "ws");
            Directory.CreateDirectory(Path.Combine(workspace, "Resources"));

            var psi = new ProcessStartInfo
            {
                FileName = CliExePath(),
                Arguments = action,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workspace,
            };
            psi.Environment["UMB_WORKSPACE"] = workspace;

            using var proc = Process.Start(psi);
            proc.StandardInput.Close();
            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();
            Assert.True(proc.WaitForExit(60000), "CLI did not exit within 60s.");
            _output.WriteLine(outTask.Result + errTask.Result);

            Assert.Equal(expectedCode, proc.ExitCode);
        }

        [Fact]
        public void Daemon_ShutdownWithNoRequests_ExitsCleanly()
        {
            var workspace = Path.Combine(_env.TempDir, "ws-empty");
            Directory.CreateDirectory(Path.Combine(workspace, "Resources"));

            var ex = Record.Exception(() =>
                RunDaemonSession(workspace, Array.Empty<string>()));

            Assert.Null(ex);
        }
    }
}
