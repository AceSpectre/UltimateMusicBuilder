using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using VGAudio.Cli;

namespace Sma5h.CLI.Services
{
    public class Nus3ConvertService
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;

        private static readonly HashSet<string> SOURCE_AUDIO_EXTENSIONS = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".wav", ".ogg"
        };

        private const string VALIDATE_FOLDER = "songs-to-validate";

        public Nus3ConvertService(IOptionsMonitor<Sma5hMusicOptions> musicConfig, ILogger<Nus3ConvertService> logger)
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

            // Find source audio files that aren't already game formats
            var sourceFiles = Directory.GetFiles(seriesDir)
                .Where(f => SOURCE_AUDIO_EXTENSIONS.Contains(Path.GetExtension(f)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (sourceFiles.Count == 0)
            {
                _logger.LogWarning("No source audio files (.mp3, .flac, .wav, .ogg) found in {Dir}.", seriesDir);
                return;
            }

            var loopScoreThreshold = (double)AnsiConsole.Prompt(
                new TextPrompt<float>("Minimum loop score (only increase if subpar loops are being accepted):")
                    .DefaultValue(94.5f)) / 100.0;

            var validateDir = Path.Combine(seriesDir, VALIDATE_FOLDER);
            Directory.CreateDirectory(validateDir);

            var tempDir = Path.Combine(_musicConfig.CurrentValue.TempPath, "nus3convert");
            Directory.CreateDirectory(tempDir);

            var nus3AudioExe = Path.Combine(_musicConfig.CurrentValue.ToolsPath, MusicConstants.Resources.NUS3AUDIO_EXE_FILE);
            if (!File.Exists(nus3AudioExe))
            {
                _logger.LogError("nus3audio.exe not found at {Path}.", nus3AudioExe);
                return;
            }

            int converted = 0;
            int goodLoops = 0;
            int fullLoops = 0;

            foreach (var sourceFile in sourceFiles)
            {
                var basename = Path.GetFileNameWithoutExtension(sourceFile);
                var outputNus3 = Path.Combine(validateDir, basename + ".nus3audio");

                if (File.Exists(outputNus3))
                {
                    _logger.LogInformation("Skipping '{Basename}': already exists in songs-to-validate.", basename);
                    continue;
                }

                _logger.LogInformation("Processing '{Basename}'...", basename);

                // Step 1: Detect loop points via pymusiclooper
                var loopCandidates = RunPymusiclooper(sourceFile);
                var sourceSampleRate = GetSourceSampleRate(sourceFile);
                long loopStart, loopEnd;
                bool isFullSongLoop;

                if (loopCandidates.Count > 0 && loopCandidates.Any(c => c.score >= loopScoreThreshold))
                {
                    // Build selection choices from all candidates with full info
                    var choices = new List<string>();
                    for (int i = 0; i < loopCandidates.Count; i++)
                    {
                        var c = loopCandidates[i];
                        var startTime = sourceSampleRate > 0 ? TimeSpan.FromSeconds((double)c.loopStart / sourceSampleRate).ToString(@"mm\:ss\.ff") : "??";
                        var endTime = sourceSampleRate > 0 ? TimeSpan.FromSeconds((double)c.loopEnd / sourceSampleRate).ToString(@"mm\:ss\.ff") : "??";
                        choices.Add($"Score: {c.score:P1}  Start: {startTime} ({c.loopStart})  End: {endTime} ({c.loopEnd})  NoteDist: {c.noteDistance:F4}  LoudnessDiff: {c.loudnessDiff:F4} dB");
                    }
                    choices.Add("Reject all (use full-song loop)");

                    var selection = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title($"Loop candidates for '{Markup.Escape(basename)}':")
                            .HighlightStyle(new Style(Color.Cyan1))
                            .AddChoices(choices));

                    var selectedIndex = choices.IndexOf(selection);
                    if (selectedIndex < loopCandidates.Count)
                    {
                        var selected = loopCandidates[selectedIndex];
                        loopStart = selected.loopStart;
                        loopEnd = selected.loopEnd;
                        isFullSongLoop = false;
                        _logger.LogInformation("  Selected loop: {Start}-{End} (score: {Score:P1})", loopStart, loopEnd, selected.score);
                        goodLoops++;
                    }
                    else
                    {
                        loopStart = 0;
                        loopEnd = 0; // will be set from WAV after conversion
                        isFullSongLoop = true;
                        fullLoops++;
                    }
                }
                else
                {
                    // No candidates above threshold — auto-reject
                    loopStart = 0;
                    loopEnd = 0; // will be set from WAV after conversion
                    isFullSongLoop = true;
                    var bestScore = loopCandidates.Count > 0 ? loopCandidates[0].score : 0;
                    _logger.LogInformation("  No candidates above threshold (best: {Score:P1}), using full-song loop.", bestScore);
                    fullLoops++;
                }

                // Step 2: Convert to WAV at 48kHz if needed
                var wavFile = sourceFile;
                bool tempWav = false;
                if (!sourceFile.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    wavFile = Path.Combine(tempDir, basename + ".wav");
                    if (!RunFfmpeg(sourceFile, wavFile))
                    {
                        _logger.LogError("  ffmpeg conversion failed for '{Basename}', skipping.", basename);
                        continue;
                    }
                    tempWav = true;
                }

                // For full-song loops, get exact sample count from the converted WAV
                if (isFullSongLoop)
                {
                    var wavSamples = GetWavSampleCount(wavFile);
                    if (wavSamples <= 0)
                    {
                        _logger.LogError("  Could not determine sample count for '{Basename}', skipping.", basename);
                        if (tempWav && File.Exists(wavFile)) File.Delete(wavFile);
                        continue;
                    }
                    loopEnd = wavSamples - 1;
                    _logger.LogInformation("  Full-song loop: 0-{End}", loopEnd);
                }

                // Step 3: Convert WAV → lopus via VGAudioCli library
                var lopusFile = Path.Combine(tempDir, basename + ".lopus");
                try
                {
                    var oldOut = Console.Out;
                    using (var writer = new StringWriter())
                    {
                        Console.SetOut(writer);
                        Converter.RunConverterCli(new string[]
                        {
                            "-i", wavFile,
                            "-o", lopusFile,
                            "--opusheader", "Namco",
                            "--cbr",
                            "-l", $"{loopStart}-{loopEnd}"
                        });
                    }
                    Console.SetOut(oldOut);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "  VGAudioCli conversion failed for '{Basename}'.", basename);
                    continue;
                }

                if (!File.Exists(lopusFile) || new FileInfo(lopusFile).Length == 0)
                {
                    _logger.LogError("  VGAudioCli produced no output for '{Basename}', skipping.", basename);
                    continue;
                }

                // Step 4: Wrap lopus → nus3audio
                var toneId = DeriveToneId(basename);
                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = nus3AudioExe,
                            Arguments = $"-n -w \"{outputNus3}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    process.WaitForExit();

                    process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = nus3AudioExe,
                            Arguments = $"-A {toneId} \"{lopusFile}\" -w \"{outputNus3}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    process.WaitForExit();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "  nus3audio.exe wrapping failed for '{Basename}'.", basename);
                    continue;
                }

                if (File.Exists(outputNus3) && new FileInfo(outputNus3).Length > 0)
                {
                    _logger.LogInformation("  → {OutputPath}", outputNus3);
                    converted++;

                    // Generate loop preview clip if a loop was selected
                    if (!isFullSongLoop)
                    {
                        var loopsDir = Path.Combine(validateDir, "loops");
                        Directory.CreateDirectory(loopsDir);
                        var previewPath = Path.Combine(loopsDir, basename + "_loop.wav");
                        CreateLoopPreview(sourceFile, loopStart, loopEnd, previewPath);
                    }
                }
                else
                {
                    _logger.LogError("  nus3audio output was empty for '{Basename}'.", basename);
                }

                // Clean up temp files
                if (tempWav && File.Exists(wavFile))
                    File.Delete(wavFile);
                if (File.Exists(lopusFile))
                    File.Delete(lopusFile);
            }

            // Clean up temp dir
            try { Directory.Delete(tempDir, recursive: false); } catch { }

            _logger.LogInformation("--------------------");
            _logger.LogInformation("Nus3 conversion complete: {Converted} file(s) converted ({Good} with detected loops, {Full} with full-song loop).",
                converted, goodLoops, fullLoops);
            _logger.LogInformation("Output: {ValidateDir}", validateDir);
            _logger.LogInformation("Listen to the files in foobar2000 (with vgmstream) to verify loop points.");
            _logger.LogInformation("Delete any files you don't like, then run 'Accept Validated Nus3'.");
        }

        private List<(long loopStart, long loopEnd, double noteDistance, double loudnessDiff, double score)> RunPymusiclooper(string filePath)
        {
            var results = new List<(long loopStart, long loopEnd, double noteDistance, double loudnessDiff, double score)>();
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "pymusiclooper",
                        Arguments = $"export-points --path \"{filePath}\" --alt-export-top 10 --fmt samples --export-to stdout",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // Format: loop_start loop_end note_distance loudness_difference score
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5
                        && long.TryParse(parts[0], out var start)
                        && long.TryParse(parts[1], out var end)
                        && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var noteDist)
                        && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var loudness)
                        && double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var score))
                    {
                        results.Add((start, end, noteDist, loudness, score));
                    }
                }

                // Sort by score descending (should already be sorted, but be safe)
                results.Sort((a, b) => b.score.CompareTo(a.score));
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "pymusiclooper failed for {File}. Falling back to full-song loop.", filePath);
            }
            return results;
        }

        private long GetWavSampleCount(string filePath)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffprobe",
                        Arguments = $"-v error -select_streams a:0 -show_entries stream=sample_rate:stream=duration -of csv=p=0 \"{filePath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                // Output format: "sample_rate,duration" e.g. "48000,185.365979"
                var parts = output.Split(',');
                if (parts.Length >= 2
                    && int.TryParse(parts[0], out var sampleRate)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
                {
                    return (long)(duration * sampleRate);
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "ffprobe failed for {File}.", filePath);
            }
            return -1;
        }

        private int GetSourceSampleRate(string filePath)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffprobe",
                        Arguments = $"-v error -select_streams a:0 -show_entries stream=sample_rate -of csv=p=0 \"{filePath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                if (int.TryParse(output, out var rate))
                    return rate;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "ffprobe failed for {File}.", filePath);
            }
            return -1;
        }

        private void CreateLoopPreview(string sourceFile, long loopStart, long loopEnd, string outputPath)
        {
            try
            {
                var sampleRate = GetSourceSampleRate(sourceFile);
                if (sampleRate <= 0)
                {
                    _logger.LogWarning("  Could not determine sample rate for loop preview.");
                    return;
                }

                double startSec = (double)loopStart / sampleRate;
                double endSec = (double)loopEnd / sampleRate;

                // Preview: 10s before loop end → 10s after loop start (simulates the loop transition)
                double seg1Start = Math.Max(0, endSec - 10);
                double seg1End = endSec;
                double seg2Start = startSec;
                double seg2End = startSec + 10;

                var s1s = seg1Start.ToString("F4", CultureInfo.InvariantCulture);
                var s1e = seg1End.ToString("F4", CultureInfo.InvariantCulture);
                var s2s = seg2Start.ToString("F4", CultureInfo.InvariantCulture);
                var s2e = seg2End.ToString("F4", CultureInfo.InvariantCulture);

                var filter = $"[0:a]atrim=start={s1s}:end={s1e},asetpts=PTS-STARTPTS[a];" +
                             $"[0:a]atrim=start={s2s}:end={s2e},asetpts=PTS-STARTPTS[b];" +
                             $"[a][b]concat=n=2:v=0:a=1";

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = $"-i \"{sourceFile}\" -filter_complex \"{filter}\" \"{outputPath}\" -y",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                    _logger.LogInformation("  Loop preview: {Path}", outputPath);
                else
                    _logger.LogWarning("  Failed to create loop preview.");
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "  Failed to create loop preview.");
            }
        }

        private bool RunFfmpeg(string inputFile, string outputWav)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = $"-i \"{inputFile}\" -ar 48000 -ac 2 \"{outputWav}\" -y",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.StandardError.ReadToEnd(); // ffmpeg outputs to stderr
                process.WaitForExit();
                return File.Exists(outputWav) && new FileInfo(outputWav).Length > 0;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "ffmpeg failed converting {File}.", inputFile);
                return false;
            }
        }

        private static string DeriveToneId(string filename)
        {
            var nameOnly = Path.GetFileNameWithoutExtension(filename).ToLowerInvariant();
            var sb = new StringBuilder(nameOnly.Length);
            foreach (var c in nameOnly)
                sb.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
            var toneId = sb.ToString().Trim('_');
            if (toneId.Length > MusicConstants.GameResources.ToneIdMaximumSize)
                toneId = toneId[..MusicConstants.GameResources.ToneIdMaximumSize];
            return toneId;
        }
    }
}
