using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h.Interfaces;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using Sma5h.Mods.Music.Services;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Tomlyn;
using Tomlyn.Model;

namespace UMB.CLI.Services
{
    public class BuildService
    {
        private readonly ILogger _logger;
        private readonly IStateManager _state;
        private readonly IServiceProvider _serviceProvider;
        private readonly IWorkspaceManager _workspace;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;

        public BuildService(IServiceProvider serviceProvider, IWorkspaceManager workspace, IStateManager state,
            IOptionsMonitor<Sma5hMusicOptions> musicConfig, ILogger<BuildService> logger)
        {
            _serviceProvider = serviceProvider;
            _workspace = workspace;
            _state = state;
            _musicConfig = musicConfig;
            _logger = logger;
        }

        public async Task Run(string requestedMod = null)
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

            // Non-interactive terminals (e.g. the desktop app) cannot show selection
            // prompts. Default to building everything and proceeding past warnings.
            var interactive = AnsiConsole.Profile.Capabilities.Interactive;

            string selectedMod = null;
            if (!string.IsNullOrWhiteSpace(requestedMod))
            {
                // Caller (e.g. the desktop app) named the mod explicitly.
                var match = modDirs.FirstOrDefault(d =>
                    Path.GetFileName(d).Equals(requestedMod, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    _logger.LogError("Requested mod \"{ModName}\" not found in {ModPath}.", requestedMod, modPath);
                    return;
                }
                selectedMod = Path.GetFileName(match);
                _logger.LogInformation("Building mod: {ModName}", selectedMod);
            }
            else if (modDirs.Count == 1)
            {
                selectedMod = Path.GetFileName(modDirs[0]);
                _logger.LogInformation("Building mod: {ModName}", selectedMod);
            }
            else if (!interactive)
            {
                _logger.LogInformation("Non-interactive build: building all {Count} mods.", modDirs.Count);
            }
            else
            {
                var choices = modDirs.Select(d => Path.GetFileName(d)).ToList();
                choices.Insert(0, "All");

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .WrapAround()
                        .Title("Select a mod to build:")
                        .HighlightStyle(new Style(Color.Cyan1))
                        .AddChoices(choices));

                if (choice != "All")
                    selectedMod = choice;
            }

            if (selectedMod != null)
                MusicModManagerService.ModFilter = new HashSet<string> { selectedMod };

            var activeMods = selectedMod != null
                ? new List<string> { modDirs.First(d => Path.GetFileName(d).Equals(selectedMod, StringComparison.OrdinalIgnoreCase)) }
                : modDirs;

            var seriesFilters = new Dictionary<string, HashSet<string>>();

            foreach (var modDir in activeMods)
            {
                var seriesDirs = Directory.GetDirectories(modDir)
                    .Where(d => !Path.GetFileName(d).StartsWith("."))
                    .ToList();

                if (seriesDirs.Count > 1 && interactive)
                {
                    var buildScope = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .WrapAround()
                            .Title($"Build scope for [cyan]{Markup.Escape(Path.GetFileName(modDir))}[/]:")
                            .HighlightStyle(new Style(Color.Cyan1))
                            .AddChoices("Compile all series", "Select series to compile"));

                    if (buildScope == "Select series to compile")
                    {
                        var seriesNames = seriesDirs.Select(d => Path.GetFileName(d)).OrderBy(n => n).ToList();

                        var selectedSeries = AnsiConsole.Prompt(
                            new MultiSelectionPrompt<string>()
                                .WrapAround()
                                .Title("Select series to compile:")
                                .HighlightStyle(new Style(Color.Cyan1))
                                .InstructionsText("(Press [cyan]space[/] to toggle, [green]enter[/] to confirm)")
                                .NotRequired()
                                .AddChoices(seriesNames));

                        seriesFilters[modDir] = new HashSet<string>(selectedSeries);
                    }
                }
            }

            // Set the series filter so FolderMusicMod can read it during Init
            FolderMusicMod.SeriesFilterByMod = seriesFilters.Count > 0 ? seriesFilters : null;

            foreach (var modDir in activeMods)
            {
                var orderPath = Path.Combine(modDir,
                    MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_ORDER_TOML_FILE);
                if (File.Exists(orderPath))
                {
                    try
                    {
                        var tomlText = File.ReadAllText(orderPath);
                        var model = Toml.ToModel(tomlText);
                        if (model.TryGetValue("order", out var val) && val is TomlArray arr)
                        {
                            var orderDict = new Dictionary<string, int>();
                            int idx = 0;
                            foreach (var item in arr.OfType<string>())
                                orderDict[item] = idx++;
                            Sma5hMusic.ExplicitSeriesOrder = orderDict;
                            _logger.LogInformation("Loaded explicit series order from {Path} ({Count} entries).",
                                orderPath, orderDict.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse series-order.toml at {Path}. Using auto-ordering.", orderPath);
                    }
                }
                else
                {
                    _logger.LogWarning("No series-order.toml found in {ModDir}. Custom series will be auto-ordered.",
                        Path.GetFileName(modDir));
                }
            }

            var warnings = ValidateSeries(activeMods, seriesFilters);

            if (warnings.Count > 0)
            {
                _logger.LogWarning("Pre-build validation found {Count} warning(s):", warnings.Count);
                foreach (var w in warnings)
                    _logger.LogWarning("{Warning}", w);

                if (interactive)
                {
                    var proceed = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .WrapAround()
                            .Title("[yellow]Validation warnings found. Proceed with build?[/]")
                            .HighlightStyle(new Style(Color.Cyan1))
                            .AddChoices("Yes - build anyway", "No - cancel build"));

                    if (proceed.StartsWith("No"))
                    {
                        _logger.LogInformation("Build cancelled.");
                        return;
                    }
                }
                else
                {
                    _logger.LogWarning("Non-interactive build: proceeding despite validation warnings.");
                }
            }

            try
            {
                await Task.Delay(1000);

                _state.Init();

                if (!_workspace.Init())
                    return;

                var mods = _serviceProvider.GetServices<ISma5hMod>();

                _logger.LogInformation("--------------------");
                var initMods = new List<ISma5hMod>();
                foreach (var mod in mods)
                {
                    _logger.LogInformation("{ModeName}: Initialize mod", mod.ModName);
                    if (mod.Init())
                        initMods.Add(mod);
                }

                _logger.LogInformation("--------------------");
                foreach (var mod in initMods)
                {
                    _logger.LogInformation("{ModeName}; Build mod changes", mod.ModName);
                    mod.Build();
                }

                _logger.LogInformation("--------------------");
                _logger.LogInformation("Starting State Manager Mod Generation");
                _state.WriteChanges();
                _logger.LogInformation("COMPLETE - Please check the logs for any error.");
                _logger.LogInformation("--------------------");
            }
            finally
            {
                MusicModManagerService.ModFilter = null;
                FolderMusicMod.SeriesFilterByMod = null;
                Sma5hMusic.ExplicitSeriesOrder = null;
            }
        }
        private List<string> ValidateSeries(List<string> activeMods, Dictionary<string, HashSet<string>> seriesFilters)
        {
            var warnings = new List<string>();

            foreach (var modDir in activeMods)
            {
                var seriesDirs = Directory.GetDirectories(modDir)
                    .Where(d => !Path.GetFileName(d).StartsWith("."))
                    .ToList();

                if (seriesFilters.TryGetValue(modDir, out var filter))
                    seriesDirs = seriesDirs.Where(d => filter.Contains(Path.GetFileName(d))).ToList();

                var modName = Path.GetFileName(modDir);

                // Load this mod's series-order.toml so we can warn about custom series
                // that have no explicit position (their in-game order is otherwise unpredictable).
                var modOrderedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var modOrderPath = Path.Combine(modDir,
                    MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_ORDER_TOML_FILE);
                if (File.Exists(modOrderPath))
                {
                    try
                    {
                        var orderTomlText = File.ReadAllText(modOrderPath);
                        var orderModel = Toml.ToModel(orderTomlText);
                        if (orderModel.TryGetValue("order", out var orderVal) && orderVal is TomlArray arr)
                            foreach (var id in arr.OfType<string>())
                                modOrderedIds.Add(id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse {Path} for validation.", modOrderPath);
                    }
                }

                foreach (var seriesDir in seriesDirs)
                {
                    var seriesName = Path.GetFileName(seriesDir);
                    var prefix = $"{modName}/{seriesName}";
                    var csvPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
                    var tomlPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);

                    if (!File.Exists(csvPath))
                        continue;

                    var validGameIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var playlistSongRefs = new List<(string playlistId, string songRef)>();
                    FolderSeriesFileConfig seriesConfig = null;
                    if (File.Exists(tomlPath))
                    {
                        try
                        {
                            var tomlText = File.ReadAllText(tomlPath);
                            seriesConfig = Toml.ToModel<FolderSeriesFileConfig>(tomlText,
                                options: new TomlModelOptions { ConvertPropertyName = ToKebabCase });
                            foreach (var game in seriesConfig.Games ?? new List<FolderGameConfig>())
                            {
                                if (!string.IsNullOrWhiteSpace(game.Id))
                                    validGameIds.Add(game.Id);
                            }
                            foreach (var pl in seriesConfig.Playlists ?? new List<FolderPlaylistOverrideConfig>())
                            {
                                if (string.IsNullOrWhiteSpace(pl.Id)) continue;
                                if (pl.Songs == null || FolderMusicMod.IsWildcardSongs(pl.Songs)) continue;
                                foreach (var s in FolderMusicMod.ExplicitSongs(pl.Songs))
                                    playlistSongRefs.Add((pl.Id, s));
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to parse {Path} for validation.", tomlPath);
                        }
                    }

                    if (seriesConfig?.Series != null
                        && !seriesConfig.Series.ExistingSeries
                        && !string.IsNullOrWhiteSpace(seriesConfig.Series.Id)
                        && !string.Equals(seriesConfig.Series.Id, "etc", StringComparison.OrdinalIgnoreCase)
                        && !modOrderedIds.Contains(seriesConfig.Series.Id))
                    {
                        warnings.Add($"  {prefix}: custom series \"{seriesConfig.Series.Id}\" is not listed in series-order.toml. Its in-game position will be unpredictable. Run 'Scaffold' to append it, or use 'Order Series' to place it manually.");
                    }

                    var csvFilenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                        {
                            HasHeaderRecord = true,
                            TrimOptions = TrimOptions.Trim,
                            MissingFieldFound = null,
                            BadDataFound = null,
                        };
                        using var reader = new StreamReader(csvPath);
                        using var csv = new CsvReader(reader, csvConfig);
                        csv.Read();
                        csv.ReadHeader();
                        var headers = csv.HeaderRecord;
                        bool hasOrderColumn = headers.Contains("order");

                        int rowNum = 0;
                        while (csv.Read())
                        {
                            rowNum++;
                            var filename = csv.GetField("filename")?.Trim() ?? "";
                            var game = csv.GetField("game")?.Trim() ?? "";
                            var title = csv.GetField("title")?.Trim() ?? "";

                            if (string.IsNullOrWhiteSpace(filename))
                                continue;

                            csvFilenames.Add(filename);

                            if (validGameIds.Count > 0 && !string.IsNullOrWhiteSpace(game)
                                && !validGameIds.Contains(game))
                            {
                                warnings.Add($"  {prefix}: \"{title}\" ({filename}) has game \"{game}\" not found in series.toml");
                            }

                            if (!hasOrderColumn)
                            {
                                if (rowNum == 1) // only warn once per file
                                    warnings.Add($"  {prefix}: tracks.csv is missing the \"order\" column");
                            }
                            else
                            {
                                var orderVal = csv.GetField("order")?.Trim() ?? "";
                                if (string.IsNullOrWhiteSpace(orderVal) || !int.TryParse(orderVal, out _))
                                    warnings.Add($"  {prefix}: \"{title}\" ({filename}) is missing a valid order number");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to validate {Path}.", csvPath);
                        continue;
                    }

                    var orphanedNus3 = Directory.GetFiles(seriesDir, "*.nus3audio")
                        .Select(Path.GetFileName)
                        .Where(f => !csvFilenames.Contains(f))
                        .OrderBy(f => f)
                        .ToList();

                    foreach (var file in orphanedNus3)
                        warnings.Add($"  {prefix}: {file} is not listed in tracks.csv");

                    // Matches by stem so "Destroyer" and "Destroyer.nus3audio" are both accepted.
                    if (playlistSongRefs.Count > 0)
                    {
                        var csvStems = new HashSet<string>(
                            csvFilenames.Select(f => Path.GetFileNameWithoutExtension(f)),
                            StringComparer.OrdinalIgnoreCase);
                        foreach (var (playlistId, songRef) in playlistSongRefs)
                        {
                            var stem = Path.GetFileNameWithoutExtension(songRef);
                            if (!csvStems.Contains(stem))
                                warnings.Add($"  {prefix}: [[playlists]] \"{playlistId}\" lists song \"{songRef}\" which doesn't match any track in tracks.csv");
                        }
                    }
                }
            }

            return warnings;
        }

        private static string ToKebabCase(string name)
        {
            var sb = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]) && i > 0)
                    sb.Append('-');
                sb.Append(char.ToLower(name[i]));
            }
            return sb.ToString();
        }
    }
}
