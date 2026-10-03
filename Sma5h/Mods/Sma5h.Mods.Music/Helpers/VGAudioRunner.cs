using System;
using System.IO;
using VGAudio.Cli;

namespace Sma5h.Mods.Music.Helpers
{
    /// <summary>
    /// Runs VGAudio in-process and returns its console output. VGAudio writes to the global
    /// Console.Out, including from a timer after returning, so calls are serialised and the
    /// capture writer is never disposed.
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
