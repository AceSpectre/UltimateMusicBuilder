using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Sma5h.CLI.Services
{
    public class AcceptNus3Service
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;

        private static readonly HashSet<string> SOURCE_AUDIO_EXTENSIONS = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".wav", ".ogg"
        };

        private const string VALIDATE_FOLDER = "songs-to-validate";

        public AcceptNus3Service(IOptionsMonitor<Sma5hMusicOptions> musicConfig, ILogger<AcceptNus3Service> logger)
        {
            _musicConfig = musicConfig;
            _logger = logger;
        }

        public void Run()
        {
            Script.PrintBanner(_logger);

            var (modDir, seriesDir) = Script.PromptModAndSeries(_musicConfig, _logger);
            if (modDir == null || seriesDir == null)
                return;

            var validateDir = Path.Combine(seriesDir, VALIDATE_FOLDER);
            if (!Directory.Exists(validateDir))
            {
                _logger.LogWarning("No songs-to-validate folder found in {Dir}.", seriesDir);
                return;
            }

            var nus3Files = Directory.GetFiles(validateDir, "*.nus3audio").ToList();
            if (nus3Files.Count == 0)
            {
                _logger.LogWarning("No .nus3audio files found in {Dir}.", validateDir);
                return;
            }

            int accepted = 0;
            int sourcesRemoved = 0;
            int csvUpdated = 0;

            // Read tracks.csv if it exists, for updating filenames
            var csvPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
            List<FolderTrackCsvRow> csvRows = null;
            bool csvExists = File.Exists(csvPath);
            if (csvExists)
            {
                var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    TrimOptions = TrimOptions.Trim,
                    MissingFieldFound = null
                };
                using var reader = new StreamReader(csvPath);
                using var csv = new CsvReader(reader, csvConfig);
                csv.Context.RegisterClassMap<FolderTrackCsvRowMap>();
                csvRows = csv.GetRecords<FolderTrackCsvRow>().ToList();
            }

            foreach (var nus3File in nus3Files)
            {
                var basename = Path.GetFileNameWithoutExtension(nus3File);
                var destFile = Path.Combine(seriesDir, Path.GetFileName(nus3File));

                // Move nus3audio into series folder
                if (File.Exists(destFile))
                    File.Delete(destFile);
                File.Move(nus3File, destFile);
                _logger.LogInformation("Accepted: {Filename}", Path.GetFileName(nus3File));
                accepted++;

                // Delete original source file(s) with matching basename
                foreach (var ext in SOURCE_AUDIO_EXTENSIONS)
                {
                    var sourceFile = Path.Combine(seriesDir, basename + ext);
                    if (File.Exists(sourceFile))
                    {
                        var oldSourceName = basename + ext;
                        File.Delete(sourceFile);
                        _logger.LogInformation("  Removed source: {Filename}", oldSourceName);
                        sourcesRemoved++;

                        // Update tracks.csv if it had a row with the old filename
                        if (csvRows != null)
                        {
                            var matchingRow = csvRows.FirstOrDefault(r =>
                                string.Equals(r.Filename, oldSourceName, StringComparison.OrdinalIgnoreCase));
                            if (matchingRow != null)
                            {
                                matchingRow.Filename = basename + ".nus3audio";
                                csvUpdated++;
                            }
                        }
                    }
                }
            }

            // Rewrite tracks.csv if any rows were updated
            if (csvUpdated > 0 && csvRows != null)
            {
                using var writer = new StreamWriter(csvPath);
                using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true
                });
                csv.Context.RegisterClassMap<FolderTrackCsvRowMap>();
                csv.WriteRecords(csvRows);
                _logger.LogInformation("Updated {Count} filename(s) in tracks.csv.", csvUpdated);
            }

            // Remove validate folder if empty
            if (Directory.GetFiles(validateDir).Length == 0)
            {
                Directory.Delete(validateDir);
                _logger.LogInformation("Removed empty songs-to-validate folder.");
            }

            _logger.LogInformation("--------------------");
            _logger.LogInformation("Accepted {Accepted} file(s), removed {Removed} source file(s).", accepted, sourcesRemoved);
        }
    }
}
