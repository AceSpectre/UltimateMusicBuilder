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
using System.Text;
using Tomlyn;

namespace Sma5h.CLI.Services
{
    public class ScaffoldService
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;

        public ScaffoldService(IOptionsMonitor<Sma5hMusicOptions> musicConfig, ILogger<ScaffoldService> logger)
        {
            _musicConfig = musicConfig;
            _logger = logger;
        }

        public void Run()
        {
            Script.PrintBanner(_logger);

            var modPath = _musicConfig.CurrentValue.Sma5hMusic.ModPath;
            Directory.CreateDirectory(modPath);

            var modDirs = Directory.GetDirectories(modPath, "*", SearchOption.TopDirectoryOnly);
            if (modDirs.Length == 0)
            {
                _logger.LogWarning("No mod folders found in {ModPath}. Create a mod folder first.", modPath);
                return;
            }

            var validExtensions = new HashSet<string>(MusicConstants.VALID_MUSIC_EXTENSIONS, StringComparer.OrdinalIgnoreCase);
            int totalScaffolded = 0;
            int totalAdded = 0;

            foreach (var modDir in modDirs)
            {
                if (Path.GetFileName(modDir).StartsWith("."))
                    continue;

                foreach (var seriesDir in Directory.GetDirectories(modDir))
                {
                    var folderName = Path.GetFileName(seriesDir);
                    var tomlPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
                    var csvPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);

                    // ── Step 1: Create missing series.toml / tracks.csv ──
                    bool wasScaffolded = false;
                    if (!File.Exists(tomlPath))
                    {
                        var tomlContent = $"[series]\nid = \"{folderName}\"\nname = \"{folderName}\"\nplaylist-incidence = 100\n\n[[games]]\nid = \"{folderName}\"\nname = \"{folderName}\"\n\n[[playlists]]\nid = \"bgm_{folderName}\"\nincidence = 100\n\n[default-track-data]\ngame = \"{folderName}\"\nauthor = \"\"\ncopyright = \"\"\nrecord-type = \"original\"\nvolume = 2.7\n";
                        File.WriteAllText(tomlPath, tomlContent);
                        _logger.LogInformation("Created {Path}", tomlPath);
                        wasScaffolded = true;
                    }
                    if (!File.Exists(csvPath))
                    {
                        var csvContent = "filename,game,title,author,copyright,record_type,special_category,volume,info1\n";
                        File.WriteAllText(csvPath, csvContent);
                        _logger.LogInformation("Created {Path}", csvPath);
                        wasScaffolded = true;
                    }
                    if (wasScaffolded)
                        totalScaffolded++;

                    // ── Step 2: Populate tracks.csv with any new music files ──
                    // Parse series.toml for defaults
                    var tomlText = File.ReadAllText(tomlPath);
                    var tomlOptions = new TomlModelOptions { ConvertPropertyName = ToKebabCase };
                    FolderSeriesFileConfig seriesFile;
                    try
                    {
                        seriesFile = Toml.ToModel<FolderSeriesFileConfig>(tomlText, options: tomlOptions);
                    }
                    catch (System.Exception e)
                    {
                        _logger.LogError(e, "Failed to parse {Path}, skipping populate.", tomlPath);
                        continue;
                    }

                    var defaults = seriesFile.DefaultTrackData;

                    // Read existing CSV rows
                    var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        HasHeaderRecord = true,
                        TrimOptions = TrimOptions.Trim,
                        MissingFieldFound = null
                    };
                    List<FolderTrackCsvRow> existingRows;
                    using (var reader = new StreamReader(csvPath))
                    using (var csv = new CsvReader(reader, csvConfig))
                    {
                        csv.Context.RegisterClassMap<FolderTrackCsvRowMap>();
                        existingRows = csv.GetRecords<FolderTrackCsvRow>().ToList();
                    }

                    var existingFilenames = new HashSet<string>(
                        existingRows.Where(r => !string.IsNullOrWhiteSpace(r.Filename)).Select(r => r.Filename),
                        StringComparer.OrdinalIgnoreCase);

                    // Find music files not already in CSV, sorted alphabetically
                    var newFiles = Directory.GetFiles(seriesDir)
                        .Where(f => validExtensions.Contains(Path.GetExtension(f)))
                        .Select(f => Path.GetFileName(f))
                        .Where(f => !existingFilenames.Contains(f))
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (newFiles.Count == 0)
                        continue;

                    // Add new rows
                    foreach (var filename in newFiles)
                    {
                        existingRows.Add(new FolderTrackCsvRow
                        {
                            Filename = filename,
                            Game = defaults?.Game ?? folderName,
                            Title = Path.GetFileNameWithoutExtension(filename),
                            Author = defaults?.Author ?? "",
                            Copyright = defaults?.Copyright ?? "",
                            RecordType = defaults?.RecordType ?? "original",
                            Volume = defaults?.Volume ?? 2.7f
                        });
                    }

                    // Rewrite CSV with all rows
                    using (var writer = new StreamWriter(csvPath))
                    using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        HasHeaderRecord = true
                    }))
                    {
                        csv.Context.RegisterClassMap<FolderTrackCsvRowMap>();
                        csv.WriteRecords(existingRows);
                    }

                    _logger.LogInformation("Added {Count} track(s) to {Path}", newFiles.Count, csvPath);
                    totalAdded += newFiles.Count;
                }
            }

            if (totalScaffolded > 0)
                _logger.LogInformation("Scaffolded {Count} series folder(s).", totalScaffolded);
            if (totalAdded > 0)
                _logger.LogInformation("Populated {Count} new track(s) total.", totalAdded);
            if (totalScaffolded == 0 && totalAdded == 0)
                _logger.LogInformation("All series folders are up to date.");
        }

        private static string ToKebabCase(string name)
        {
            var sb = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (char.IsUpper(c))
                {
                    if (i > 0) sb.Append('-');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
