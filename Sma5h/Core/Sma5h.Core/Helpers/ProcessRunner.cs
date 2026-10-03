using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Sma5h.Helpers
{
    public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    /// <summary>
    /// Runs an external tool to completion, draining both output streams, and kills it if it
    /// exceeds the timeout.
    /// </summary>
    public static class ProcessRunner
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(15);

        public static ProcessResult Run(string fileName, string arguments, TimeSpan? timeout = null,
            DataReceivedEventHandler onStdout = null, DataReceivedEventHandler onStderr = null)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += Collect(stdout, onStdout);
            process.ErrorDataReceived += Collect(stderr, onStderr);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var limit = timeout ?? DefaultTimeout;
            if (!process.WaitForExit((int)limit.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* exited between the timeout and the kill */ }
                throw new TimeoutException($"{Path.GetFileName(fileName)} did not finish within {limit.TotalMinutes:0} minutes and was stopped.");
            }
            // Let the async output readers finish.
            process.WaitForExit();

            return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
        }

        private static DataReceivedEventHandler Collect(StringBuilder buffer, DataReceivedEventHandler forward) =>
            (sender, e) =>
            {
                if (e.Data == null) return;
                buffer.AppendLine(e.Data);
                forward?.Invoke(sender, e);
            };
    }
}
