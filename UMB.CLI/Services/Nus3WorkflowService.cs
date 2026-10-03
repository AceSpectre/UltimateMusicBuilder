using Sma5h.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using UMB.CLI.Desktop;

namespace UMB.CLI.Services
{
    /// <summary>
    /// The desktop app's nus3 convert workflow: source listing, loop analysis (cached in
    /// songs-to-validate/.analysis-cache.json), waveform and loop previews, and per-track
    /// conversion records (.conversions.json).
    /// </summary>
    public class Nus3WorkflowService
    {
        private const int ConversionSampleRate = 48000;

        // Used when the default pass finds nothing: a short minimum loop without pruning surfaces
        // short loops (e.g. boss themes) that pymusiclooper's default 0.35 duration multiplier rejects.
        private static readonly LoopAnalysisOptions RelaxedOptions = new() { MinLoopDuration = 2, DisablePruning = true };

        private readonly Nus3ConvertService _converter;

        public Nus3WorkflowService(Nus3ConvertService converter)
        {
            _converter = converter;
        }

        /// <summary>Source audio files in the series that haven't been accepted yet.</summary>
        public List<Nus3SourceTrack> ListSources(string seriesPath)
        {
            var seriesDir = Path.GetFullPath(seriesPath);
            if (!Directory.Exists(seriesDir)) return new List<Nus3SourceTrack>();

            return Directory.GetFiles(seriesDir)
                .Where(f => CliUtil.SourceAudioExtensions.Contains(Path.GetExtension(f)))
                .Select(Path.GetFileName)
                // Already accepted into the series folder (sibling .nus3audio): done, hide it.
                .Where(name => !File.Exists(Path.Combine(seriesDir, Path.GetFileNameWithoutExtension(name) + ".nus3audio")))
                .Select(name => new Nus3SourceTrack(name, PrettifyName(name), name, "—", 0, File.Exists(Nus3PathFor(seriesDir, name))))
                .ToList();
        }

        public Dictionary<string, Nus3ConversionMeta> LoadConversions(string seriesPath) =>
            ReadJson<Dictionary<string, Nus3ConversionMeta>>(ConversionsPath(Path.GetFullPath(seriesPath)));

        public Nus3AnalysisResult AnalyzeLoopPoints(string filePath, LoopAnalysisOptions options)
        {
            var (seriesDir, filename) = Track(filePath);

            // Only a cached result that actually has loops is reused, so an empty result gets retried.
            if (options.Force != true && CacheEntryFor(seriesDir, filename) is { Candidates.Count: > 0 } cached)
                return new Nus3AnalysisResult(SourceTrack(seriesDir, filename, cached.Duration, cached.DurationSeconds), cached.Candidates);

            var (sampleRate, duration) = AudioTools.Probe(filePath);
            var track = SourceTrack(seriesDir, filename, duration > 0 ? FormatDuration(duration) : "0:00", duration);

            var candidates = FindCandidates(filePath, sampleRate, options);
            var explicitOptions = options.MinLoopDuration != null || options.MinDurationMultiplier != null || options.DisablePruning != null;
            if (candidates.Count == 0 && !explicitOptions)
                candidates = FindCandidates(filePath, sampleRate, RelaxedOptions);

            UpdateCacheEntry(seriesDir, filename, e =>
            {
                e.Duration = track.Duration;
                e.DurationSeconds = track.DurationSeconds;
                e.Candidates = candidates;
            });
            return new Nus3AnalysisResult(track, candidates);
        }

        /// <summary>Track length in seconds from the analysis cache, or ffprobe.</summary>
        public double GetTrackDuration(string filePath)
        {
            var (seriesDir, filename) = Track(filePath);
            if (CacheEntryFor(seriesDir, filename) is { DurationSeconds: > 0 } cached)
                return cached.DurationSeconds;

            var duration = AudioTools.Probe(filePath).Duration;
            if (duration > 0)
                UpdateCacheEntry(seriesDir, filename, e => { e.Duration = FormatDuration(duration); e.DurationSeconds = duration; });
            return duration;
        }

        /// <summary><paramref name="bars"/> normalised peak levels across the track (cached).</summary>
        public List<double> ExtractWaveformPeaks(string filePath, int bars)
        {
            var (seriesDir, filename) = Track(filePath);
            if (CacheEntryFor(seriesDir, filename) is { Peaks.Count: > 0 } cached)
                return cached.Peaks;

            var tempPcm = Path.Combine(Path.GetTempPath(), $"umb-peaks-{Guid.NewGuid():N}.raw");
            try
            {
                var duration = AudioTools.Probe(filePath).Duration;
                if (duration <= 0) return new List<double>();

                // Resample so the whole track is roughly one sample per bar.
                ProcessRunner.Run(AudioTools.Tool("ffmpeg"),
                    $"-i \"{filePath}\" -ac 1 -ar {(int)Math.Ceiling(bars / duration)} -f s16le -acodec pcm_s16le \"{tempPcm}\" -y");
                var pcm = File.Exists(tempPcm) ? File.ReadAllBytes(tempPcm) : Array.Empty<byte>();
                var levels = Enumerable.Range(0, pcm.Length / 2).Select(i => Math.Abs((int)BitConverter.ToInt16(pcm, i * 2))).ToList();
                var max = Math.Max(1, levels.DefaultIfEmpty(0).Max());

                var peaks = levels.Take(bars).Select(v => (double)v / max).ToList();
                peaks.AddRange(Enumerable.Repeat(0.0, bars - peaks.Count));

                UpdateCacheEntry(seriesDir, filename, e => e.Peaks = peaks);
                return peaks;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or System.ComponentModel.Win32Exception)
            {
                return new List<double>();
            }
            finally
            {
                TryDelete(tempPcm);
            }
        }

        /// <summary>
        /// The loop seam as a WAV data URL, or null. With no loop chosen (end ≤ start) it previews
        /// the song's end running into its start.
        /// </summary>
        public string GenerateLoopPreview(string filePath, double loopStartSec, double loopEndSec, double previewLength)
        {
            var outputWav = Path.Combine(Path.GetTempPath(), $"umb-preview-{Guid.NewGuid():N}.wav");
            try
            {
                if (loopEndSec <= loopStartSec)
                {
                    loopStartSec = 0;
                    loopEndSec = Math.Max(0, AudioTools.Probe(filePath).Duration);
                    if (loopEndSec <= 0) return null;
                }

                return AudioTools.RenderLoopPreview(filePath, loopStartSec, loopEndSec, previewLength / 2, outputWav)
                    ? "data:audio/wav;base64," + Convert.ToBase64String(File.ReadAllBytes(outputWav))
                    : null;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or System.ComponentModel.Win32Exception)
            {
                return null;
            }
            finally
            {
                TryDelete(outputWav);
            }
        }

        /// <summary>
        /// Converts one track into songs-to-validate and records its loop decision so the
        /// converted state survives a restart. True when the .nus3audio was produced.
        /// </summary>
        public bool ConvertTrack(string seriesPath, string trackId, string mode, LoopCandidate candidate)
        {
            var seriesDir = Path.GetFullPath(seriesPath);
            _converter.ConvertBatch(new Nus3BatchInput
            {
                SeriesPath = seriesDir,
                Decisions = new List<Nus3BatchDecision>
                {
                    new()
                    {
                        Filename = trackId,
                        Mode = mode,
                        LoopStartSamples = candidate == null ? 0 : (long)Math.Round(candidate.LoopStart * ConversionSampleRate),
                        LoopEndSamples = candidate == null ? 0 : (long)Math.Round(candidate.LoopEnd * ConversionSampleRate)
                    }
                }
            });

            if (!File.Exists(Nus3PathFor(seriesDir, trackId)))
                return false;
            var conversions = LoadConversions(seriesDir);
            conversions[trackId] = new Nus3ConversionMeta { Mode = mode, Candidate = candidate };
            WriteJson(ConversionsPath(seriesDir), conversions);
            return true;
        }

        /// <summary>Removes a converted track from songs-to-validate and forgets its decision.</summary>
        public void RejectTrack(string seriesPath, string trackId)
        {
            var seriesDir = Path.GetFullPath(seriesPath);
            TryDelete(Nus3PathFor(seriesDir, trackId));
            var conversions = LoadConversions(seriesDir);
            if (conversions.Remove(trackId))
                WriteJson(ConversionsPath(seriesDir), conversions);
        }

        private static List<LoopCandidate> FindCandidates(string filePath, int sampleRate, LoopAnalysisOptions options)
        {
            var loops = AudioTools.FindLoops(filePath, options);
            var rate = sampleRate > 0 ? sampleRate : ConversionSampleRate;
            return loops.Select((loop, i) =>
            {
                var start = (double)loop.Start / rate;
                var end = (double)loop.End / rate;
                return new LoopCandidate
                {
                    Rank = i + 1,
                    Score = Math.Round(loop.Score * 1000) / 10,
                    LoopStart = Math.Round(start * 10) / 10,
                    LoopEnd = Math.Round(end * 10) / 10,
                    LoopLength = Math.Round((end - start) * 10) / 10,
                    LoopStartStr = FormatTimestamp(start),
                    LoopEndStr = FormatTimestamp(end),
                    LoopLengthStr = FormatTimestamp(end - start),
                    NoteDistance = Math.Round(loop.NoteDistance * 10000) / 10000,
                    SpectralSim = Math.Clamp(1 - loop.NoteDistance, 0, 1),
                    RmsDelta = Math.Round(loop.LoudnessDiff * 10) / 10,
                    Seam = loop.LoudnessDiff > 3.0 ? "click" : loop.LoudnessDiff > 2.0 ? "audible" : loop.LoudnessDiff > 1.0 ? "good" : "smooth",
                    Note = i == 0 ? "best match" : ""
                };
            }).ToList();
        }

        private (string seriesDir, string filename) Track(string filePath)
        {
            var path = Path.GetFullPath(filePath);
            return (Path.GetDirectoryName(path), Path.GetFileName(path));
        }

        private Nus3SourceTrack SourceTrack(string seriesDir, string filename, string duration, double durationSeconds) =>
            new(filename, PrettifyName(filename), filename, duration, durationSeconds, File.Exists(Nus3PathFor(seriesDir, filename)));

        // ── Analysis cache ─────────────────────────────────────────────────────

        private class CacheEntry
        {
            public double MtimeMs { get; set; }
            public long Size { get; set; }
            public string Duration { get; set; } = "0:00";
            public double DurationSeconds { get; set; }
            public List<LoopCandidate> Candidates { get; set; }
            public List<double> Peaks { get; set; }
        }

        /// <summary>The cached entry, unless the source file changed since it was written.</summary>
        private static CacheEntry CacheEntryFor(string seriesDir, string filename)
        {
            if (!ReadJson<Dictionary<string, CacheEntry>>(CachePath(seriesDir)).TryGetValue(filename, out var entry))
                return null;
            var source = new FileInfo(Path.Combine(seriesDir, filename));
            if (!source.Exists) return entry;
            return MtimeMs(source) == entry.MtimeMs && source.Length == entry.Size ? entry : null;
        }

        private static void UpdateCacheEntry(string seriesDir, string filename, Action<CacheEntry> update)
        {
            var cache = ReadJson<Dictionary<string, CacheEntry>>(CachePath(seriesDir));
            var entry = cache.GetValueOrDefault(filename) ?? new CacheEntry();
            update(entry);
            var source = new FileInfo(Path.Combine(seriesDir, filename));
            if (source.Exists)
            {
                entry.MtimeMs = MtimeMs(source);
                entry.Size = source.Length;
            }
            cache[filename] = entry;
            WriteJson(CachePath(seriesDir), cache);
        }

        private static double MtimeMs(FileInfo file) =>
            (file.LastWriteTimeUtc - DateTime.UnixEpoch).TotalMilliseconds;

        // ── Files ──────────────────────────────────────────────────────────────

        private static string ValidateDir(string seriesDir) => Path.Combine(seriesDir, CliUtil.ValidateFolder);
        private static string CachePath(string seriesDir) => Path.Combine(ValidateDir(seriesDir), ".analysis-cache.json");
        private static string ConversionsPath(string seriesDir) => Path.Combine(ValidateDir(seriesDir), ".conversions.json");

        private static string Nus3PathFor(string seriesDir, string trackId) =>
            Path.Combine(ValidateDir(seriesDir), Path.GetFileNameWithoutExtension(trackId) + ".nus3audio");

        private static T ReadJson<T>(string path) where T : new()
        {
            try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), DesktopApi.Json) ?? new T() : new T(); }
            catch (Exception ex) when (ex is JsonException or IOException) { return new T(); }
        }

        /// <summary>Best effort: these files are caches/records, so a failed write is not an error.</summary>
        private static void WriteJson(string path, object value)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonSerializer.Serialize(value, DesktopApi.Json));
            }
            catch (IOException) { }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
        }

        // ── Formatting ─────────────────────────────────────────────────────────

        private static string PrettifyName(string filename) =>
            Regex.Replace(Path.GetFileNameWithoutExtension(filename).Replace('-', ' ').Replace('_', ' '), @"\b\w", m => m.Value.ToUpperInvariant());

        private static string FormatDuration(double seconds) =>
            $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}";

        private static string FormatTimestamp(double seconds) =>
            $"{(int)(seconds / 60)}:{(seconds % 60).ToString("0.0", CultureInfo.InvariantCulture).PadLeft(4, '0')}";
    }
}
