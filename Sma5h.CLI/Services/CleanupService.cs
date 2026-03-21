using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Sma5h.CLI.Services
{
    public class CleanupService
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;

        private const string VALIDATE_FOLDER = "songs-to-validate";

        public CleanupService(IOptionsMonitor<Sma5hMusicOptions> musicConfig, ILogger<CleanupService> logger)
        {
            _musicConfig = musicConfig;
            _logger = logger;
        }

        public void Run()
        {
            Script.PrintBanner(_logger);

            var modPath = _musicConfig.CurrentValue.Sma5hMusic.ModPath;
            Directory.CreateDirectory(modPath);

            var modDirs = Directory.GetDirectories(modPath, "*", SearchOption.TopDirectoryOnly)
                .Where(d => !Path.GetFileName(d).StartsWith("."))
                .ToList();

            if (modDirs.Count == 0)
            {
                _logger.LogWarning("No mod folders found in {ModPath}.", modPath);
                return;
            }

            int totalRemoved = 0;
            int totalFiles = 0;

            foreach (var modDir in modDirs)
            {
                var seriesDirs = Directory.GetDirectories(modDir)
                    .Where(d => !Path.GetFileName(d).StartsWith(".") && Path.GetFileName(d) != VALIDATE_FOLDER)
                    .ToList();

                foreach (var seriesDir in seriesDirs)
                {
                    var csvPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
                    if (!File.Exists(csvPath))
                        continue;

                    totalFiles++;

                    var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        HasHeaderRecord = true,
                        TrimOptions = TrimOptions.Trim,
                        MissingFieldFound = null
                    };
                    List<FolderTrackCsvRow> rows;
                    using (var reader = new StreamReader(csvPath))
                    using (var csv = new CsvReader(reader, csvConfig))
                    {
                        csv.Context.RegisterClassMap<FolderTrackCsvRowMap>();
                        rows = csv.GetRecords<FolderTrackCsvRow>().ToList();
                    }

                    var removedRows = new List<FolderTrackCsvRow>();
                    foreach (var row in rows)
                    {
                        var audioFile = Path.Combine(seriesDir, row.Filename);
                        if (!File.Exists(audioFile))
                            removedRows.Add(row);
                    }

                    if (removedRows.Count == 0)
                        continue;

                    var relativeCsv = Path.Combine(Path.GetFileName(modDir), Path.GetFileName(seriesDir), "tracks.csv");
                    foreach (var row in removedRows)
                    {
                        _logger.LogInformation("Removing '{Filename}' from {CsvPath} (file not found)", row.Filename, relativeCsv);
                        rows.Remove(row);
                        totalRemoved++;
                    }

                    // Rewrite tracks.csv
                    using var writer = new StreamWriter(csvPath);
                    using var csvWriter = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        HasHeaderRecord = true
                    });
                    csvWriter.Context.RegisterClassMap<FolderTrackCsvRowMap>();
                    csvWriter.WriteRecords(rows);
                }
            }

            _logger.LogInformation("--------------------");
            _logger.LogInformation("Cleanup complete: removed {Removed} entries from {Files} tracks.csv file(s).", totalRemoved, totalFiles);
        }
    }
}
