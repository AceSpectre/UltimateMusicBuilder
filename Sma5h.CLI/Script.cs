using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h.Interfaces;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Tomlyn;

namespace Sma5h.CLI
{
    public class Script
    {
        private readonly ILogger _logger;
        private readonly IStateManager _state;
        private readonly IServiceProvider _serviceProvider;
        private readonly IWorkspaceManager _workspace;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;
        private const double CLIVersion = 1.41;

        public Script(IServiceProvider serviceProvider, IWorkspaceManager workspace, IStateManager state,
            IOptionsMonitor<Sma5hMusicOptions> musicConfig, ILogger<Script> logger)
        {
            _serviceProvider = serviceProvider;
            _workspace = workspace;
            _state = state;
            _musicConfig = musicConfig;
            _logger = logger;
        }

        private void PrintBanner()
        {
            _logger.LogInformation($"Sma5h.CLI v.{CLIVersion}");
            _logger.LogInformation("--------------------");
            _logger.LogInformation("research: soneek");
            _logger.LogInformation("prcEditor: https://github.com/BenHall-7/paracobNET");
            _logger.LogInformation("msbtEditor: https://github.com/IcySon55/3DLandMSBTeditor");
            _logger.LogInformation("nus3audio:  https://github.com/jam1garner/nus3audio-rs");
            _logger.LogInformation("bgm-property:  https://github.com/jam1garner/smash-bgm-property");
            _logger.LogInformation("VGAudio:  https://github.com/Thealexbarney/VGAudio");
            _logger.LogInformation("--------------------");
        }

        public async Task RunBuild()
        {
            PrintBanner();
            await Task.Delay(1000);

            //Init State Manager
            _state.Init();

            //Init workspace
            if (!_workspace.Init())
                return;

            //Load Mods
            var mods = _serviceProvider.GetServices<ISma5hMod>();

            //Step that initialize a mod
            _logger.LogInformation("--------------------");
            var initMods = new List<ISma5hMod>();
            foreach (var mod in mods)
            {
                _logger.LogInformation("{ModeName}: Initialize mod", mod.ModName);
                if (mod.Init())
                    initMods.Add(mod);
            }

            //Step to activate an eventual build step for a mod.
            _logger.LogInformation("--------------------");
            foreach (var mod in initMods)
            {
                _logger.LogInformation("{ModeName}; Build mod changes", mod.ModName);
                mod.Build();
            }

            //Generate Output mod
            _logger.LogInformation("--------------------");
            _logger.LogInformation("Starting State Manager Mod Generation");
            _state.WriteChanges();
            _logger.LogInformation("COMPLETE - Please check the logs for any error.");
            _logger.LogInformation("--------------------");
        }

        public void RunScaffold()
        {
            PrintBanner();

            var modPath = _musicConfig.CurrentValue.Sma5hMusic.ModPath;
            Directory.CreateDirectory(modPath);

            var modDirs = Directory.GetDirectories(modPath, "*", SearchOption.TopDirectoryOnly);
            if (modDirs.Length == 0)
            {
                _logger.LogWarning("No mod folders found in {ModPath}. Create a mod folder first.", modPath);
                return;
            }

            int totalScaffolded = 0;

            foreach (var modDir in modDirs)
            {
                if (Path.GetFileName(modDir).StartsWith("."))
                    continue;

                foreach (var seriesDir in Directory.GetDirectories(modDir))
                {
                    var folderName = Path.GetFileName(seriesDir);
                    var tomlPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
                    var csvPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);

                    bool hasToml = File.Exists(tomlPath);
                    bool hasCsv = File.Exists(csvPath);

                    if (hasToml && hasCsv)
                        continue;

                    if (!hasToml)
                    {
                        var tomlContent = $"[series]\nid = \"{folderName}\"\nname = \"{folderName}\"\nplaylist-incidence = 100\n\n[[games]]\nid = \"{folderName}\"\nname = \"{folderName}\"\n\n[[playlists]]\nid = \"bgm_{folderName}\"\nincidence = 100\n\n[default-track-data]\ngame = \"{folderName}\"\nauthor = \"\"\ncopyright = \"\"\nrecord-type = \"original\"\nvolume = 2.7\n";
                        File.WriteAllText(tomlPath, tomlContent);
                        _logger.LogInformation("Created {Path}", tomlPath);
                    }

                    if (!hasCsv)
                    {
                        var csvContent = "filename,game,title,author,copyright,record_type,volume,order\n";
                        File.WriteAllText(csvPath, csvContent);
                        _logger.LogInformation("Created {Path}", csvPath);
                    }

                    totalScaffolded++;
                }
            }

            if (totalScaffolded == 0)
                _logger.LogInformation("All series folders already have series.toml and tracks.csv.");
            else
                _logger.LogInformation("Scaffolded {Count} series folder(s).", totalScaffolded);
        }

        public void RunPopulate()
        {
            PrintBanner();

            var modPath = _musicConfig.CurrentValue.Sma5hMusic.ModPath;
            Directory.CreateDirectory(modPath);

            var validExtensions = new HashSet<string>(MusicConstants.VALID_MUSIC_EXTENSIONS, StringComparer.OrdinalIgnoreCase);
            int totalAdded = 0;

            foreach (var modDir in Directory.GetDirectories(modPath, "*", SearchOption.TopDirectoryOnly))
            {
                if (Path.GetFileName(modDir).StartsWith("."))
                    continue;

                foreach (var seriesDir in Directory.GetDirectories(modDir))
                {
                    var tomlPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
                    var csvPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);

                    if (!File.Exists(tomlPath) || !File.Exists(csvPath))
                    {
                        _logger.LogDebug("Skipping {Dir}: missing series.toml or tracks.csv. Run scaffold first.", seriesDir);
                        continue;
                    }

                    // Parse series.toml for defaults
                    var tomlText = File.ReadAllText(tomlPath);
                    var tomlOptions = new TomlModelOptions { ConvertPropertyName = ToKebabCase };
                    FolderSeriesFileConfig seriesFile;
                    try
                    {
                        seriesFile = Toml.ToModel<FolderSeriesFileConfig>(tomlText, options: tomlOptions);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Failed to parse {Path}, skipping.", tomlPath);
                        continue;
                    }

                    var defaults = seriesFile.DefaultTrackData;
                    var folderName = Path.GetFileName(seriesDir);

                    // Read existing CSV rows to find already-listed filenames
                    var existingFilenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        HasHeaderRecord = true,
                        TrimOptions = TrimOptions.Trim,
                        MissingFieldFound = null
                    };
                    using (var reader = new StreamReader(csvPath))
                    using (var csv = new CsvReader(reader, csvConfig))
                    {
                        csv.Context.RegisterClassMap<FolderTrackCsvRowMap>();
                        foreach (var row in csv.GetRecords<FolderTrackCsvRow>())
                        {
                            if (!string.IsNullOrWhiteSpace(row.Filename))
                                existingFilenames.Add(row.Filename);
                        }
                    }

                    // Find music files not already in CSV, sorted alphabetically
                    var newFiles = Directory.GetFiles(seriesDir)
                        .Where(f => validExtensions.Contains(Path.GetExtension(f)))
                        .Select(f => Path.GetFileName(f))
                        .Where(f => !existingFilenames.Contains(f))
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (newFiles.Count == 0)
                        continue;

                    // Determine starting order value (after existing rows)
                    int nextOrder = existingFilenames.Count;

                    // Append new rows to CSV
                    using (var writer = new StreamWriter(csvPath, append: true))
                    using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        HasHeaderRecord = false
                    }))
                    {
                        csv.Context.RegisterClassMap<FolderTrackCsvRowMap>();
                        foreach (var filename in newFiles)
                        {
                            csv.WriteField(filename);
                            csv.WriteField(defaults?.Game ?? folderName);
                            csv.WriteField(Path.GetFileNameWithoutExtension(filename));
                            csv.WriteField(defaults?.Author ?? "");
                            csv.WriteField(defaults?.Copyright ?? "");
                            csv.WriteField(defaults?.RecordType ?? "original");
                            csv.WriteField(defaults?.Volume ?? 2.7f);
                            csv.WriteField(nextOrder);
                            csv.NextRecord();
                            nextOrder++;
                        }
                    }

                    _logger.LogInformation("Added {Count} track(s) to {Path}", newFiles.Count, csvPath);
                    totalAdded += newFiles.Count;
                }
            }

            if (totalAdded == 0)
                _logger.LogInformation("No new music files found to add.");
            else
                _logger.LogInformation("Populated {Count} new track(s) total.", totalAdded);
        }

        private static string ToKebabCase(string name)
        {
            var sb = new System.Text.StringBuilder(name.Length + 4);
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
