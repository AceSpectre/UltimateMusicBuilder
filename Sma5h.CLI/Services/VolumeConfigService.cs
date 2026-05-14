using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h.CLI.Views;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sma5h.CLI.Services
{
    public class VolumeConfigService
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;
        private readonly ILufsAnalysisService _lufsService;
        private readonly IAudioDecodeService _decodeService;

        public VolumeConfigService(
            IOptionsMonitor<Sma5hMusicOptions> musicConfig,
            ILufsAnalysisService lufsService,
            IAudioDecodeService decodeService,
            ILogger<VolumeConfigService> logger)
        {
            _musicConfig = musicConfig;
            _lufsService = lufsService;
            _decodeService = decodeService;
            _logger = logger;
        }

        public void Run()
        {
            Script.PrintBanner(_logger);

            var (modDir, seriesDir) = Script.PromptModAndSeries(_musicConfig, _logger);
            if (modDir == null || seriesDir == null)
                return;

            var csvPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
            if (!File.Exists(csvPath))
            {
                _logger.LogWarning("No tracks.csv found in {SeriesDir}.", seriesDir);
                return;
            }

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

            var lufsOpts = _musicConfig.CurrentValue.Sma5hMusic.LufsNormalization;
            var target = lufsOpts?.TargetLufs ?? -14.0f;
            var maxMult = lufsOpts?.MaxGainMultiplier ?? 4.0f;

            if (!_lufsService.IsAvailable)
            {
                _logger.LogWarning("FFmpeg is not available — auto-gain values cannot be calculated. You can still edit per-song overrides, but they will not be informed by measurement.");
            }

            // Build view models with LUFS analysis (parallelized with progress bar)
            var viewModels = new VolumeRowViewModel[rows.Count];
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start($"Analyzing {rows.Count} track(s)...", ctx =>
                {
                    Parallel.For(0, rows.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
                    {
                        var row = rows[i];
                        var filename = row.GetValueOrDefault("filename", "");
                        var title = row.GetValueOrDefault("title", filename);
                        var sourcePath = string.IsNullOrEmpty(filename) ? "" : Path.Combine(seriesDir, filename);
                        var userOverride = ParseVolume(row.GetValueOrDefault("volume", "1.0"));

                        var vm = new VolumeRowViewModel
                        {
                            OriginalIndex = i,
                            Title = title,
                            Filename = filename,
                            SourcePath = sourcePath,
                            UserOverride = userOverride,
                            TargetLufs = target,
                            MaxMultiplier = maxMult,
                        };

                        if (!string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath))
                        {
                            var measurement = _lufsService.Measure(sourcePath);
                            if (measurement.IsValid)
                            {
                                var gain = _lufsService.CalculateGain(measurement, target, maxMult);
                                vm.MeasuredLufs = measurement.IntegratedLufs;
                                vm.AutoGain = gain.Multiplier;
                                vm.WasClamped = gain.WasClamped;
                                vm.HasMeasurement = true;
                            }
                            else
                            {
                                vm.AutoGain = 1.0f;
                            }
                        }
                        else
                        {
                            vm.AutoGain = 1.0f;
                            _logger.LogWarning("Source file missing for row {Index}: {Path}", i, sourcePath);
                        }

                        viewModels[i] = vm;
                    });
                });

            // Persist any new measurements
            _lufsService.SaveCache();

            var tempDir = _musicConfig.CurrentValue.TempPath ?? "Temp";

            List<VolumeRowViewModel> result = null;
            try
            {
                result = AvaloniaHost.ShowWindow(
                    () => new VolumeConfigWindow(viewModels.ToList(), new AudioPreviewDecoder(_decodeService, tempDir, _logger))
                    {
                        Title = $"Config Volume — {Path.GetFileName(seriesDir)}"
                    },
                    w => w.Result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to launch config volume window.");
                return;
            }

            if (result == null)
            {
                _logger.LogInformation("Volume configuration cancelled.");
                return;
            }

            // Persist UserOverride back into tracks.csv `volume` column
            if (!headers.Contains("volume"))
                headers = headers.Append("volume").ToArray();

            foreach (var vm in result)
            {
                if (vm.OriginalIndex < 0 || vm.OriginalIndex >= rows.Count) continue;
                rows[vm.OriginalIndex]["volume"] = vm.UserOverride.ToString("0.###", CultureInfo.InvariantCulture);
            }

            WriteCsvRows(csvPath, rows, headers);
            _logger.LogInformation("Volume overrides saved to {Path}.", csvPath);
        }

        private static float ParseVolume(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 1.0f;
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 1.0f;
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

            foreach (var h in headers)
                csv.WriteField(h);
            csv.NextRecord();

            foreach (var row in rows)
            {
                foreach (var h in headers)
                    csv.WriteField(row.GetValueOrDefault(h, ""));
                csv.NextRecord();
            }
        }
    }

    /// <summary>
    /// On-demand audio decoder used by the volume preview window to convert
    /// arbitrary source formats (NUS3AUDIO, IDSP, LOPUS, BRSTM, etc.) to a
    /// temp WAV that NAudio can play. Caches per source path within a single
    /// window session.
    /// </summary>
    public class AudioPreviewDecoder
    {
        private readonly IAudioDecodeService _decodeService;
        private readonly string _tempDir;
        private readonly ILogger _logger;
        private readonly Dictionary<string, string> _decodedPaths = new();
        private readonly object _lock = new();

        public AudioPreviewDecoder(IAudioDecodeService decodeService, string tempDir, ILogger logger)
        {
            _decodeService = decodeService;
            _tempDir = tempDir;
            _logger = logger;
        }

        /// <summary>
        /// Returns a path to a WAV file the caller can play. May be the source itself
        /// (for .wav inputs), a cached decode, or a freshly-decoded temp file.
        /// Returns null if decoding fails.
        /// </summary>
        public string EnsureWav(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                return null;
            if (Path.GetExtension(sourcePath).Equals(".wav", StringComparison.OrdinalIgnoreCase))
                return sourcePath;

            lock (_lock)
            {
                if (_decodedPaths.TryGetValue(sourcePath, out var cached) && File.Exists(cached))
                    return cached;

                try
                {
                    Directory.CreateDirectory(_tempDir);
                    var safeName = MakeSafeFileName(Path.GetFileNameWithoutExtension(sourcePath));
                    var outPath = Path.Combine(_tempDir, $"preview_{safeName}_{Guid.NewGuid():N}.wav");
                    if (_decodeService.DecodeToWav(sourcePath, outPath))
                    {
                        _decodedPaths[sourcePath] = outPath;
                        return outPath;
                    }
                    return null;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to decode {File} for preview.", Path.GetFileName(sourcePath));
                    return null;
                }
            }
        }

        public void Cleanup()
        {
            lock (_lock)
            {
                foreach (var path in _decodedPaths.Values)
                {
                    try { if (File.Exists(path)) File.Delete(path); }
                    catch (Exception ex) { _logger.LogDebug(ex, "Failed to delete temp preview {Path}.", path); }
                }
                _decodedPaths.Clear();
            }
        }

        private static string MakeSafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(invalid.Contains(c) ? '_' : c);
            return sb.ToString();
        }
    }
}
