using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Sma5h.Helpers
{
    public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    /// <summary>
    /// Runs an external tool to completion. Both output streams are drained asynchronously so a
    /// chatty tool can't fill an unread pipe and stall, and a hung tool is killed after a timeout
    /// instead of blocking the CLI (and the desktop's daemon queue) forever.
    /// </summary>
    public static class ProcessRunner
    {
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(15);

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
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data == null) return;
                lock (stdout) stdout.AppendLine(e.Data);
                onStdout?.Invoke(sender, e);
            };
            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data == null) return;
                lock (stderr) stderr.AppendLine(e.Data);
                onStderr?.Invoke(sender, e);
            };

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
            // The timed wait can return before the async readers have flushed; this one waits for them.
            process.WaitForExit();

            lock (stdout) lock (stderr)
                return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
        }
    }
}
