using System;
using System.IO;
using VGAudio.Cli;

namespace Sma5h.Mods.Music.Helpers
{
    /// <summary>
    /// Runs the in-process VGAudio CLI and returns what it printed. VGAudio reports through
    /// the process-wide Console.Out and its progress-bar Timer keeps writing to that writer
    /// after RunConverterCli returns, so:
    ///   - calls are serialised, otherwise parallel callers restore each other's writer;
    ///   - the capture writer is synchronized and never disposed, otherwise a late timer
    ///     write throws ObjectDisposedException and kills the process.
    /// </summary>
    public static class VGAudioRunner
    {
        private static readonly object ConsoleLock = new();

        public static string Run(params string[] args)
        {
            lock (ConsoleLock)
            {
                var captured = new StringWriter();
                var writer = TextWriter.Synchronized(captured);
                var oldOut = Console.Out;
                try
                {
                    Console.SetOut(writer);
                    Converter.RunConverterCli(args);
                }
                finally
                {
                    Console.SetOut(oldOut);
                }
                // The synchronized wrapper locks on itself, so this read can't race a late timer write.
                lock (writer) return captured.ToString();
            }
        }
    }
}
