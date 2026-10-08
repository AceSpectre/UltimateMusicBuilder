using Microsoft.Extensions.Options;
using Sma5h.Mods.Music;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UMB.CLI.Desktop;

namespace UMB.CLI.Services
{
    public static class ModPaths
    {
        public static string Root(IOptionsMonitor<Sma5hMusicOptions> config) =>
            CanonicalDirectoryPath(config.CurrentValue.Sma5hMusic.ModPath);

        /// <summary>The subfolders of <paramref name="path"/> (mods or series), skipping dot folders.</summary>
        public static List<string> VisibleDirs(string path) => Directory.Exists(path)
            ? Directory.GetDirectories(path).Where(d => !Path.GetFileName(d).StartsWith(".")).ToList()
            : new();

        public static bool IsUnderMods(IOptionsMonitor<Sma5hMusicOptions> config, string path)
        {
            var relative = Path.GetRelativePath(Root(config), CanonicalDirectoryPath(path));
            return relative != "." && !relative.StartsWith("..") && !Path.IsPathRooted(relative);
        }

        /// <summary>Resolves a caller-supplied path, rejecting anything outside the mods folder.</summary>
        public static string ResolveUnderMods(IOptionsMonitor<Sma5hMusicOptions> config, string path, string message = "Invalid mod path.")
        {
            if (string.IsNullOrWhiteSpace(path) || !IsUnderMods(config, path))
                throw new DesktopApiException(message);
            return CanonicalDirectoryPath(path);
        }

        private static string CanonicalDirectoryPath(string path)
        {
            var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var parent = Path.GetDirectoryName(fullPath);
            if (parent == null) return fullPath;

            var directory = new DirectoryInfo(Path.Combine(CanonicalDirectoryPath(parent), Path.GetFileName(fullPath)));
            return directory.LinkTarget == null
                ? directory.FullName
                : CanonicalDirectoryPath(directory.ResolveLinkTarget(returnFinalTarget: true).FullName);
        }
    }
}
