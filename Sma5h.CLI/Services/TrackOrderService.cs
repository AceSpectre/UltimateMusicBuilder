using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h.CLI.Views;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace Sma5h.CLI.Services
{
    public class TrackOrderService
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;

        public TrackOrderService(IOptionsMonitor<Sma5hMusicOptions> musicConfig,
            ILogger<TrackOrderService> logger)
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

            var csvPath = Path.Combine(seriesDir,
                MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
            if (!File.Exists(csvPath))
            {
                _logger.LogWarning("No tracks.csv found in {SeriesDir}.", seriesDir);
                return;
            }

            // Read all rows as dynamic records to preserve every column
            List<Dictionary<string, string>> rows;
            string[] headers;
            try
            {
                (rows, headers) = ReadCsvRows(csvPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse {Path}.", csvPath);
                return;
            }

            if (rows.Count == 0)
            {
                _logger.LogWarning("No tracks found in {Path}.", csvPath);
                return;
            }

            // Sort by existing order column if present
            if (headers.Contains("order"))
            {
                rows = rows.OrderBy(r =>
                {
                    if (r.TryGetValue("order", out var val) && int.TryParse(val, out var o))
                        return o;
                    return int.MaxValue;
                }).ToList();
            }

            // Build view models
            var viewModels = new List<TrackViewModel>();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var title = row.GetValueOrDefault("title", row.GetValueOrDefault("filename", $"Track {i}"));
                var game = row.GetValueOrDefault("game", "");
                var filename = row.GetValueOrDefault("filename", "");
                var subtitle = string.IsNullOrEmpty(game) ? filename : $"{game} — {filename}";

                viewModels.Add(new TrackViewModel
                {
                    OriginalIndex = i,
                    Title = title,
                    Subtitle = subtitle,
                });
            }

            // Launch Avalonia window on STA thread
            List<int> result = null;
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

                    var window = new TrackOrderWindow(viewModels);
                    window.Title = $"Order Tracks — {Path.GetFileName(seriesDir)}";
                    window.Closed += (_, _) => result = window.Result;
                    lifetime.MainWindow = window;
                    lifetime.Start(Array.Empty<string>());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to launch track order window.");
                }
            });
            if (OperatingSystem.IsWindows())
                thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            // Save result
            if (result != null)
            {
                // Reorder rows and assign new order values
                var reorderedRows = result.Select(i => rows[i]).ToList();

                // Ensure order column exists in headers
                if (!headers.Contains("order"))
                    headers = headers.Append("order").ToArray();

                for (int i = 0; i < reorderedRows.Count; i++)
                    reorderedRows[i]["order"] = i.ToString();

                WriteCsvRows(csvPath, reorderedRows, headers);
                _logger.LogInformation("Track order saved to {Path}.", csvPath);
            }
            else
            {
                _logger.LogInformation("Track ordering cancelled.");
            }
        }

        private (List<Dictionary<string, string>> rows, string[] headers) ReadCsvRows(string csvPath)
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                TrimOptions = TrimOptions.Trim,
                MissingFieldFound = null,
                BadDataFound = null,
            };

            using var reader = new StreamReader(csvPath);
            using var csv = new CsvReader(reader, config);
            csv.Read();
            csv.ReadHeader();
            var headers = csv.HeaderRecord;

            var rows = new List<Dictionary<string, string>>();
            while (csv.Read())
            {
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var h in headers)
                    dict[h] = csv.GetField(h) ?? "";
                rows.Add(dict);
            }

            return (rows, headers);
        }

        private void WriteCsvRows(string csvPath, List<Dictionary<string, string>> rows, string[] headers)
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
            };

            using var writer = new StreamWriter(csvPath);
            using var csv = new CsvWriter(writer, config);

            // Write header
            foreach (var h in headers)
                csv.WriteField(h);
            csv.NextRecord();

            // Write rows
            foreach (var row in rows)
            {
                foreach (var h in headers)
                    csv.WriteField(row.GetValueOrDefault(h, ""));
                csv.NextRecord();
            }
        }
    }
}
