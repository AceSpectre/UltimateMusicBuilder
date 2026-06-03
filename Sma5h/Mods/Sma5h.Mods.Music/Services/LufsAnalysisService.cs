using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Sma5h.Helpers;
using Sma5h.Interfaces;
using Sma5h.Mods.Music.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Sma5h.Mods.Music.Services
{
    public class LufsAnalysisService : ILufsAnalysisService
    {
        private readonly ILogger<ILufsAnalysisService> _logger;
        private readonly IProcessService _processService;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _config;
        private readonly IServiceProvider _services; // lazy resolve IAudioDecodeService to avoid DI cycle
        private IAudioDecodeService _decoder;

        // Per-directory caches keyed by absolute directory path. Each directory's LUFS.csv
        // is loaded lazily on first access to any file in that directory and rewritten
        // atomically by SaveCache() at the end of a run.
        private readonly ConcurrentDictionary<string, DirectoryCache> _dirCaches =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly object _ffmpegResolveLock = new();
        private string _resolvedFfmpegPath;
        private bool _ffmpegResolved;
        private bool _ffmpegMissingLogged;

        public LufsAnalysisService(IOptionsMonitor<Sma5hMusicOptions> config, IProcessService processService, IServiceProvider services, ILogger<ILufsAnalysisService> logger)
        {
            _config = config;
            _processService = processService;
            _services = services;
            _logger = logger;
        }

        private IAudioDecodeService Decoder
        {
            // AudioDecodeService depends on ILufsAnalysisService (for the resolved FFmpeg path),
            // so we lazy-resolve it here to avoid a constructor-time cycle.
            get => _decoder ??= (IAudioDecodeService)_services.GetService(typeof(IAudioDecodeService));
        }

        public bool IsAvailable => ResolvedFfmpegPath != null;

        public string ResolvedFfmpegPath
        {
            get
            {
                if (_ffmpegResolved) return _resolvedFfmpegPath;
                lock (_ffmpegResolveLock)
                {
                    if (_ffmpegResolved) return _resolvedFfmpegPath;
                    _resolvedFfmpegPath = ResolveFfmpeg();
                    _ffmpegResolved = true;
                    if (_resolvedFfmpegPath == null && !_ffmpegMissingLogged)
                    {
                        _logger.LogWarning("FFmpeg not found on PATH. LUFS normalization will be skipped. Install via: choco install ffmpeg (Windows) / brew install ffmpeg (macOS) / apt install ffmpeg (Linux).");
                        _ffmpegMissingLogged = true;
                    }
                    else if (_resolvedFfmpegPath != null)
                    {
                        _logger.LogInformation("Using FFmpeg at {Path}", _resolvedFfmpegPath);
                    }
                    return _resolvedFfmpegPath;
                }
            }
        }

        private string ResolveFfmpeg()
        {
            // ffmpeg must be on the system PATH. Install via:
            //   choco install ffmpeg   (Windows)
            //   brew install ffmpeg    (macOS)
            //   apt install ffmpeg     (Debian/Ubuntu)
            return ToolPathResolver.Resolve(null, null, "ffmpeg");
        }

        public LufsMeasurement Measure(string audioFilePath) => MeasureInternal(audioFilePath, allowFfmpeg: true);

        public LufsMeasurement MeasureCached(string audioFilePath) => MeasureInternal(audioFilePath, allowFfmpeg: false);

        private LufsMeasurement MeasureInternal(string audioFilePath, bool allowFfmpeg)
        {
            if (!File.Exists(audioFilePath))
            {
                _logger.LogWarning("LUFS measurement requested for missing file {Path}.", audioFilePath);
                return InvalidMeasurement();
            }

            var fi = new FileInfo(audioFilePath);
            var dir = Path.GetFullPath(fi.DirectoryName);
            var filename = fi.Name;
            var fileSize = fi.Length;
            var mtime = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeSeconds();

            var dirCache = _dirCaches.GetOrAdd(dir, d => new DirectoryCache(d, GetCsvFileName()));
            dirCache.EnsureLoaded(_logger);

            var lufsOpts = _config.CurrentValue.Sma5hMusic?.LufsNormalization;
            var currentTarget = lufsOpts?.TargetLufs ?? -14f;
            var maxMult = lufsOpts?.MaxGainMultiplier ?? 4f;

            // Cache hit: size + mtime match.
            if (dirCache.TryGet(filename, out var entry)
                && entry.FileSize == fileSize
                && entry.MtimeUnix == mtime)
            {
                // Target drift: recompute multiplier from the (still-valid) measured LUFS.
                // No ffmpeg needed.
                if (Math.Abs(entry.TargetLufs - currentTarget) > 0.001f)
                {
                    var (newMult, _) = ComputeMultiplier(entry.MeasuredLufs, currentTarget, maxMult);
                    dirCache.UpdateDerived(filename, newMult, currentTarget, _logger);
                }
                return new LufsMeasurement
                {
                    IntegratedLufs = entry.MeasuredLufs,
                    IsValid = true
                };
            }

            // Cache miss: caller may have opted out of FFmpeg (read-only load).
            if (!allowFfmpeg) return InvalidMeasurement();

            // Cache miss: need ffmpeg.
            if (!IsAvailable) return InvalidMeasurement();

            var measurement = RunFfmpegLoudnorm(audioFilePath);
            if (measurement.IsValid)
            {
                var (mult, _) = ComputeMultiplier(measurement.IntegratedLufs, currentTarget, maxMult);
                dirCache.Upsert(new LufsCacheEntry
                {
                    Filename = filename,
                    MeasuredLufs = measurement.IntegratedLufs,
                    Multiplier = mult,
                    TargetLufs = currentTarget,
                    FileSize = fileSize,
                    MtimeUnix = mtime
                }, _logger);
            }
            return measurement;
        }

        public GainResult CalculateGain(LufsMeasurement measurement, float targetLufs, float maxMultiplier)
        {
            if (measurement == null || !measurement.IsValid)
                return new GainResult(1.0f, false);
            var (mult, clamped) = ComputeMultiplier(measurement.IntegratedLufs, targetLufs, maxMultiplier);
            return new GainResult(mult, clamped);
        }

        // linear_gain = 10^((target - measured) / 20), clamped to [0, max].
        private static (float multiplier, bool wasClamped) ComputeMultiplier(float measuredLufs, float targetLufs, float maxMultiplier)
        {
            var deltaDb = targetLufs - measuredLufs;
            var raw = (float)Math.Pow(10.0, deltaDb / 20.0);
            if (raw <= 0 || float.IsNaN(raw) || float.IsInfinity(raw))
                return (1.0f, false);
            if (maxMultiplier > 0 && raw > maxMultiplier)
                return (maxMultiplier, true);
            return (raw, false);
        }

        public void SaveCache()
        {
            foreach (var kv in _dirCaches.ToArray())
            {
                try { kv.Value.SaveIfDirty(_logger); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to save LUFS cache for directory {Dir}.", kv.Key); }
            }
        }

        private string GetCsvFileName()
        {
            var name = _config.CurrentValue.Sma5hMusic?.LufsNormalization?.LufsCacheFileName;
            return string.IsNullOrWhiteSpace(name) ? "LUFS.csv" : name;
        }

        private LufsMeasurement RunFfmpegLoudnorm(string audioFilePath)
        {
            var ffmpegPath = ResolvedFfmpegPath;
            if (ffmpegPath == null) return InvalidMeasurement();

            // FFmpeg can't read Smash container formats (.nus3audio) and won't reliably read
            // .idsp/.lopus/.brstm either. Decode those to a temp WAV first.
            var ext = Path.GetExtension(audioFilePath).ToLowerInvariant();
            var needsDecode = ext != ".wav" && ext != ".mp3" && ext != ".ogg" && ext != ".flac" && ext != ".m4a" && ext != ".aac";
            string analysisFile = audioFilePath;
            string tempWav = null;
            if (needsDecode)
            {
                var decoder = Decoder;
                if (decoder == null)
                {
                    _logger.LogWarning("No audio decoder available; cannot analyze {File}.", audioFilePath);
                    return InvalidMeasurement();
                }
                var tempBase = _config.CurrentValue.TempPath ?? "Temp";
                Directory.CreateDirectory(tempBase);
                tempWav = Path.Combine(tempBase, $"lufs_{Guid.NewGuid():N}.wav");
                if (!decoder.DecodeToWav(audioFilePath, tempWav))
                {
                    _logger.LogWarning("Could not decode {File} for LUFS analysis. Skipping normalization for this track.", audioFilePath);
                    SafeDelete(tempWav);
                    return InvalidMeasurement();
                }
                analysisFile = tempWav;
            }

            var args = $"-hide_banner -nostats -i \"{analysisFile}\" -af loudnorm=I=-14:print_format=json -f null -";
            var stderr = new StringBuilder();

            try
            {
                _processService.RunProcess(ffmpegPath, args,
                    standardRedirect: null,
                    errorRedirect: (_, data) => { if (data?.Data != null) stderr.AppendLine(data.Data); });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FFmpeg loudnorm invocation failed for {File}.", audioFilePath);
                SafeDelete(tempWav);
                return InvalidMeasurement();
            }

            var result = ParseLoudnormJson(stderr.ToString(), audioFilePath);
            SafeDelete(tempWav);
            return result;
        }

        private static void SafeDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
        }

        private LufsMeasurement ParseLoudnormJson(string ffmpegStderr, string audioFilePath)
        {
            var start = ffmpegStderr.LastIndexOf('{');
            var end = ffmpegStderr.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                _logger.LogWarning("Could not locate loudnorm JSON block for {File}. Skipping normalization.", audioFilePath);
                return InvalidMeasurement();
            }

            var jsonBlock = ffmpegStderr.Substring(start, end - start + 1);
            try
            {
                var obj = JObject.Parse(jsonBlock);
                var integrated = ParseFloat(obj["input_i"]);
                var truePeak = ParseFloat(obj["input_tp"]);
                var lra = ParseFloat(obj["input_lra"]);
                if (float.IsNaN(integrated))
                {
                    _logger.LogWarning("loudnorm returned non-numeric input_i for {File}.", audioFilePath);
                    return InvalidMeasurement();
                }
                return new LufsMeasurement
                {
                    IntegratedLufs = integrated,
                    TruePeakDb = float.IsNaN(truePeak) ? 0f : truePeak,
                    LoudnessRangeLu = float.IsNaN(lra) ? 0f : lra,
                    IsValid = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse loudnorm JSON for {File}.", audioFilePath);
                return InvalidMeasurement();
            }
        }

        private static float ParseFloat(JToken token)
        {
            if (token == null) return float.NaN;
            var s = token.ToString();
            if (string.IsNullOrWhiteSpace(s) || s.Equals("-inf", StringComparison.OrdinalIgnoreCase))
                return float.NaN;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : float.NaN;
        }

        private static LufsMeasurement InvalidMeasurement() => new() { IsValid = false };
    }

    // CSV row schema as documented in the plan:
    //   filename,measured_lufs,multiplier,target_lufs,file_size,mtime_unix
    public class LufsCacheEntry
    {
        public string Filename { get; set; }
        public float MeasuredLufs { get; set; }
        public float Multiplier { get; set; }
        public float TargetLufs { get; set; }
        public long FileSize { get; set; }
        public long MtimeUnix { get; set; }
    }

    internal sealed class LufsCacheEntryMap : ClassMap<LufsCacheEntry>
    {
        public LufsCacheEntryMap()
        {
            Map(m => m.Filename).Name("filename");
            Map(m => m.MeasuredLufs).Name("measured_lufs");
            Map(m => m.Multiplier).Name("multiplier");
            Map(m => m.TargetLufs).Name("target_lufs");
            Map(m => m.FileSize).Name("file_size");
            Map(m => m.MtimeUnix).Name("mtime_unix");
        }
    }

    // One per audio-file directory. Holds the parsed LUFS.csv rows for that directory,
    // tracks dirty state, and writes back atomically. Thread-safe.
    internal sealed class DirectoryCache
    {
        private readonly string _directory;
        private readonly string _csvFileName;
        private readonly Dictionary<string, LufsCacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();
        private bool _loaded;
        private bool _dirty;

        public DirectoryCache(string directory, string csvFileName)
        {
            _directory = directory;
            _csvFileName = csvFileName;
        }

        public void EnsureLoaded(ILogger logger)
        {
            if (_loaded) return;
            lock (_lock)
            {
                if (_loaded) return;
                _loaded = true;
                var csvPath = Path.Combine(_directory, _csvFileName);
                if (!File.Exists(csvPath)) return;
                try
                {
                    using var reader = new StreamReader(csvPath);
                    using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                    {
                        HasHeaderRecord = true,
                        MissingFieldFound = null,
                        BadDataFound = null,
                    });
                    csv.Context.RegisterClassMap<LufsCacheEntryMap>();
                    foreach (var entry in csv.GetRecords<LufsCacheEntry>())
                    {
                        if (entry != null && !string.IsNullOrEmpty(entry.Filename))
                            _entries[entry.Filename] = entry;
                    }
                    logger.LogDebug("Loaded {Count} LUFS entries from {Path}.", _entries.Count, csvPath);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to load LUFS cache at {Path}; treating as empty.", csvPath);
                }
            }
        }

        public bool TryGet(string filename, out LufsCacheEntry entry)
        {
            lock (_lock)
            {
                return _entries.TryGetValue(filename, out entry);
            }
        }

        // Upsert + inline persistence: write the row, then flush LUFS.csv before returning.
        // Two reasons we save synchronously inside the lock:
        //   1. Durability — if the build is interrupted mid-pre-warm, measurements so far survive.
        //   2. Correctness — serializing the save under the lock avoids two threads racing on
        //      the same .tmp path. CSV writes are tiny (few KB), so the contention is cheap.
        public void Upsert(LufsCacheEntry entry, ILogger logger)
        {
            lock (_lock)
            {
                _entries[entry.Filename] = entry;
                _dirty = true;
                FlushLocked(logger);
            }
        }

        // Update only the derived fields (multiplier, target_lufs) without recomputing measurement.
        // Used when TargetLufs config changes — the underlying measured_lufs is still valid.
        public void UpdateDerived(string filename, float newMultiplier, float newTargetLufs, ILogger logger)
        {
            lock (_lock)
            {
                if (!_entries.TryGetValue(filename, out var existing)) return;
                existing.Multiplier = newMultiplier;
                existing.TargetLufs = newTargetLufs;
                _dirty = true;
                FlushLocked(logger);
            }
        }

        // End-of-run flush; kept for callers (SaveCache) but most writes now happen inline.
        public void SaveIfDirty(ILogger logger)
        {
            lock (_lock) { FlushLocked(logger); }
        }

        private void FlushLocked(ILogger logger)
        {
            if (!_dirty) return;
            var snapshot = _entries.Values
                .OrderBy(e => e.Filename, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var csvPath = Path.Combine(_directory, _csvFileName);
            var tempPath = csvPath + ".tmp";
            try
            {
                Directory.CreateDirectory(_directory);
                using (var writer = new StreamWriter(tempPath))
                using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                {
                    csv.Context.RegisterClassMap<LufsCacheEntryMap>();
                    csv.WriteRecords(snapshot);
                }
                File.Move(tempPath, csvPath, overwrite: true);
                _dirty = false;
                logger.LogDebug("Wrote LUFS cache {Path} ({Count} rows).", csvPath, snapshot.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to write LUFS cache {Path}.", csvPath);
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }
    }
}
