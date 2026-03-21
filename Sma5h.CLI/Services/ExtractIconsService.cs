using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Sma5h.CLI.Services
{
    public class ExtractIconsService
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;

        public ExtractIconsService(IOptionsMonitor<Sma5hMusicOptions> musicConfig, ILogger<ExtractIconsService> logger)
        {
            _musicConfig = musicConfig;
            _logger = logger;
        }

        public void Run()
        {
            Script.PrintBanner(_logger);

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
    }
}
