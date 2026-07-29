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

            // RunDaemon prints a per-request sentinel once each action completes.
            Assert.Contains("__DONE__\t1\t0", stdout);
            Assert.Contains("__DONE__\t2\t0", stdout);
            // RunAction's default branch handled the unknown command.
            Assert.Contains("Unknown command: bogus-command", stdout);
            // The save action actually mutated the CSV via VolumeConfigService.RunSaveBatch.
            var csv = File.ReadAllText(Path.Combine(seriesDir, "tracks.csv"));
            Assert.Contains("a.nus3audio,a,1.75", csv);
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
