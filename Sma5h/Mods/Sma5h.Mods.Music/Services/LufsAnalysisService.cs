using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sma5h.Interfaces;
using Sma5h.Mods.Music.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
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
        private readonly ConcurrentDictionary<string, LufsMeasurement> _cache = new();
        private bool _cacheLoaded;
        private bool _cacheDirty;
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
                        var configured = _config.CurrentValue.Sma5hMusic?.LufsNormalization?.FfmpegPath;
                        _logger.LogWarning("FFmpeg not found (looked at {Path} and on PATH). LUFS normalization will be skipped. Install FFmpeg system-wide or place ffmpeg.exe at the configured path.", configured);
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
            var configured = _config.CurrentValue.Sma5hMusic?.LufsNormalization?.FfmpegPath;
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
                return Path.GetFullPath(configured);

            // Fall back to PATH lookup. On Windows ProcessStartInfo with UseShellExecute=false
            // resolves bare executable names via PATH, but we want to verify it actually
            // exists before declaring availability so callers get a clean IsAvailable signal.
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return null;

            var exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    var candidate = Path.Combine(dir, exeName);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch { /* malformed PATH entry — skip */ }
            }
            return null;
        }

        public LufsMeasurement Measure(string audioFilePath)
        {
            if (!File.Exists(audioFilePath))
            {
                _logger.LogWarning("LUFS measurement requested for missing file {Path}.", audioFilePath);
                return InvalidMeasurement();
            }

            EnsureCacheLoaded();

            var hash = ComputeFileHash(audioFilePath);
            if (_cache.TryGetValue(hash, out var cached) && cached.IsValid)
            {
                _logger.LogDebug("LUFS cache hit for {File} (hash {Hash}).", Path.GetFileName(audioFilePath), hash[..Math.Min(12, hash.Length)]);
                return cached;
            }

            if (!IsAvailable)
                return InvalidMeasurement();

            var measurement = RunFfmpegLoudnorm(audioFilePath);
            if (measurement.IsValid)
            {
                measurement.SourceHash = hash;
                _cache[hash] = measurement;
                _cacheDirty = true;
            }
            return measurement;
        }

        public GainResult CalculateGain(LufsMeasurement measurement, float targetLufs, float maxMultiplier)
        {
            if (measurement == null || !measurement.IsValid)
                return new GainResult(1.0f, false);

            // LUFS is logarithmic. linear_gain = 10^((target - measured) / 20)
            var deltaDb = targetLufs - measurement.IntegratedLufs;
            var raw = (float)Math.Pow(10.0, deltaDb / 20.0);
            if (raw <= 0 || float.IsNaN(raw) || float.IsInfinity(raw))
                return new GainResult(1.0f, false);

            if (maxMultiplier > 0 && raw > maxMultiplier)
                return new GainResult(maxMultiplier, true);
            return new GainResult(raw, false);
        }

        public void SaveCache()
        {
            if (!_cacheDirty) return;
            var cachePath = _config.CurrentValue.Sma5hMusic?.LufsNormalization?.MeasurementCacheFile;
            if (string.IsNullOrEmpty(cachePath)) return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(cachePath)));
                var snapshot = new Dictionary<string, LufsMeasurement>(_cache);
                File.WriteAllText(cachePath, JsonConvert.SerializeObject(snapshot, Formatting.Indented));
                _cacheDirty = false;
                _logger.LogDebug("Saved {Count} LUFS measurements to {Path}.", snapshot.Count, cachePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save LUFS cache to {Path}.", cachePath);
            }
        }

        private void EnsureCacheLoaded()
        {
            if (_cacheLoaded) return;
            lock (_cache)
            {
                if (_cacheLoaded) return;
                _cacheLoaded = true;
                var cachePath = _config.CurrentValue.Sma5hMusic?.LufsNormalization?.MeasurementCacheFile;
                if (string.IsNullOrEmpty(cachePath) || !File.Exists(cachePath))
                    return;
                try
                {
                    var json = File.ReadAllText(cachePath);
                    var loaded = JsonConvert.DeserializeObject<Dictionary<string, LufsMeasurement>>(json);
                    if (loaded != null)
                    {
                        foreach (var kv in loaded)
                            if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                                _cache[kv.Key] = kv.Value;
                        _logger.LogDebug("Loaded {Count} LUFS measurements from cache {Path}.", loaded.Count, cachePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load LUFS cache from {Path}; ignoring.", cachePath);
                }
            }
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

            // loudnorm in analysis (single-pass) mode prints a JSON block to stderr.
            // -hide_banner suppresses FFmpeg's version banner. -nostats suppresses per-frame progress.
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
            // Locate the JSON object emitted by loudnorm. It's the last `{` ... `}` block in the stderr.
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

        private static string ComputeFileHash(string path)
        {
            // Hash the entire file. nus3audio/idsp/wav files are typically <10MB so this is cheap.
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            var bytes = sha.ComputeHash(stream);
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.AppendFormat("{0:x2}", b);
            return sb.ToString();
        }

        private static LufsMeasurement InvalidMeasurement() => new() { IsValid = false };
    }
}
