using CsvHelper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UMB.CLI.Views;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using Sma5h.Mods.Music.Services;
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
        /// <summary>Series dB boost to write to series.toml; null leaves it unchanged.</summary>
        public double? SeriesDb { get; set; }
    }

    public class VolumeOverride
    {
        public int OriginalIndex { get; set; }
        public float Db { get; set; }
    }

    public class VolumePreviewBatchInput
    {
        public string SeriesPath { get; set; }
        public string Filename { get; set; }
        public string OutputPath { get; set; }
    }

    public class VolumeRowDto
    {
        public int OriginalIndex { get; set; }
        public string Title { get; set; }
        public string Filename { get; set; }
        public bool HasMeasurement { get; set; }
        public float MeasuredLufs { get; set; }
        public float AutoGainDb { get; set; }
        public bool WasClamped { get; set; }
        public float TrackDb { get; set; }
        /// <summary>nus3bank volume (dB) of the track with no boosts; the boosts are added to it.</summary>
        public float BankBaseDb { get; set; } = MusicConstants.DEFAULT_NUS3BANK_VOLUME_DB;
        /// <summary>A vanilla song: no auto-gain, and the global and series boosts only apply with ApplyVolumeToVanilla.</summary>
        public bool IsVanilla { get; set; }
    }

    public class VolumeAnalyzeResultDto
    {
        public string SeriesName { get; set; }
        public float GlobalDb { get; set; }
        public double SeriesDb { get; set; }
        public float TargetLufs { get; set; }
        public float MaxGainDb { get; set; }
        public bool ApplyVolumeToVanilla { get; set; }
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
        private readonly Lazy<Dictionary<string, float>> _vanillaBankDb;

        public VolumeConfigService(
            IOptionsMonitor<Sma5hMusicOptions> musicConfig,
            ILufsAnalysisService lufsService,
            IAudioDecodeService decodeService,
            VanillaCatalogService catalog,
            ILogger<VolumeConfigService> logger)
        {
            _musicConfig = musicConfig;
            _lufsService = lufsService;
            _decodeService = decodeService;
            _catalog = catalog;
            _logger = logger;
            _vanillaBankDb = new(() => AudioStateService.ReadCoreNus3BankVolumes(musicConfig.CurrentValue.ResourcesPath));
        }

        private string VanillaToneId(string bgmId) => _catalog.Get()?.Songs.FirstOrDefault(s => s.BgmId == bgmId)?.ToneId;

        /// <summary>A row's audio file: in the series folder, or the game's own for a vanilla song ("" when unknown).</summary>
        private string SourcePath(string seriesDir, string filename)
        {
            if (!FolderMusicMod.IsVanillaBgmId(filename)) return Path.Combine(seriesDir, filename);
            return VanillaToneId(filename) is { } toneId
                ? Path.Combine(_musicConfig.CurrentValue.GameResourcesPath, "stream", "sound", "bgm", string.Format(MusicConstants.GameResources.NUS3AUDIO_FILE, toneId))
                : "";
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

            // The window has no series control, so the series boost is folded into the global one.
            var globalDb = _musicConfig.CurrentValue.Sma5hMusic.GlobalVolumeDb + (float)SeriesToml.ReadVolumeDb(seriesDir);
            var lufsOpts = _musicConfig.CurrentValue.Sma5hMusic.LufsNormalization ?? new();
            var target = lufsOpts.TargetLufs;
            var maxGainDb = lufsOpts.MaxGainDb;

            if (!_lufsService.IsAvailable)
            {
                _logger.LogWarning("FFmpeg is not available — auto-gain values cannot be calculated. You can still edit per-song overrides, but they will not be informed by measurement.");
            }

            var viewModels = new VolumeRowViewModel[rows.Count];
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start($"Analyzing {rows.Count} track(s)...", ctx =>
                {
                    Parallel.For(0, rows.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
                    {
                        var dto = AnalyzeRow(rows[i], i, seriesDir, target, maxGainDb, useCacheOnly: false);
                        viewModels[i] = new VolumeRowViewModel
                        {
                            OriginalIndex = dto.OriginalIndex,
                            Title = dto.Title,
                            Filename = dto.Filename,
                            SourcePath = string.IsNullOrEmpty(dto.Filename) ? "" : Path.Combine(seriesDir, dto.Filename),
                            TrackDb = dto.TrackDb,
                            GlobalDb = globalDb,
                            MeasuredLufs = dto.MeasuredLufs,
                            AutoGainDb = dto.AutoGainDb,
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

            if (!headers.Contains(TracksCsv.VolumeDbColumn))
                headers = headers.Append(TracksCsv.VolumeDbColumn).ToArray();

            foreach (var vm in result)
            {
                if (vm.OriginalIndex < 0 || vm.OriginalIndex >= rows.Count) continue;
                rows[vm.OriginalIndex][TracksCsv.VolumeDbColumn] = CliUtil.FormatDb(vm.TrackDb);
            }

            WriteCsvRows(csvPath, rows, headers);
            _logger.LogInformation("Volume overrides saved to {Path}.", csvPath);
        }

        /// <summary>
        /// Per-row LUFS measurement + auto-gain shared by the interactive window and
        /// the batch path. useCacheOnly reads cached measurements without re-analysis.
        /// </summary>
        private VolumeRowDto AnalyzeRow(Dictionary<string, string> row, int index, string seriesDir,
            float target, float maxGainDb, bool useCacheOnly)
        {
            var filename = row.GetValueOrDefault("filename", "");
            var title = row.GetValueOrDefault("title", filename);
            var sourcePath = string.IsNullOrEmpty(filename) ? "" : SourcePath(seriesDir, filename);

            var dto = new VolumeRowDto
            {
                OriginalIndex = index,
                Title = title,
                Filename = filename,
                TrackDb = ParseDb(row.GetValueOrDefault(TracksCsv.VolumeDbColumn, "")),
            };

            // Vanilla songs are boosted by hand only, from their own bank volume.
            if (FolderMusicMod.IsVanillaBgmId(filename))
            {
                dto.IsVanilla = true;
                if (VanillaToneId(filename) is { } toneId && _vanillaBankDb.Value.TryGetValue(toneId, out var bankDb))
                    dto.BankBaseDb = bankDb;
            }
            else if (!string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath))
            {
                var measurement = useCacheOnly ? _lufsService.MeasureCached(sourcePath) : _lufsService.Measure(sourcePath);
                if (measurement.IsValid)
                {
                    var gain = _lufsService.CalculateGain(measurement, target, maxGainDb);
                    dto.MeasuredLufs = measurement.IntegratedLufs;
                    dto.AutoGainDb = gain.Db;
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

            VanillaTracks.Backfill(seriesDir, _catalog.Get());
            var (rows, _) = ReadCsvRows(csvPath);

            var lufsOpts = _musicConfig.CurrentValue.Sma5hMusic.LufsNormalization ?? new();
            var target = lufsOpts.TargetLufs;
            var maxGainDb = lufsOpts.MaxGainDb;
            var lufsCacheName = string.IsNullOrWhiteSpace(lufsOpts.LufsCacheFileName) ? "LUFS.csv" : lufsOpts.LufsCacheFileName;
            var lufsCacheExists = File.Exists(Path.Combine(seriesDir, lufsCacheName));

            if (input.Analyze && !_lufsService.IsAvailable)
                _logger.LogWarning("FFmpeg is not available — auto-gain values cannot be calculated. Overrides can still be edited.");

            var dtos = new VolumeRowDto[rows.Count];
            var completed = 0;
            Parallel.For(0, rows.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
            {
                var dto = AnalyzeRow(rows[i], i, seriesDir, target, maxGainDb, useCacheOnly: !input.Analyze);
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
                GlobalDb = _musicConfig.CurrentValue.Sma5hMusic.GlobalVolumeDb,
                SeriesDb = SeriesToml.ReadVolumeDb(seriesDir),
                TargetLufs = target,
                MaxGainDb = maxGainDb,
                ApplyVolumeToVanilla = _musicConfig.CurrentValue.Sma5hMusic.ApplyVolumeToVanilla,
                FfmpegAvailable = _lufsService.IsAvailable,
                LufsCacheExists = lufsCacheExists,
                Items = dtos.ToList(),
            });
            _logger.LogInformation("Volume analysis written for {Count} track(s).", dtos.Length);
        }

        /// <summary>
        /// Non-interactive save for the desktop app. Reads
        /// { "seriesPath": "...", "overrides": [{ "originalIndex": 0, "db": 1.5 }] }
        /// and persists each track boost into the tracks.csv `volume_db` column, plus the optional
        /// seriesDb into series.toml.
        /// </summary>
        public void RunSaveBatch(string jsonPath)
        {
            var input = ReadBatchInput<VolumeSaveBatchInput>(jsonPath, "config-volume-save");
            if (input == null || string.IsNullOrWhiteSpace(input.SeriesPath))
            {
                _logger.LogError("Invalid input: seriesPath is required.");
                return;
            }

            if (input.SeriesDb is { } seriesDb)
                SeriesToml.WriteVolumeDb(input.SeriesPath, seriesDb);

            var csvPath = Path.Combine(input.SeriesPath, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
            if (!File.Exists(csvPath))
            {
                _logger.LogWarning("No tracks.csv found in {SeriesDir}.", input.SeriesPath);
                return;
            }

            var (rows, headers) = ReadCsvRows(csvPath);
            if (!headers.Contains(TracksCsv.VolumeDbColumn))
                headers = headers.Append(TracksCsv.VolumeDbColumn).ToArray();

            foreach (var ov in input.Overrides ?? new List<VolumeOverride>())
            {
                if (ov.OriginalIndex < 0 || ov.OriginalIndex >= rows.Count) continue;
                rows[ov.OriginalIndex][TracksCsv.VolumeDbColumn] = CliUtil.FormatDb(ov.Db);
            }

            WriteCsvRows(csvPath, rows, headers);
            _logger.LogInformation("Track dB boosts saved to {Path}.", csvPath);
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

            var sourcePath = SourcePath(input.SeriesPath, input.Filename);
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

        /// <summary>Sets every series and track dB boost in every mod to 0.</summary>
        public void ResetAllVolumes()
        {
            var changed = 0;
            foreach (var seriesDir in ModPaths.VisibleDirs(_musicConfig.CurrentValue.Sma5hMusic.ModPath).SelectMany(ModPaths.VisibleDirs))
            {
                if (SeriesToml.ReadVolumeDb(seriesDir) != 0)
                    SeriesToml.WriteVolumeDb(seriesDir, 0);

                var csvPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
                if (!File.Exists(csvPath)) continue;
                var (rows, headers) = ReadCsvRows(csvPath);
                var rowsToReset = headers.Contains(TracksCsv.VolumeDbColumn) ? rows.Where(r => ParseDb(r[TracksCsv.VolumeDbColumn]) != 0f).ToList() : new();
                if (rowsToReset.Count == 0) continue;
                rowsToReset.ForEach(r => r[TracksCsv.VolumeDbColumn] = CliUtil.FormatDb(0));
                WriteCsvRows(csvPath, rows, headers);
                changed += rowsToReset.Count;
            }
            _logger.LogInformation("Reset {Count} track dB boost(s) to 0.", changed);
        }

        internal static float ParseDb(string raw) =>
            float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;


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
