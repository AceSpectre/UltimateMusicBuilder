using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h.CLI.Views;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Tomlyn;
using Tomlyn.Model;

namespace Sma5h.CLI.Services
{
    public class SeriesOrderService
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;

        public SeriesOrderService(IOptionsMonitor<Sma5hMusicOptions> musicConfig,
            ILogger<SeriesOrderService> logger)
        {
            _musicConfig = musicConfig;
            _logger = logger;
        }

        public void Run()
        {
            Script.PrintBanner(_logger);

            // Select mod directory
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

            string selectedModDir;
            if (modDirs.Count == 1)
            {
                selectedModDir = modDirs[0];
                _logger.LogInformation("Using mod: {ModName}", Path.GetFileName(selectedModDir));
            }
            else
            {
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select a mod:")
                        .HighlightStyle(new Style(Color.Cyan1))
                        .AddChoices(modDirs.Select(d => Path.GetFileName(d))));
                selectedModDir = modDirs.First(d => Path.GetFileName(d) == choice);
            }

            // Scan for custom series
            var customSeries = new List<(string id, string name, string iconPath)>();
            foreach (var seriesDir in Directory.GetDirectories(selectedModDir))
            {
                if (Path.GetFileName(seriesDir).StartsWith("."))
                    continue;

                var tomlPath = Path.Combine(seriesDir,
                    MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
                if (!File.Exists(tomlPath))
                    continue;

                FolderSeriesFileConfig config;
                try
                {
                    var tomlText = File.ReadAllText(tomlPath);
                    config = Toml.ToModel<FolderSeriesFileConfig>(tomlText,
                        options: new TomlModelOptions { ConvertPropertyName = ToKebabCase });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse {Path}, skipping.", tomlPath);
                    continue;
                }

                if (config.Series == null) continue;
                if (config.Series.ExistingSeries) continue;
                if (string.Equals(config.Series.Id, "etc", StringComparison.OrdinalIgnoreCase)) continue;

                var iconPath = Path.Combine(seriesDir,
                    MusicConstants.MusicModFiles.FOLDER_MOD_ICON_PNG_FILE);

                customSeries.Add((
                    config.Series.Id,
                    config.Series.Name ?? config.Series.Id,
                    File.Exists(iconPath) ? iconPath : null
                ));
            }

            if (customSeries.Count == 0)
            {
                _logger.LogWarning("No custom series found in {ModDir}.", selectedModDir);
                return;
            }

            // Load existing order
            var orderFile = Path.Combine(selectedModDir,
                MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_ORDER_TOML_FILE);
            var existingOrder = LoadSeriesOrder(orderFile);

            // Sort: ordered series first (in saved order), then unordered alphabetically
            var orderedIds = existingOrder ?? new List<string>();
            var sorted = customSeries
                .OrderBy(s =>
                {
                    var idx = orderedIds.IndexOf(s.id);
                    return idx >= 0 ? idx : int.MaxValue;
                })
                .ThenBy(s => s.name)
                .ToList();

            // Build ViewModels
            var viewModels = sorted.Select(s => new SeriesViewModel
            {
                Id = s.id,
                Name = s.name,
                IconPath = s.iconPath,
            }).ToList();

            // Launch Avalonia window on STA thread
            List<string> result = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var lifetime = new ClassicDesktopStyleApplicationLifetime
                    {
                        ShutdownMode = ShutdownMode.OnMainWindowClose
                    };

                    var builder = AppBuilder.Configure<SeriesOrderApp>()
                        .UsePlatformDetect()
                        .SetupWithLifetime(lifetime);

                    var window = new SeriesOrderWindow(viewModels);
                    window.Closed += (_, _) => result = window.Result;
                    lifetime.MainWindow = window;
                    lifetime.Start(Array.Empty<string>());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to launch series order window.");
                }
            });
            if (OperatingSystem.IsWindows())
                thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            // Save result
            if (result != null)
            {
                SaveSeriesOrder(orderFile, result);
                _logger.LogInformation("Series order saved to {Path}.", orderFile);
            }
            else
            {
                _logger.LogInformation("Series ordering cancelled.");
            }
        }

        private List<string> LoadSeriesOrder(string path)
        {
            if (!File.Exists(path))
                return null;
            try
            {
                var toml = File.ReadAllText(path);
                var model = Toml.ToModel(toml);
                if (model.TryGetValue("order", out var val) && val is TomlArray arr)
                    return arr.OfType<string>().ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse {Path}.", path);
            }
            return null;
        }

        private void SaveSeriesOrder(string path, List<string> order)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Custom series display order");
            sb.AppendLine("# Listed series appear after official series, before \"Other\"");
            sb.AppendLine("# Unlisted custom series will be placed after these");
            sb.AppendLine("order = [");
            foreach (var id in order)
                sb.AppendLine($"    \"{id}\",");
            sb.AppendLine("]");
            File.WriteAllText(path, sb.ToString());
        }

        private static string ToKebabCase(string name)
        {
            var sb = new StringBuilder(name.Length + 4);
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
