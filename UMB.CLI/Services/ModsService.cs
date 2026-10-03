using Microsoft.Extensions.Options;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UMB.CLI.Desktop;

namespace UMB.CLI.Services
{
    /// <summary>Lists mod folders and their series for the desktop app.</summary>
    public class ModsService
    {
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;

        public ModsService(IOptionsMonitor<Sma5hMusicOptions> musicConfig)
        {
            _musicConfig = musicConfig;
        }

        public List<ModInfo> ListMods()
        {
            var root = ModPaths.Root(_musicConfig);
            if (!Directory.Exists(root)) return new List<ModInfo>();
            return Directory.GetDirectories(root)
                .Select(dir => new ModInfo(Path.GetFileName(dir), dir))
                .ToList();
        }

        /// <summary>Series folders (those with a tracks.csv); empty for a path outside the mods folder.</summary>
        public List<ModInfo> ListModSeries(string modPath)
        {
            if (string.IsNullOrWhiteSpace(modPath) || !ModPaths.IsUnderMods(_musicConfig, modPath) || !Directory.Exists(modPath))
                return new List<ModInfo>();
            return Directory.GetDirectories(Path.GetFullPath(modPath))
                .Where(dir =>
                {
                    var name = Path.GetFileName(dir);
                    return !name.StartsWith(".") && name != CliUtil.ValidateFolder
                        && File.Exists(Path.Combine(dir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE));
                })
                .Select(dir => new ModInfo(Path.GetFileName(dir), dir))
                .ToList();
        }

        public ModStats GetModStats(string modPath)
        {
            var series = ListModSeries(modPath);
            var trackCount = series.Sum(s => TracksCsv.CountDataRows(Path.Combine(s.Path, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE)));
            return new ModStats(series.Count, trackCount);
        }
    }
}
