using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h.Interfaces;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectre.Console;
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

            // Let user pick which mod to build
            string selectedMod = null;
            if (modDirs.Count == 1)
            {
                selectedMod = Path.GetFileName(modDirs[0]);
                _logger.LogInformation("Building mod: {ModName}", selectedMod);
            }
            else
            {
                var choices = modDirs.Select(d => Path.GetFileName(d)).ToList();
                choices.Insert(0, "All");

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select a mod to build:")
                        .HighlightStyle(new Style(Color.Cyan1))
                        .AddChoices(choices));

                if (choice != "All")
                    selectedMod = choice;
            }

            // Temporarily disable unselected mods by prefixing with '.'
            var disabledDirs = new List<(string original, string disabled)>();
            if (selectedMod != null)
            {
                foreach (var dir in modDirs)
                {
                    var name = Path.GetFileName(dir);
                    if (!name.Equals(selectedMod, StringComparison.OrdinalIgnoreCase))
                    {
                        var disabledPath = Path.Combine(Path.GetDirectoryName(dir), "." + name);
                        Directory.Move(dir, disabledPath);
                        disabledDirs.Add((dir, disabledPath));
                    }
                }
            }

            try
            {
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
            finally
            {
                // Re-enable disabled mods
                foreach (var (original, disabled) in disabledDirs)
                {
                    if (Directory.Exists(disabled))
                        Directory.Move(disabled, original);
                }
            }
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
                        var csvContent = "filename,game,title,author,copyright,record_type,volume,order\n";
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
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Failed to parse {Path}, skipping populate.", tomlPath);
                        continue;
                    }

                    var defaults = seriesFile.DefaultTrackData;

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

            if (totalScaffolded > 0)
                _logger.LogInformation("Scaffolded {Count} series folder(s).", totalScaffolded);
            if (totalAdded > 0)
                _logger.LogInformation("Populated {Count} new track(s) total.", totalAdded);
            if (totalScaffolded == 0 && totalAdded == 0)
                _logger.LogInformation("All series folders are up to date.");
        }

        public void RunConvert()
        {
            PrintBanner();

            var oldModPath = AnsiConsole.Prompt(
                new TextPrompt<string>("Enter path to old Sma5h mod folder:")
                    .Validate(path =>
                    {
                        if (!Directory.Exists(path))
                            return ValidationResult.Error("Directory does not exist.");
                        if (!File.Exists(Path.Combine(path, MusicConstants.MusicModFiles.MUSIC_MOD_METADATA_JSON_FILE)))
                            return ValidationResult.Error("No metadata_mod.json found in that folder.");
                        return ValidationResult.Success();
                    }));

            var jsonPath = Path.Combine(oldModPath, MusicConstants.MusicModFiles.MUSIC_MOD_METADATA_JSON_FILE);
            JObject json;
            try
            {
                json = JObject.Parse(File.ReadAllText(jsonPath));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to parse {Path}.", jsonPath);
                return;
            }

            // Load existing series IDs from ParamLabels.csv
            var existingSeriesIds = LoadExistingSeriesIds();

            // Load base game tone IDs from nusbank_ids.csv
            var baseGameToneIds = LoadBaseGameToneIds();

            // Prompt for output mod name
            var modName = json["name"]?.ToString() ?? Path.GetFileName(oldModPath);
            var outputModName = AnsiConsole.Prompt(
                new TextPrompt<string>("Name for the new UMB mod folder:")
                    .DefaultValue(SanitizeFolderName(modName)));

            var modPath = _musicConfig.CurrentValue.Sma5hMusic.ModPath;
            var outputModDir = Path.Combine(modPath, outputModName);

            if (Directory.Exists(outputModDir))
            {
                _logger.LogError("Output folder already exists: {Path}. Delete it first or choose a different name.", outputModDir);
                return;
            }

            Directory.CreateDirectory(outputModDir);

            var seriesArray = json["series"] as JArray;
            if (seriesArray == null || seriesArray.Count == 0)
            {
                _logger.LogWarning("No series found in metadata_mod.json.");
                return;
            }

            int totalTracks = 0;
            int totalSeries = 0;

            foreach (var series in seriesArray)
            {
                var seriesNameId = series["name_id"]?.ToString();
                var uiSeriesId = series["ui_series_id"]?.ToString();
                var seriesName = series["msbt_title"]?["us_en"]?.ToString() ?? seriesNameId;
                bool isExisting = existingSeriesIds.Contains(uiSeriesId);

                if (string.IsNullOrWhiteSpace(seriesNameId))
                {
                    _logger.LogWarning("Skipping series with empty name_id.");
                    continue;
                }

                var seriesDir = Path.Combine(outputModDir, seriesNameId);
                Directory.CreateDirectory(seriesDir);

                // Collect all games and tracks from this series
                var games = series["games"] as JArray;
                if (games == null || games.Count == 0)
                {
                    _logger.LogWarning("Series '{SeriesId}' has no games, skipping.", seriesNameId);
                    continue;
                }

                var gameInfos = new List<(string id, string name)>();
                var trackRows = new List<ConvertTrackRow>();

                foreach (var game in games)
                {
                    var gameNameId = game["name_id"]?.ToString();
                    var gameName = game["msbt_title"]?["us_en"]?.ToString() ?? gameNameId;

                    if (string.IsNullOrWhiteSpace(gameNameId))
                        continue;

                    if (!gameInfos.Any(g => g.id == gameNameId))
                        gameInfos.Add((gameNameId, gameName));

                    var bgms = game["bgms"] as JArray;
                    if (bgms == null) continue;

                    foreach (var bgm in bgms)
                    {
                        var filename = bgm["filename"]?.ToString();
                        if (string.IsNullOrWhiteSpace(filename))
                            continue;

                        var dbRoot = bgm["db_root"];
                        var title = dbRoot?["msbt_title"]?["us_en"]?.ToString()
                                    ?? Path.GetFileNameWithoutExtension(filename);
                        var author = dbRoot?["msbt_author"]?["us_en"]?.ToString() ?? "";
                        var copyright = dbRoot?["msbt_copyright"]?["us_en"]?.ToString() ?? "";
                        var recordType = dbRoot?["record_type"]?.ToString() ?? "record_original";
                        if (recordType.StartsWith("record_"))
                            recordType = recordType.Substring(7);
                        var volume = bgm["nus3bank_config"]?["volume"]?.Value<float>() ?? 2.7f;
                        var testDispOrder = dbRoot?["test_disp_order"]?.Value<int>() ?? 0;
                        var specialCategory = bgm["stream_set"]?["special_category"]?.ToString();

                        // Resolve info1 to a filename (strip "info_" prefix → tone_id → find matching filename)
                        var info1Raw = bgm["stream_set"]?["info1"]?.ToString();
                        string info1Filename = null;
                        if (!string.IsNullOrEmpty(info1Raw))
                        {
                            var info1ToneId = info1Raw.StartsWith("info_") ? info1Raw.Substring(5) : info1Raw;
                            // Will resolve after all tracks are collected
                            info1Filename = info1ToneId; // placeholder — resolved below
                        }

                        trackRows.Add(new ConvertTrackRow
                        {
                            Filename = filename,
                            Game = gameNameId,
                            Title = title,
                            Author = author,
                            Copyright = copyright,
                            RecordType = recordType,
                            SpecialCategory = specialCategory,
                            Volume = volume,
                            OriginalOrder = testDispOrder,
                            Info1 = info1Filename
                        });

                        // Copy audio file
                        var srcFile = Path.Combine(oldModPath, filename);
                        var destFile = Path.Combine(seriesDir, filename);
                        if (File.Exists(srcFile))
                        {
                            File.Copy(srcFile, destFile, overwrite: false);
                        }
                        else
                        {
                            _logger.LogWarning("Audio file not found: {File}", srcFile);
                        }
                    }
                }

                if (trackRows.Count == 0)
                {
                    _logger.LogWarning("Series '{SeriesId}' has no tracks, skipping.", seriesNameId);
                    continue;
                }

                // Sort tracks by original display order, then by filename as tiebreaker
                trackRows = trackRows
                    .OrderBy(t => t.OriginalOrder)
                    .ThenBy(t => t.Filename, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Resolve info1 tone_id placeholders to actual filenames
                var toneIdToFilename = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in trackRows)
                {
                    var toneId = Path.GetFileNameWithoutExtension(t.Filename).ToLowerInvariant();
                    toneIdToFilename[toneId] = t.Filename;
                }
                foreach (var t in trackRows)
                {
                    if (t.Info1 == null) continue;
                    if (toneIdToFilename.TryGetValue(t.Info1, out var resolvedFilename))
                    {
                        t.Info1 = resolvedFilename;
                    }
                    else if (baseGameToneIds.Contains(t.Info1))
                    {
                        // Reference to a base game song — store as info_ ID for direct use
                        t.Info1 = "info_" + t.Info1;
                    }
                    else
                    {
                        _logger.LogWarning("Track '{Filename}': info1 references tone_id '{ToneId}' which was not found in this series or the base game.", t.Filename, t.Info1);
                        t.Info1 = null;
                    }
                }

                // Write series.toml
                WriteConvertedSeriesToml(seriesDir, seriesNameId, seriesName, isExisting, gameInfos);

                // Write tracks.csv
                WriteConvertedTracksCsv(seriesDir, trackRows);

                totalTracks += trackRows.Count;
                totalSeries++;
                _logger.LogInformation("Converted series '{SeriesName}' ({SeriesId}): {TrackCount} tracks{Existing}",
                    seriesName, seriesNameId, trackRows.Count, isExisting ? " [existing series]" : "");
            }

            _logger.LogInformation("--------------------");
            _logger.LogInformation("Conversion complete: {SeriesCount} series, {TrackCount} tracks → {OutputDir}",
                totalSeries, totalTracks, outputModDir);
        }

        public void RunExtractIcons()
        {
            PrintBanner();

            var ultimateTexCli = Path.Combine(_musicConfig.CurrentValue.ToolsPath, "Windows", "ultimate_tex_cli.exe");
            if (!File.Exists(ultimateTexCli))
            {
                _logger.LogError("ultimate_tex_cli.exe not found at {Path}.", ultimateTexCli);
                return;
            }

            var sourceModPath = AnsiConsole.Prompt(
                new TextPrompt<string>("Enter path to source Sma5h mod folder (with metadata_mod.json):")
                    .Validate(path =>
                    {
                        if (!Directory.Exists(path))
                            return ValidationResult.Error("Directory does not exist.");
                        if (!File.Exists(Path.Combine(path, MusicConstants.MusicModFiles.MUSIC_MOD_METADATA_JSON_FILE)))
                            return ValidationResult.Error("No metadata_mod.json found in that folder.");
                        return ValidationResult.Success();
                    }));

            var builtModPath = AnsiConsole.Prompt(
                new TextPrompt<string>("Enter path to built Sma5h mod folder (containing ui/replace/series/series_0/):")
                    .Validate(path =>
                    {
                        if (!Directory.Exists(path))
                            return ValidationResult.Error("Directory does not exist.");
                        var series0Dir = Path.Combine(path, "ui", "replace", "series", "series_0");
                        if (!Directory.Exists(series0Dir))
                            return ValidationResult.Error("No ui/replace/series/series_0/ folder found.");
                        return ValidationResult.Success();
                    }));

            // Parse metadata_mod.json to get series name_ids
            var jsonPath = Path.Combine(sourceModPath, MusicConstants.MusicModFiles.MUSIC_MOD_METADATA_JSON_FILE);
            JObject json;
            try
            {
                json = JObject.Parse(File.ReadAllText(jsonPath));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to parse {Path}.", jsonPath);
                return;
            }

            // Build set of series name_ids from the source mod
            var seriesIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seriesArray = json["series"] as JArray;
            if (seriesArray != null)
            {
                foreach (var series in seriesArray)
                {
                    var nameId = series["name_id"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(nameId))
                        seriesIds.Add(nameId);
                }
            }

            if (seriesIds.Count == 0)
            {
                _logger.LogWarning("No series found in metadata_mod.json.");
                return;
            }

            // Find the UMB mod folder to place icons into
            var modPath = _musicConfig.CurrentValue.Sma5hMusic.ModPath;
            var series0Dir = Path.Combine(builtModPath, "ui", "replace", "series", "series_0");
            int totalExtracted = 0;

            foreach (var seriesId in seriesIds)
            {
                // Built BNTX file: series_0_{seriesId}.bntx
                var bntxFile = Path.Combine(series0Dir, $"series_0_{seriesId}.bntx");
                if (!File.Exists(bntxFile))
                    continue;

                // Find the matching UMB series subfolder across all mod folders
                string targetSeriesDir = null;
                foreach (var modDir in Directory.GetDirectories(modPath, "*", SearchOption.TopDirectoryOnly))
                {
                    var candidate = Path.Combine(modDir, seriesId);
                    if (Directory.Exists(candidate))
                    {
                        targetSeriesDir = candidate;
                        break;
                    }
                }

                if (targetSeriesDir == null)
                {
                    _logger.LogWarning("No UMB series folder found for '{SeriesId}', skipping icon.", seriesId);
                    continue;
                }

                var outputPng = Path.Combine(targetSeriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_ICON_PNG_FILE);

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ultimateTexCli,
                        Arguments = $"\"{bntxFile}\" \"{outputPng}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit();

                if (File.Exists(outputPng))
                {
                    _logger.LogInformation("Extracted icon for '{SeriesId}' → {OutputPath}", seriesId, outputPng);
                    totalExtracted++;
                }
                else
                {
                    _logger.LogError("Failed to extract icon for '{SeriesId}' from {BntxFile}.", seriesId, bntxFile);
                }
            }

            _logger.LogInformation("--------------------");
            if (totalExtracted > 0)
                _logger.LogInformation("Extracted {Count} icon(s).", totalExtracted);
            else
                _logger.LogInformation("No icons found to extract.");
        }

        private HashSet<string> LoadExistingSeriesIds()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paramLabelsPath = Path.Combine("Resources", "ParamLabels.csv");
            if (!File.Exists(paramLabelsPath))
            {
                _logger.LogWarning("ParamLabels.csv not found at {Path}. Existing series detection disabled.", paramLabelsPath);
                return ids;
            }
            foreach (var line in File.ReadLines(paramLabelsPath))
            {
                var commaIdx = line.IndexOf(',');
                if (commaIdx < 0) continue;
                var label = line.Substring(commaIdx + 1).Trim();
                if (label.StartsWith("ui_series_") && label != "ui_series_none"
                    && label != "ui_series_all" && label != "ui_series_random"
                    && label != "ui_series_mymusic")
                {
                    ids.Add(label);
                }
            }
            return ids;
        }

        private HashSet<string> LoadBaseGameToneIds()
        {
            var toneIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nusBankPath = Path.Combine(_musicConfig.CurrentValue.ResourcesPath, MusicConstants.Resources.NUS3BANK_IDS_FILE);
            if (!File.Exists(nusBankPath))
            {
                _logger.LogWarning("nusbank_ids.csv not found at {Path}. Base game tone_id detection disabled.", nusBankPath);
                return toneIds;
            }
            foreach (var line in File.ReadLines(nusBankPath).Skip(1)) // skip header
            {
                var parts = line.Split(',');
                if (parts.Length < 2) continue;
                var name = parts[1].Trim();
                if (name.StartsWith("bgm_"))
                    toneIds.Add(name.Substring(4)); // strip "bgm_" prefix to get tone_id
            }
            return toneIds;
        }

        private void WriteConvertedSeriesToml(string seriesDir, string seriesId, string seriesName,
            bool isExisting, List<(string id, string name)> games)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[series]");
            sb.AppendLine($"id = \"{EscapeTomlString(seriesId)}\"");
            sb.AppendLine($"name = \"{EscapeTomlString(seriesName)}\"");
            if (isExisting)
                sb.AppendLine("existing-series = true");
            sb.AppendLine();

            foreach (var (id, name) in games)
            {
                sb.AppendLine("[[games]]");
                sb.AppendLine($"id = \"{EscapeTomlString(id)}\"");
                sb.AppendLine($"name = \"{EscapeTomlString(name)}\"");
                sb.AppendLine();
            }

            if (!isExisting)
            {
                sb.AppendLine("[[playlists]]");
                sb.AppendLine($"id = \"bgm_{EscapeTomlString(seriesId)}\"");
                sb.AppendLine("incidence = 100");
                sb.AppendLine();
            }

            var tomlPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
            File.WriteAllText(tomlPath, sb.ToString());
        }

        private void WriteConvertedTracksCsv(string seriesDir, List<ConvertTrackRow> tracks)
        {
            var csvPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
            using var writer = new StreamWriter(csvPath);
            using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true
            });

            // Write header
            csv.WriteField("filename");
            csv.WriteField("game");
            csv.WriteField("title");
            csv.WriteField("author");
            csv.WriteField("copyright");
            csv.WriteField("record_type");
            csv.WriteField("special_category");
            csv.WriteField("volume");
            csv.WriteField("info1");
            csv.WriteField("order");
            csv.NextRecord();

            // Write rows in order
            for (int i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                csv.WriteField(t.Filename);
                csv.WriteField(t.Game);
                csv.WriteField(t.Title);
                csv.WriteField(t.Author);
                csv.WriteField(t.Copyright);
                csv.WriteField(t.RecordType);
                csv.WriteField(t.SpecialCategory ?? "");
                csv.WriteField(t.Volume);
                csv.WriteField(t.Info1 ?? "");
                csv.WriteField(i);
                csv.NextRecord();
            }
        }

        private static string EscapeTomlString(string value)
        {
            return value?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
        }

        private static string SanitizeFolderName(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                if (Path.GetInvalidFileNameChars().Contains(c))
                    sb.Append('_');
                else
                    sb.Append(c);
            }
            return sb.ToString().Trim().ToLowerInvariant().Replace(' ', '-');
        }

        private class ConvertTrackRow
        {
            public string Filename { get; set; }
            public string Game { get; set; }
            public string Title { get; set; }
            public string Author { get; set; }
            public string Copyright { get; set; }
            public string RecordType { get; set; }
            public string SpecialCategory { get; set; }
            public float Volume { get; set; }
            public int OriginalOrder { get; set; }
            public string Info1 { get; set; }
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
