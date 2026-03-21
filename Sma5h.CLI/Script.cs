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
using VGAudio.Cli;

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

        private static readonly HashSet<string> SOURCE_AUDIO_EXTENSIONS = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".wav", ".ogg"
        };

        private const string VALIDATE_FOLDER = "songs-to-validate";

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
                    catch (Exception e)
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

        public void RunNus3Convert()
        {
            PrintBanner();

            var (modDir, seriesDir) = PromptModAndSeries();
            if (modDir == null || seriesDir == null)
                return;

            // Find source audio files that aren't already game formats
            var sourceFiles = Directory.GetFiles(seriesDir)
                .Where(f => SOURCE_AUDIO_EXTENSIONS.Contains(Path.GetExtension(f)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (sourceFiles.Count == 0)
            {
                _logger.LogWarning("No source audio files (.mp3, .flac, .wav, .ogg) found in {Dir}.", seriesDir);
                return;
            }

            var loopScoreThreshold = (double)AnsiConsole.Prompt(
                new TextPrompt<float>("Minimum loop score (only increase if subpar loops are being accepted):")
                    .DefaultValue(94.5f)) / 100.0;

            var validateDir = Path.Combine(seriesDir, VALIDATE_FOLDER);
            Directory.CreateDirectory(validateDir);

            var tempDir = Path.Combine(_musicConfig.CurrentValue.TempPath, "nus3convert");
            Directory.CreateDirectory(tempDir);

            var nus3AudioExe = Path.Combine(_musicConfig.CurrentValue.ToolsPath, MusicConstants.Resources.NUS3AUDIO_EXE_FILE);
            if (!File.Exists(nus3AudioExe))
            {
                _logger.LogError("nus3audio.exe not found at {Path}.", nus3AudioExe);
                return;
            }

            int converted = 0;
            int goodLoops = 0;
            int fullLoops = 0;

            foreach (var sourceFile in sourceFiles)
            {
                var basename = Path.GetFileNameWithoutExtension(sourceFile);
                var outputNus3 = Path.Combine(validateDir, basename + ".nus3audio");

                if (File.Exists(outputNus3))
                {
                    _logger.LogInformation("Skipping '{Basename}': already exists in songs-to-validate.", basename);
                    continue;
                }

                _logger.LogInformation("Processing '{Basename}'...", basename);

                // Step 1: Detect loop points via pymusiclooper
                var loopCandidates = RunPymusiclooper(sourceFile);
                var sourceSampleRate = GetSourceSampleRate(sourceFile);
                long loopStart, loopEnd;
                bool isFullSongLoop;

                if (loopCandidates.Count > 0 && loopCandidates.Any(c => c.score >= loopScoreThreshold))
                {
                    // Build selection choices from all candidates with full info
                    var choices = new List<string>();
                    for (int i = 0; i < loopCandidates.Count; i++)
                    {
                        var c = loopCandidates[i];
                        var startTime = sourceSampleRate > 0 ? TimeSpan.FromSeconds((double)c.loopStart / sourceSampleRate).ToString(@"mm\:ss\.ff") : "??";
                        var endTime = sourceSampleRate > 0 ? TimeSpan.FromSeconds((double)c.loopEnd / sourceSampleRate).ToString(@"mm\:ss\.ff") : "??";
                        choices.Add($"Score: {c.score:P1}  Start: {startTime} ({c.loopStart})  End: {endTime} ({c.loopEnd})  NoteDist: {c.noteDistance:F4}  LoudnessDiff: {c.loudnessDiff:F4} dB");
                    }
                    choices.Add("Reject all (use full-song loop)");

                    var selection = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title($"Loop candidates for '{Markup.Escape(basename)}':")
                            .HighlightStyle(new Style(Color.Cyan1))
                            .AddChoices(choices));

                    var selectedIndex = choices.IndexOf(selection);
                    if (selectedIndex < loopCandidates.Count)
                    {
                        var selected = loopCandidates[selectedIndex];
                        loopStart = selected.loopStart;
                        loopEnd = selected.loopEnd;
                        isFullSongLoop = false;
                        _logger.LogInformation("  Selected loop: {Start}-{End} (score: {Score:P1})", loopStart, loopEnd, selected.score);
                        goodLoops++;
                    }
                    else
                    {
                        loopStart = 0;
                        loopEnd = 0; // will be set from WAV after conversion
                        isFullSongLoop = true;
                        fullLoops++;
                    }
                }
                else
                {
                    // No candidates above threshold — auto-reject
                    loopStart = 0;
                    loopEnd = 0; // will be set from WAV after conversion
                    isFullSongLoop = true;
                    var bestScore = loopCandidates.Count > 0 ? loopCandidates[0].score : 0;
                    _logger.LogInformation("  No candidates above threshold (best: {Score:P1}), using full-song loop.", bestScore);
                    fullLoops++;
                }

                // Step 2: Convert to WAV at 48kHz if needed
                var wavFile = sourceFile;
                bool tempWav = false;
                if (!sourceFile.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    wavFile = Path.Combine(tempDir, basename + ".wav");
                    if (!RunFfmpeg(sourceFile, wavFile))
                    {
                        _logger.LogError("  ffmpeg conversion failed for '{Basename}', skipping.", basename);
                        continue;
                    }
                    tempWav = true;
                }

                // For full-song loops, get exact sample count from the converted WAV
                if (isFullSongLoop)
                {
                    var wavSamples = GetWavSampleCount(wavFile);
                    if (wavSamples <= 0)
                    {
                        _logger.LogError("  Could not determine sample count for '{Basename}', skipping.", basename);
                        if (tempWav && File.Exists(wavFile)) File.Delete(wavFile);
                        continue;
                    }
                    loopEnd = wavSamples - 1;
                    _logger.LogInformation("  Full-song loop: 0-{End}", loopEnd);
                }

                // Step 3: Convert WAV → lopus via VGAudioCli library
                var lopusFile = Path.Combine(tempDir, basename + ".lopus");
                try
                {
                    var oldOut = Console.Out;
                    using (var writer = new StringWriter())
                    {
                        Console.SetOut(writer);
                        Converter.RunConverterCli(new string[]
                        {
                            "-i", wavFile,
                            "-o", lopusFile,
                            "--opusheader", "Namco",
                            "--cbr",
                            "-l", $"{loopStart}-{loopEnd}"
                        });
                    }
                    Console.SetOut(oldOut);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "  VGAudioCli conversion failed for '{Basename}'.", basename);
                    continue;
                }

                if (!File.Exists(lopusFile) || new FileInfo(lopusFile).Length == 0)
                {
                    _logger.LogError("  VGAudioCli produced no output for '{Basename}', skipping.", basename);
                    continue;
                }

                // Step 4: Wrap lopus → nus3audio
                var toneId = DeriveToneId(basename);
                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = nus3AudioExe,
                            Arguments = $"-n -w \"{outputNus3}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    process.WaitForExit();

                    process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = nus3AudioExe,
                            Arguments = $"-A {toneId} \"{lopusFile}\" -w \"{outputNus3}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    process.WaitForExit();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "  nus3audio.exe wrapping failed for '{Basename}'.", basename);
                    continue;
                }

                if (File.Exists(outputNus3) && new FileInfo(outputNus3).Length > 0)
                {
                    _logger.LogInformation("  → {OutputPath}", outputNus3);
                    converted++;

                    // Generate loop preview clip if a loop was selected
                    if (!isFullSongLoop)
                    {
                        var loopsDir = Path.Combine(validateDir, "loops");
                        Directory.CreateDirectory(loopsDir);
                        var previewPath = Path.Combine(loopsDir, basename + "_loop.wav");
                        CreateLoopPreview(sourceFile, loopStart, loopEnd, previewPath);
                    }
                }
                else
                {
                    _logger.LogError("  nus3audio output was empty for '{Basename}'.", basename);
                }

                // Clean up temp files
                if (tempWav && File.Exists(wavFile))
                    File.Delete(wavFile);
                if (File.Exists(lopusFile))
                    File.Delete(lopusFile);
            }

            // Clean up temp dir
            try { Directory.Delete(tempDir, recursive: false); } catch { }

            _logger.LogInformation("--------------------");
            _logger.LogInformation("Nus3 conversion complete: {Converted} file(s) converted ({Good} with detected loops, {Full} with full-song loop).",
                converted, goodLoops, fullLoops);
            _logger.LogInformation("Output: {ValidateDir}", validateDir);
            _logger.LogInformation("Listen to the files in foobar2000 (with vgmstream) to verify loop points.");
            _logger.LogInformation("Delete any files you don't like, then run 'Accept Validated Nus3'.");
        }

        public void RunAcceptValidatedNus3()
        {
            PrintBanner();

            var (modDir, seriesDir) = PromptModAndSeries();
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

        private (string modDir, string seriesDir) PromptModAndSeries()
        {
            var modPath = _musicConfig.CurrentValue.Sma5hMusic.ModPath;
            Directory.CreateDirectory(modPath);

            var modDirs = Directory.GetDirectories(modPath, "*", SearchOption.TopDirectoryOnly)
                .Where(d => !Path.GetFileName(d).StartsWith("."))
                .ToList();

            if (modDirs.Count == 0)
            {
                _logger.LogWarning("No mod folders found in {ModPath}.", modPath);
                return (null, null);
            }

            // Select mod
            string selectedModDir;
            if (modDirs.Count == 1)
            {
                selectedModDir = modDirs[0];
                _logger.LogInformation("Using mod: {ModName}", Path.GetFileName(selectedModDir));
            }
            else
            {
                var modChoice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select a mod:")
                        .HighlightStyle(new Style(Color.Cyan1))
                        .AddChoices(modDirs.Select(d => Path.GetFileName(d))));
                selectedModDir = modDirs.First(d => Path.GetFileName(d) == modChoice);
            }

            // Select series
            var seriesDirs = Directory.GetDirectories(selectedModDir)
                .Where(d => !Path.GetFileName(d).StartsWith(".") && Path.GetFileName(d) != VALIDATE_FOLDER)
                .ToList();

            if (seriesDirs.Count == 0)
            {
                _logger.LogWarning("No series folders found in {ModDir}.", selectedModDir);
                return (null, null);
            }

            string selectedSeriesDir;
            if (seriesDirs.Count == 1)
            {
                selectedSeriesDir = seriesDirs[0];
                _logger.LogInformation("Using series: {SeriesName}", Path.GetFileName(selectedSeriesDir));
            }
            else
            {
                var seriesChoice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select a series:")
                        .HighlightStyle(new Style(Color.Cyan1))
                        .AddChoices(seriesDirs.Select(d => Path.GetFileName(d))));
                selectedSeriesDir = seriesDirs.First(d => Path.GetFileName(d) == seriesChoice);
            }

            return (selectedModDir, selectedSeriesDir);
        }

        private List<(long loopStart, long loopEnd, double noteDistance, double loudnessDiff, double score)> RunPymusiclooper(string filePath)
        {
            var results = new List<(long loopStart, long loopEnd, double noteDistance, double loudnessDiff, double score)>();
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "pymusiclooper",
                        Arguments = $"export-points --path \"{filePath}\" --alt-export-top 10 --fmt samples --export-to stdout",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // Format: loop_start loop_end note_distance loudness_difference score
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5
                        && long.TryParse(parts[0], out var start)
                        && long.TryParse(parts[1], out var end)
                        && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var noteDist)
                        && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var loudness)
                        && double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var score))
                    {
                        results.Add((start, end, noteDist, loudness, score));
                    }
                }

                // Sort by score descending (should already be sorted, but be safe)
                results.Sort((a, b) => b.score.CompareTo(a.score));
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "pymusiclooper failed for {File}. Falling back to full-song loop.", filePath);
            }
            return results;
        }

        private long GetWavSampleCount(string filePath)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffprobe",
                        Arguments = $"-v error -select_streams a:0 -show_entries stream=sample_rate:stream=duration -of csv=p=0 \"{filePath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                // Output format: "sample_rate,duration" e.g. "48000,185.365979"
                var parts = output.Split(',');
                if (parts.Length >= 2
                    && int.TryParse(parts[0], out var sampleRate)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
                {
                    return (long)(duration * sampleRate);
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "ffprobe failed for {File}.", filePath);
            }
            return -1;
        }

        private int GetSourceSampleRate(string filePath)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffprobe",
                        Arguments = $"-v error -select_streams a:0 -show_entries stream=sample_rate -of csv=p=0 \"{filePath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                if (int.TryParse(output, out var rate))
                    return rate;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "ffprobe failed for {File}.", filePath);
            }
            return -1;
        }

        private void CreateLoopPreview(string sourceFile, long loopStart, long loopEnd, string outputPath)
        {
            try
            {
                var sampleRate = GetSourceSampleRate(sourceFile);
                if (sampleRate <= 0)
                {
                    _logger.LogWarning("  Could not determine sample rate for loop preview.");
                    return;
                }

                double startSec = (double)loopStart / sampleRate;
                double endSec = (double)loopEnd / sampleRate;

                // Preview: 10s before loop end → 10s after loop start (simulates the loop transition)
                double seg1Start = Math.Max(0, endSec - 10);
                double seg1End = endSec;
                double seg2Start = startSec;
                double seg2End = startSec + 10;

                var s1s = seg1Start.ToString("F4", CultureInfo.InvariantCulture);
                var s1e = seg1End.ToString("F4", CultureInfo.InvariantCulture);
                var s2s = seg2Start.ToString("F4", CultureInfo.InvariantCulture);
                var s2e = seg2End.ToString("F4", CultureInfo.InvariantCulture);

                var filter = $"[0:a]atrim=start={s1s}:end={s1e},asetpts=PTS-STARTPTS[a];" +
                             $"[0:a]atrim=start={s2s}:end={s2e},asetpts=PTS-STARTPTS[b];" +
                             $"[a][b]concat=n=2:v=0:a=1";

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = $"-i \"{sourceFile}\" -filter_complex \"{filter}\" \"{outputPath}\" -y",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                    _logger.LogInformation("  Loop preview: {Path}", outputPath);
                else
                    _logger.LogWarning("  Failed to create loop preview.");
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "  Failed to create loop preview.");
            }
        }

        private bool RunFfmpeg(string inputFile, string outputWav)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = $"-i \"{inputFile}\" -ar 48000 -ac 2 \"{outputWav}\" -y",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.StandardError.ReadToEnd(); // ffmpeg outputs to stderr
                process.WaitForExit();
                return File.Exists(outputWav) && new FileInfo(outputWav).Length > 0;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "ffmpeg failed converting {File}.", inputFile);
                return false;
            }
        }

        private static string DeriveToneId(string filename)
        {
            var nameOnly = Path.GetFileNameWithoutExtension(filename).ToLowerInvariant();
            var sb = new StringBuilder(nameOnly.Length);
            foreach (var c in nameOnly)
                sb.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
            var toneId = sb.ToString().Trim('_');
            if (toneId.Length > MusicConstants.GameResources.ToneIdMaximumSize)
                toneId = toneId[..MusicConstants.GameResources.ToneIdMaximumSize];
            return toneId;
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

        public void RunCleanup()
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
