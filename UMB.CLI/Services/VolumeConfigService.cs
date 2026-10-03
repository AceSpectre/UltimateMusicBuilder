using CsvHelper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UMB.CLI.Views;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Models;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks;

namespace UMB.CLI.Services
{
    public class VolumeAnalyzeBatchInput
    {
        public string SeriesPath { get; set; }
        public string OutputPath { get; set; }
        /// <summary>
        /// When true, run FFmpeg LUFS analysis for any track not already cached.
        /// When false (default), load existing cached measurements only and never
        /// invoke FFmpeg — a fast, interruption-free read used when the view opens.
        /// </summary>
        public bool Analyze { get; set; }
    }

    public class VolumeSaveBatchInput
    {
        public string SeriesPath { get; set; }
        public List<VolumeOverride> Overrides { get; set; }
    }

    public class VolumeOverride
    {
        public string BgmId { get; set; }
        public int OriginalIndex { get; set; }
        public float Volume { get; set; }
    }

    public class VolumePreviewBatchInput
    {
        public string SeriesPath { get; set; }
        public string Filename { get; set; }
        public string OutputPath { get; set; }
    }

    public class VolumeRowDto
    {
        public string BgmId { get; set; }
        public int OriginalIndex { get; set; }
        public string Title { get; set; }
        public string Filename { get; set; }
        public bool HasVolumeOverride { get; set; }
        public bool HasMeasurement { get; set; }
        public float MeasuredLufs { get; set; }
        public float AutoGain { get; set; }
        public bool WasClamped { get; set; }
        public float UserOverride { get; set; }
    }

    public class VolumeAnalyzeResultDto
    {
        public string SeriesName { get; set; }
        public float GlobalVolumeMultiplier { get; set; }
        public float TargetLufs { get; set; }
        public float MaxMultiplier { get; set; }
        public bool FfmpegAvailable { get; set; }
        /// <summary>True if a LUFS cache file already exists for the series directory.</summary>
        public bool LufsCacheExists { get; set; }
        public List<VolumeRowDto> Items { get; set; } = new();
    }

    public class VolumeConfigService
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;
        private readonly ILufsAnalysisService _lufsService;
        private readonly IAudioDecodeService _decodeService;
        private readonly VanillaCatalogService _catalog;

        public VolumeConfigService(
            IOptionsMonitor<Sma5hMusicOptions> musicConfig,
            ILufsAnalysisService lufsService,
            IAudioDecodeService decodeService,
            ILogger<VolumeConfigService> logger, VanillaCatalogService catalog = null)
        {
            _catalog = catalog;
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

            rows.AddRange(VanillaRows(seriesDir).Select(r => (Dictionary<string, string>)r));
            if (rows.Count == 0)
            {
                _logger.LogWarning("No tracks found in {Path}.", csvPath);
                return;
            }

            var globalMult = _musicConfig.CurrentValue.Sma5hMusic.GlobalVolumeMultiplier;
            var lufsOpts = _musicConfig.CurrentValue.Sma5hMusic.LufsNormalization;
            var target = lufsOpts?.TargetLufs ?? -14.0f;
            var maxMult = lufsOpts?.MaxGainMultiplier ?? 4.0f;

            if (!_lufsService.IsAvailable)
            {
                _logger.LogWarning("FFmpeg is not available — auto-gain values cannot be calculated. You can still edit per-song overrides, but they will not be informed by measurement.");
            }

            var dtoByIndex = new Dictionary<int, VolumeRowDto>();
            var viewModels = new VolumeRowViewModel[rows.Count];
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start($"Analyzing {rows.Count} track(s)...", ctx =>
                {
                    Parallel.For(0, rows.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
                    {
                        var dto = AnalyzeRow(rows[i], i, seriesDir, target, maxMult, useCacheOnly: false);
                        lock (dtoByIndex) dtoByIndex[dto.OriginalIndex] = dto;
                        viewModels[i] = new VolumeRowViewModel
                        {
                            OriginalIndex = dto.OriginalIndex,
                            Title = dto.Title,
                            Filename = dto.Filename,
                            SourcePath = string.IsNullOrEmpty(dto.Filename) ? "" : Path.Combine(seriesDir, dto.Filename),
                            UserOverride = dto.UserOverride,
                            GlobalVolumeMultiplier = globalMult,
                            MeasuredLufs = dto.MeasuredLufs,
                            AutoGain = dto.AutoGain,
                            WasClamped = dto.WasClamped,
                            HasMeasurement = dto.HasMeasurement,
                        };
                    });
                });

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

            SaveOverrides(seriesDir, result.Select(vm => new VolumeOverride
            {
                OriginalIndex = vm.OriginalIndex, BgmId = dtoByIndex[vm.OriginalIndex].BgmId, Volume = vm.UserOverride
            }).ToList());
            _logger.LogInformation("Volume overrides saved to {Path}.", csvPath);
        }

        /// <summary>
        /// Per-row LUFS measurement + auto-gain shared by the interactive window and
        /// the batch path. useCacheOnly reads cached measurements without re-analysis.
        /// </summary>
        private VolumeRowDto AnalyzeRow(Dictionary<string, string> row, int index, string seriesDir,
            float target, float maxMult, bool useCacheOnly)
        {
            var filename = row.GetValueOrDefault("filename", "");
            var title = row.GetValueOrDefault("title", filename);
            var sourcePath = string.IsNullOrEmpty(filename) ? "" : Path.Combine(seriesDir, filename);

            var dto = new VolumeRowDto
            {
                BgmId = row.GetValueOrDefault("bgm_id"),
                HasVolumeOverride = row.GetValueOrDefault("has_volume_override", "True") == "True",
                OriginalIndex = row.ContainsKey("vanilla_index") ? int.Parse(row["vanilla_index"], CultureInfo.InvariantCulture) : index,
                Title = title,
                Filename = filename,
                UserOverride = ParseVolume(row.GetValueOrDefault("volume", "1.0")),
                AutoGain = ParseVolume(row.GetValueOrDefault("base_volume", "1")),
            };

            if (!string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath))
            {
                var measurement = useCacheOnly ? _lufsService.MeasureCached(sourcePath) : _lufsService.Measure(sourcePath);
                if (measurement.IsValid)
                {
                    var gain = _lufsService.CalculateGain(measurement, target, maxMult);
                    dto.MeasuredLufs = measurement.IntegratedLufs;
                    dto.AutoGain = gain.Multiplier;
                    dto.WasClamped = gain.WasClamped;
                    dto.HasMeasurement = true;
                }
            }
            else
            {
                _logger.LogWarning("Source file missing for row {Index}: {Path}", index, sourcePath);
            }

            return dto;
        }

        /// <summary>
        /// Non-interactive analysis for the desktop app. Reads
        /// { "seriesPath": "...", "outputPath": "..." }, measures every track's
        /// loudness + auto-gain (same as the interactive window) and writes a
        /// VolumeAnalyzeResultDto JSON to outputPath for the renderer to render.
        /// </summary>
        public void RunAnalyzeBatch(string jsonPath)
        {
            var input = ReadBatchInput<VolumeAnalyzeBatchInput>(jsonPath, "config-volume-analyze");
            if (input == null || string.IsNullOrWhiteSpace(input.SeriesPath) || string.IsNullOrWhiteSpace(input.OutputPath))
            {
                _logger.LogError("Invalid input: seriesPath and outputPath are required.");
                return;
            }

            var seriesDir = input.SeriesPath;
            var csvPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
            if (!File.Exists(csvPath))
            {
                _logger.LogWarning("No tracks.csv found in {SeriesDir}.", seriesDir);
                WriteAnalyzeResult(input.OutputPath, new VolumeAnalyzeResultDto { SeriesName = Path.GetFileName(seriesDir) });
                return;
            }

            var (rows, _) = ReadCsvRows(csvPath);
            rows.AddRange(VanillaRows(seriesDir).Select(r => (Dictionary<string, string>)r));

            var globalMult = _musicConfig.CurrentValue.Sma5hMusic.GlobalVolumeMultiplier;
            var lufsOpts = _musicConfig.CurrentValue.Sma5hMusic.LufsNormalization;
            var target = lufsOpts?.TargetLufs ?? -14.0f;
            var maxMult = lufsOpts?.MaxGainMultiplier ?? 4.0f;
            var lufsCacheName = string.IsNullOrWhiteSpace(lufsOpts?.LufsCacheFileName) ? "LUFS.csv" : lufsOpts.LufsCacheFileName;
            var lufsCacheExists = File.Exists(Path.Combine(seriesDir, lufsCacheName));

            if (input.Analyze && !_lufsService.IsAvailable)
                _logger.LogWarning("FFmpeg is not available — auto-gain values cannot be calculated. Overrides can still be edited.");

            var dtos = new VolumeRowDto[rows.Count];
            var completed = 0;
            Parallel.For(0, rows.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
            {
                var dto = AnalyzeRow(rows[i], i, seriesDir, target, maxMult, useCacheOnly: !input.Analyze);
                dtos[i] = dto;

                if (input.Analyze)
                {
                    var done = Interlocked.Increment(ref completed);
                    CliOutput.WriteLine($"__LUFS_PROGRESS__\t{done}\t{rows.Count}\t{dto.Filename}");
                }
            });

            // Only the analysis path can mutate the cache; a read-only load writes nothing.
            if (input.Analyze)
            {
                _lufsService.SaveCache();
                lufsCacheExists = File.Exists(Path.Combine(seriesDir, lufsCacheName));
            }

            WriteAnalyzeResult(input.OutputPath, new VolumeAnalyzeResultDto
            {
                SeriesName = Path.GetFileName(seriesDir),
                GlobalVolumeMultiplier = globalMult,
                TargetLufs = target,
                MaxMultiplier = maxMult,
                FfmpegAvailable = _lufsService.IsAvailable,
                LufsCacheExists = lufsCacheExists,
                Items = dtos.ToList(),
            });
            _logger.LogInformation("Volume analysis written for {Count} track(s).", dtos.Length);
        }

        /// <summary>
        /// Non-interactive save for the desktop app. Reads
        /// { "seriesPath": "...", "overrides": [{ "originalIndex": 0, "volume": 1.2 }] }
        /// and persists each override into the tracks.csv `volume` column.
        /// </summary>
        public void RunSaveBatch(string jsonPath)
        {
            var input = ReadBatchInput<VolumeSaveBatchInput>(jsonPath, "config-volume-save");
            if (input == null || string.IsNullOrWhiteSpace(input.SeriesPath))
            {
                _logger.LogError("Invalid input: seriesPath is required.");
                return;
            }

            var csvPath = Path.Combine(input.SeriesPath, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
            if (!File.Exists(csvPath))
            {
                _logger.LogWarning("No tracks.csv found in {SeriesDir}.", input.SeriesPath);
                return;
            }

            SaveOverrides(input.SeriesPath, input.Overrides ?? new());
            _logger.LogInformation("Volume overrides saved to {Path}.", csvPath);
        }

        /// <summary>
        /// Decodes a single track to a PCM WAV at the requested path so the desktop
        /// renderer can preview it (through a Web Audio gain node) at the exact
        /// post-build loudness. Reads { "seriesPath", "filename", "outputPath" }.
        /// </summary>
        public void RunPreviewBatch(string jsonPath)
        {
            var input = ReadBatchInput<VolumePreviewBatchInput>(jsonPath, "config-volume-preview");
            if (input == null || string.IsNullOrWhiteSpace(input.SeriesPath)
                || string.IsNullOrWhiteSpace(input.Filename) || string.IsNullOrWhiteSpace(input.OutputPath))
            {
                _logger.LogError("Invalid input: seriesPath, filename and outputPath are required.");
                return;
            }

            var sourcePath = Path.IsPathRooted(input.Filename)
                ? VanillaRows(input.SeriesPath).SingleOrDefault(r => r.Get("filename") == input.Filename)?.Get("filename")
                : Path.Combine(input.SeriesPath, input.Filename);
            if (!File.Exists(sourcePath))
            {
                _logger.LogError("Source file not found: {Path}", sourcePath);
                return;
            }

            if (_decodeService.DecodeToWav(sourcePath, input.OutputPath))
                _logger.LogInformation("Decoded preview: {Path}", input.OutputPath);
            else
                _logger.LogError("Failed to decode '{File}' for preview.", input.Filename);
        }

        private List<CsvRow> VanillaRows(string seriesPath)
        {
            var tomlPath = Path.Combine(seriesPath, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
            if (!File.Exists(tomlPath)) return new();
            var series = SeriesToml.ReadHeader(File.ReadAllText(tomlPath));
            if (!series.ExistingSeries) return new();
            var saved = VanillaSongOverride.Read(seriesPath).ToDictionary(o => o.BgmId);
            return (_catalog?.Get()?.Songs ?? new()).Where(s => s.SeriesId == MusicConstants.InternalIds.SERIES_ID_PREFIX + series.Id)
                .Select((song, index) => new CsvRow
                {
                    ["bgm_id"] = song.BgmId, ["vanilla_index"] = (-index - 1).ToString(CultureInfo.InvariantCulture),
                    ["filename"] = song.AudioPath, ["title"] = saved.GetValueOrDefault(song.BgmId)?.Title ?? song.Name,
                    ["volume"] = (saved.GetValueOrDefault(song.BgmId)?.Volume ?? 1).ToString(CultureInfo.InvariantCulture),
                    ["has_volume_override"] = (saved.GetValueOrDefault(song.BgmId)?.Volume != null).ToString(),
                    ["base_volume"] = song.BaseVolume.ToString(CultureInfo.InvariantCulture)
                }).ToList();
        }

        private void SaveOverrides(string seriesPath, List<VolumeOverride> input)
        {
            if (input.Any(o => !float.IsFinite(o.Volume) || o.Volume < 0))
                throw new InvalidDataException("Volume must be a finite non-negative multiplier.");
            var csvPath = Path.Combine(seriesPath, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
            var (rows, headers) = ReadCsvRows(csvPath);
            if (!headers.Contains("volume")) headers = headers.Append("volume").ToArray();
            var vanilla = VanillaRows(seriesPath).Select(r => r.Get("bgm_id")).ToHashSet();
            var saved = VanillaSongOverride.Read(seriesPath).ToDictionary(o => o.BgmId);
            foreach (var ov in input)
            {
                if (!string.IsNullOrEmpty(ov.BgmId))
                {
                    if (!vanilla.Contains(ov.BgmId)) throw new InvalidDataException("Invalid vanilla song for this series.");
                    if (!saved.TryGetValue(ov.BgmId, out var song)) saved[ov.BgmId] = song = new() { BgmId = ov.BgmId };
                    // An explicit 1 also opts this vanilla song into build-time normalization.
                    song.Volume = ov.Volume;
                }
                else if (ov.OriginalIndex >= 0 && ov.OriginalIndex < rows.Count)
                    rows[ov.OriginalIndex]["volume"] = ov.Volume.ToString("0.###", CultureInfo.InvariantCulture);
            }
            WriteCsvRows(csvPath, rows, headers);
            if (saved.Count > 0) VanillaSongOverride.Write(seriesPath, saved.Values);
        }

        private T ReadBatchInput<T>(string jsonPath, string command) where T : class
        {
            if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
            {
                _logger.LogError("Usage: dotnet run {Command} <input.json>", command);
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(
                    File.ReadAllText(jsonPath),
                    CliUtil.JsonCaseInsensitive);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse {Path}.", jsonPath);
                return null;
            }
        }

        private void WriteAnalyzeResult(string outputPath, VolumeAnalyzeResultDto result)
        {
            var json = JsonSerializer.Serialize(result,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            File.WriteAllText(outputPath, json);
        }

        internal static float ParseVolume(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 1.0f;
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 1.0f;
        }

        private (List<Dictionary<string, string>> rows, string[] headers) ReadCsvRows(string csvPath)
        {
            var config = CliUtil.CsvReadLenient();

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
            var config = CliUtil.CsvWrite();

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
                    var safeName = CliUtil.MakeSafeFileName(Path.GetFileNameWithoutExtension(sourcePath));
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

    }
}
