using System;
using System.IO;

namespace Sma5h.Helpers
{
    public static class ToolPathResolver
    {
        // Resolves an external tool binary path for cross-platform invocation.
        //
        // Lookup order:
        //   1. {toolsPath}/{relativeName}{.exe on Windows}  -> returns absolute path
        //   2. {toolsPath}/{relativeName} (no suffix)       -> returns absolute path
        //   3. {pathFallback} on the system PATH            -> returns absolute path
        //   4. null                                          -> caller should warn
        //
        // relativeName must NOT include an OS-specific extension. Legacy callers that
        // pass ".exe" still work — the suffix is stripped before resolution.
        public static string Resolve(string toolsPath, string relativeName, string pathFallback = null)
        {
            if (string.IsNullOrEmpty(relativeName))
                return string.IsNullOrEmpty(pathFallback) ? null : FindOnPath(pathFallback);

            // Legacy/absolute path support: if the configured value resolves as-is (with or
            // without a platform-appropriate .exe), honor it. Lets users keep older config
            // values like "Tools/FFmpeg/ffmpeg.exe" working without edits.
            var asGiven = TryLocal(relativeName);
            if (asGiven != null)
                return asGiven;

            if (relativeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                relativeName = relativeName.Substring(0, relativeName.Length - 4);

            var underTools = TryLocal(Path.Combine(toolsPath ?? string.Empty, relativeName));
            if (underTools != null)
                return underTools;

            return string.IsNullOrEmpty(pathFallback) ? null : FindOnPath(pathFallback);
        }

        private static string TryLocal(string baseLocal)
        {
            if (File.Exists(baseLocal))
                return Path.GetFullPath(baseLocal);
            if (OperatingSystem.IsWindows())
            {
                if (!baseLocal.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var withExe = baseLocal + ".exe";
                    if (File.Exists(withExe))
                        return Path.GetFullPath(withExe);
                }
            }
            else
            {
                if (baseLocal.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var stripped = baseLocal.Substring(0, baseLocal.Length - 4);
                    if (File.Exists(stripped))
                        return Path.GetFullPath(stripped);
                }
            }
            return null;
        }

        private static string FindOnPath(string command)
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv))
                return null;

            var withExt = OperatingSystem.IsWindows()
                          && !command.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? command + ".exe"
                : command;

            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;
                try
                {
                    var candidate = Path.Combine(dir, withExt);
                    if (File.Exists(candidate))
                        return candidate;

                    if (OperatingSystem.IsWindows() && withExt != command)
                    {
                        candidate = Path.Combine(dir, command);
                        if (File.Exists(candidate))
                            return candidate;
                    }
                }
                catch
                {
                }
            }
            return null;
        }
    }
}
