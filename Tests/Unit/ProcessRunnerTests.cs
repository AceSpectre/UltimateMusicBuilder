using System.Diagnostics;
using Sma5h.Helpers;
using Xunit;

namespace Tests.Unit
{
    /// <summary>
    /// <see cref="ProcessRunner"/> must not deadlock on chatty tools and must kill hung ones.
    /// Uses cmd.exe as the child process, so these only run on Windows.
    /// </summary>
    public class ProcessRunnerTests
    {
        [Fact]
        public void DrainsBothStreams_WhenToolWritesMoreThanAPipeBuffer()
        {
            if (!OperatingSystem.IsWindows()) return;

            // ~40 KB to each stream: well past the pipe buffer, which used to stall a
            // caller that read one stream to the end before touching the other.
            var result = ProcessRunner.Run("cmd.exe",
                "/c for /L %i in (1,1,3000) do @(echo out %i & echo err %i 1>&2)",
                TimeSpan.FromSeconds(60));

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("out 3000", result.StandardOutput);
            Assert.Contains("err 3000", result.StandardError);
        }

        [Fact]
        public void KillsAndThrows_WhenToolExceedsTimeout()
        {
            if (!OperatingSystem.IsWindows()) return;

            var sw = Stopwatch.StartNew();
            var ex = Assert.Throws<TimeoutException>(() =>
                ProcessRunner.Run("cmd.exe", "/c ping -n 60 127.0.0.1 >nul", TimeSpan.FromSeconds(1)));

            Assert.Contains("cmd.exe", ex.Message);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"Took {sw.Elapsed}; the tool was not stopped.");
        }

        [Fact]
        public void ForwardsLinesToCallbacks()
        {
            if (!OperatingSystem.IsWindows()) return;

            var stdoutLines = new List<string>();
            var stderrLines = new List<string>();
            ProcessRunner.Run("cmd.exe", "/c echo hello & echo oops 1>&2",
                onStdout: (_, e) => { lock (stdoutLines) stdoutLines.Add(e.Data); },
                onStderr: (_, e) => { lock (stderrLines) stderrLines.Add(e.Data); });

            Assert.Contains(stdoutLines, l => l.Trim() == "hello");
            Assert.Contains(stderrLines, l => l.Trim() == "oops");
        }
    }
}
