using CsvHelper;
using Microsoft.Extensions.Options;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UMB.CLI.Desktop;

namespace UMB.CLI.Services
{
    /// <summary>
    /// Finds tracks.csv rows whose <c>volume</c> looks like a legacy nus3bank dB value rather than a
    /// gain multiplier. Mods imported with older versions of Convert copied the legacy dB value
    /// (usually 2.7) straight into the column, which is now read as a 2.7x (+8.6 dB) boost.
    /// </summary>
    public class VolumeCheckService
    {
        /// <summary>Multipliers at or above this are rarely intentional (2x is about +6 dB).</summary>
        public const float SuspiciousVolume = 2.0f;

        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;

        public VolumeCheckService(IOptionsMonitor<Sma5hMusicOptions> musicConfig)
        {
            _musicConfig = musicConfig;
        }

        /// <summary>Checks the named mod, or every mod when <paramref name="modName"/> is empty (as Build does).</summary>
        public List<SuspiciousVolumeTrack> Check(string modName)
        {
            var root = ModPaths.Root(_musicConfig);
            if (!Directory.Exists(root)) return new List<SuspiciousVolumeTrack>();
            var modDirs = Directory.GetDirectories(root)
                .Where(d => !Path.GetFileName(d).StartsWith("."))
                .Where(d => string.IsNullOrWhiteSpace(modName)
                    || Path.GetFileName(d).Equals(modName, StringComparison.OrdinalIgnoreCase));
            return Check(modDirs);
        }

        public List<SuspiciousVolumeTrack> Check(IEnumerable<string> modDirs, Dictionary<string, HashSet<string>> seriesFilters = null)
        {
            var result = new List<SuspiciousVolumeTrack>();
            foreach (var modDir in modDirs)
            {
                var seriesDirs = Directory.GetDirectories(modDir)
                    .Where(d => !Path.GetFileName(d).StartsWith("."))
                    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (seriesFilters != null && seriesFilters.TryGetValue(modDir, out var filter))
                    seriesDirs = seriesDirs.Where(d => filter.Contains(Path.GetFileName(d))).ToList();

                foreach (var seriesDir in seriesDirs)
                {
                    var csvPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
                    if (!File.Exists(csvPath)) continue;

                    using var reader = new StreamReader(csvPath);
                    using var csv = new CsvReader(reader, CliUtil.CsvReadLenient());
                    csv.Read();
                    csv.ReadHeader();
                    if (!csv.HeaderRecord.Contains("volume")) continue;

                    while (csv.Read())
                    {
                        var filename = csv.GetField("filename")?.Trim() ?? "";
                        if (string.IsNullOrWhiteSpace(filename)) continue;
                        var volume = VolumeConfigService.ParseVolume(csv.GetField("volume"));
                        if (volume >= SuspiciousVolume)
                            result.Add(new SuspiciousVolumeTrack(Path.GetFileName(modDir), Path.GetFileName(seriesDir),
                                filename, csv.GetField("title")?.Trim() ?? "", volume));
                    }
                }
            }
            return result;
        }
    }
}
