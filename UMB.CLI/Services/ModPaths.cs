using Microsoft.Extensions.Options;
using Sma5h.Mods.Music;
using System.IO;
using UMB.CLI.Desktop;

namespace UMB.CLI.Services
{
    public static class ModPaths
    {
        public static string Root(IOptionsMonitor<Sma5hMusicOptions> config) =>
            Path.GetFullPath(config.CurrentValue.Sma5hMusic.ModPath);

        public static bool IsUnderMods(IOptionsMonitor<Sma5hMusicOptions> config, string path)
        {
            var relative = Path.GetRelativePath(Root(config), Path.GetFullPath(path));
            return relative != "." && !relative.StartsWith("..") && !Path.IsPathRooted(relative);
        }

        /// <summary>Resolves a caller-supplied path, rejecting anything outside the mods folder.</summary>
        public static string ResolveUnderMods(IOptionsMonitor<Sma5hMusicOptions> config, string path, string message = "Invalid mod path.")
        {
            if (string.IsNullOrWhiteSpace(path) || !IsUnderMods(config, path))
                throw new DesktopApiException(message);
            return Path.GetFullPath(path);
        }
    }
}
