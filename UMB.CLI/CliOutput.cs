using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Threading;

namespace UMB.CLI
{
    /// <summary>
    /// The CLI's real stdout when it is driven by another process (the desktop app). Log lines and
    /// protocol lines (__DONE__, __LUFS_PROGRESS__) all go through this one writer, in order:
    /// the stock console logger writes from a background queue, so its lines could otherwise
    /// arrive after the __DONE__ that ends a request. The writer is captured once at startup, so
    /// tools that temporarily swap Console.Out (VGAudio) can't swallow these lines.
    /// </summary>
    internal static class CliOutput
    {
        private static readonly object WriteLock = new();
        private static TextWriter _stdout;

        /// <summary>
        /// Switches redirected stdin/stdout to UTF-8 (Windows defaults to the OEM code page, which
        /// mangles non-ASCII paths in daemon requests and song titles in logs) and captures stdout.
        /// Must run before anything else touches Console.
        /// </summary>
        public static void Init()
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            if (Console.IsInputRedirected)
                Console.InputEncoding = utf8;
            if (Console.IsOutputRedirected)
                Console.OutputEncoding = utf8;
            _stdout = Console.Out;
        }

        public static void WriteLine(string line)
        {
            lock (WriteLock)
            {
                // Not initialised when services run outside Program (tests): use the current Console.
                var stdout = _stdout ?? Console.Out;
                stdout.WriteLine(line);
                stdout.Flush();
            }
        }
    }

    /// <summary>
    /// Synchronous single-line console logger used when stdout is redirected. Matches the stock
    /// simple console format ("info: Category[0] message") that the desktop app parses.
    /// </summary>
    internal sealed class RedirectedConsoleLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(categoryName);

        public void Dispose() { }

        private sealed class Logger : ILogger
        {
            private readonly string _category;

            public Logger(string category) => _category = category;

            public IDisposable BeginScope<TState>(TState state) => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                if (!IsEnabled(logLevel))
                    return;

                var message = formatter(state, exception);
                if (exception != null)
                    message += " " + exception;
                message = message.Replace("\r\n", " ").Replace('\n', ' ');

                CliOutput.WriteLine($"{LevelPrefix(logLevel)}: {_category}[{eventId.Id}] {message}");
            }

            private static string LevelPrefix(LogLevel logLevel) => logLevel switch
            {
                LogLevel.Trace => "trce",
                LogLevel.Debug => "dbug",
                LogLevel.Information => "info",
                LogLevel.Warning => "warn",
                LogLevel.Error => "fail",
                _ => "crit"
            };
        }
    }

    /// <summary>
    /// Counts errors logged during an action. Services report failure by logging an error and
    /// returning, so this is what turns a failed action into a non-zero exit/__DONE__ code.
    /// </summary>
    internal sealed class ErrorCountingLoggerProvider : ILoggerProvider
    {
        private static int _errorCount;

        public static int ErrorCount => Volatile.Read(ref _errorCount);

        public static void Reset() => Interlocked.Exchange(ref _errorCount, 0);

        public ILogger CreateLogger(string categoryName) => new Logger();

        public void Dispose() { }

        private sealed class Logger : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error && logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                if (IsEnabled(logLevel))
                    Interlocked.Increment(ref _errorCount);
            }
        }
    }
}
