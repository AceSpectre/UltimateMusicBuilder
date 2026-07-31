using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h.Helpers;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using VGAudio.Cli;

namespace UMB.CLI.Services
{
    public class Nus3BatchInput
    {
        public string SeriesPath { get; set; }
        public List<Nus3BatchDecision> Decisions { get; set; }
    }

    public class Nus3BatchDecision
    {
        public string Filename { get; set; }
        public string Mode { get; set; }
        public long LoopStartSamples { get; set; }
        public long LoopEndSamples { get; set; }
    }

    public class Nus3ConvertService
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;

        public Nus3ConvertService(IOptionsMonitor<Sma5hMusicOptions> musicConfig, ILogger<Nus3ConvertService> logger)
        {
            _musicConfig = musicConfig;
            _logger = logger;
        }

        // ffmpeg/ffprobe/ffplay and pymusiclooper are expected on the system PATH
        // (install via choco/brew/apt/pipx — see scripts/fetch-tools for hints).
        private string ResolveFfTool(string name) =>
            ToolPathResolver.Resolve(null, null, name) ?? name;

        private string ResolvePymusiclooper() =>
            ToolPathResolver.Resolve(null, null, "pymusiclooper") ?? "pymusiclooper";

        /// <summary>
        /// Runs a console tool to completion. Returns stdout when captureStdout,
        /// otherwise drains and returns stderr (ffmpeg-family tools log there).
        /// </summary>
        private static string RunProcess(string fileName, string arguments, bool captureStdout = false)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = captureStdout ? process.StandardOutput.ReadToEnd() : process.StandardError.ReadToEnd();
            process.WaitForExit();
            return output;
        }

        private (string nus3AudioExe, string validateDir, string tempDir)? PrepareConversion(string seriesDir)
        {
            var nus3AudioExe = ToolPathResolver.Resolve(_musicConfig.CurrentValue.ToolsPath, MusicConstants.Resources.NUS3AUDIO_EXE_FILE);
            if (nus3AudioExe == null)
            {
                _logger.LogError("nus3audio binary not found under Tools/{Rel}. Run scripts/fetch-tools to install.", MusicConstants.Resources.NUS3AUDIO_EXE_FILE);
                return null;
            }

            var validateDir = Path.Combine(seriesDir, CliUtil.ValidateFolder);
            Directory.CreateDirectory(validateDir);

            var tempDir = Path.Combine(_musicConfig.CurrentValue.TempPath, "nus3convert");
            Directory.CreateDirectory(tempDir);

            return (nus3AudioExe, validateDir, tempDir);
        }

        // Namco Opus accepts only 8/12/16/24/48 kHz, so anything that isn't a 48 kHz .wav
        // (including .wav at other rates) is resampled to a temp WAV first.
        private (string wavFile, bool isTemp)? PrepareWav48k(string sourceFile, string basename, string tempDir)
        {
            var isWav = sourceFile.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
            if (isWav && GetSourceSampleRate(sourceFile) == 48000)
                return (sourceFile, false);

            var wavFile = Path.Combine(tempDir, basename + ".wav");
            if (!RunFfmpeg(sourceFile, wavFile))
            {
                _logger.LogError("  ffmpeg conversion failed for '{Basename}', skipping.", basename);
                return null;
            }
            return (wavFile, true);
        }

        /// <summary>Full-song loop end = last sample of the (48 kHz) WAV.</summary>
        private bool TryGetFullSongLoopEnd(string wavFile, string basename, out long loopEnd)
        {
            loopEnd = GetWavSampleCount(wavFile) - 1;
            if (loopEnd < 0)
            {
                _logger.LogError("  Could not determine sample count for '{Basename}', skipping.", basename);
                return false;
            }
            _logger.LogInformation("  Full-song loop: 0-{End}", loopEnd);
            return true;
        }

        /// <summary>
        /// Encodes a prepared WAV to Namco lopus (VGAudio) and wraps it into outputNus3
        /// via nus3audio. Returns true when the output exists and is non-empty.
        /// </summary>
        private bool ConvertWavToNus3(string wavFile, string basename, long loopStart, long loopEnd,
            string tempDir, string nus3AudioExe, string outputNus3)
        {
            var lopusFile = Path.Combine(tempDir, basename + ".lopus");
            try
            {
                string vgOutput;
                try
                {
                    var oldOut = Console.Out;
                    using var writer = new StringWriter();
                    try
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
                    finally
                    {
                        Console.SetOut(oldOut);
                    }
                    vgOutput = writer.ToString();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "  VGAudioCli conversion failed for '{Basename}'.", basename);
                    return false;
                }

                if (!File.Exists(lopusFile) || new FileInfo(lopusFile).Length == 0)
                {
                    _logger.LogError("  VGAudioCli produced no output for '{Basename}', skipping. VGAudio said: {Msg}",
                        basename, string.IsNullOrWhiteSpace(vgOutput) ? "(no message)" : vgOutput.Trim());
                    return false;
                }

                var toneId = FolderMusicMod.DeriveToneId(basename);
                try
                {
                    RunProcess(nus3AudioExe, $"-n -w \"{outputNus3}\"");
                    RunProcess(nus3AudioExe, $"-A {toneId} \"{lopusFile}\" -w \"{outputNus3}\"");
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "  nus3audio wrapping failed for '{Basename}'.", basename);
                    return false;
                }

                if (File.Exists(outputNus3) && new FileInfo(outputNus3).Length > 0)
                    return true;

                _logger.LogError("  nus3audio output was empty for '{Basename}'.", basename);
                return false;
            }
            finally
            {
                if (File.Exists(lopusFile))
                    File.Delete(lopusFile);
            }
        }

        public void Run()
        {
            Script.PrintBanner(_logger);

            var (modDir, seriesDir) = Script.PromptModAndSeries(_musicConfig, _logger);
            if (modDir == null || seriesDir == null)
                return;

            // Find source audio files that aren't already game formats
            var sourceFiles = Directory.GetFiles(seriesDir)
                .Where(f => CliUtil.SourceAudioExtensions.Contains(Path.GetExtension(f)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (sourceFiles.Count == 0)
            {
                _logger.LogWarning("No source audio files (.mp3, .flac, .wav, .ogg) found in {Dir}.", seriesDir);
                return;
            }

            var autoMode = AnsiConsole.Confirm(
                "Auto-convert without previewing/selecting loops? (uses highest-ranked loop above threshold, falls back to full-song loop)",
                defaultValue: false);

            var loopScoreThreshold = (double)AnsiConsole.Prompt(
                new TextPrompt<float>("Minimum loop score (only increase if subpar loops are being accepted):")
                    .DefaultValue(94.5f)) / 100.0;

            var previewLength = autoMode ? 5.0 : (double)AnsiConsole.Prompt(
                new TextPrompt<float>("Loop preview length in seconds (total, split evenly before/after loop point):")
                    .DefaultValue(5f));

            var prep = PrepareConversion(seriesDir);
            if (prep == null)
                return;
            var (nus3AudioExe, validateDir, tempDir) = prep.Value;

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

                var siblingNus3 = Path.Combine(seriesDir, basename + ".nus3audio");
                if (File.Exists(siblingNus3))
                {
                    var skipExisting = AnsiConsole.Confirm(
                        $"'[cyan]{Markup.Escape(basename)}[/]' already has a .nus3audio in the series folder. Skip conversion?",
                        defaultValue: true);
                    if (skipExisting)
                    {
                        _logger.LogInformation("Skipping '{Basename}': sibling .nus3audio exists in series folder.", basename);
                        continue;
                    }
                }

                _logger.LogInformation("Processing '{Basename}'...", basename);

                var loopCandidates = RunPymusiclooper(sourceFile);
                var sourceSampleRate = GetSourceSampleRate(sourceFile);
                long loopStart, loopEnd;
                bool isFullSongLoop;

                if (loopCandidates.Count > 0 && loopCandidates.Any(c => c.score >= loopScoreThreshold))
                {
                    int selectedCandidateIndex;
                    if (autoMode)
                    {
                        // Auto-pick the highest-ranked candidate above threshold (list is sorted by score desc)
                        selectedCandidateIndex = loopCandidates.FindIndex(c => c.score >= loopScoreThreshold);
                    }
                    else
                    {
                        selectedCandidateIndex = PromptForLoopCandidate(basename, loopCandidates, sourceSampleRate, sourceFile, previewLength);
                    }

                    if (selectedCandidateIndex >= 0)
                    {
                        var selected = loopCandidates[selectedCandidateIndex];
                        loopStart = selected.loopStart;
                        loopEnd = selected.loopEnd;
                        isFullSongLoop = false;
                        // Convert loop points from source sample rate to 48kHz (the WAV output rate)
                        if (sourceSampleRate > 0 && sourceSampleRate != 48000)
                        {
                            loopStart = (long)Math.Round((double)loopStart / sourceSampleRate * 48000);
                            loopEnd = (long)Math.Round((double)loopEnd / sourceSampleRate * 48000);
                            _logger.LogDebug("  Resampled loop points to 48kHz: {Start}-{End}", loopStart, loopEnd);
                        }
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
                    loopStart = 0;
                    loopEnd = 0; // will be set from WAV after conversion
                    isFullSongLoop = true;
                    var bestScore = loopCandidates.Count > 0 ? loopCandidates[0].score : 0;
                    _logger.LogInformation("  No candidates above threshold (best: {Score:P1}), using full-song loop.", bestScore);
                    fullLoops++;
                }

                var wav = PrepareWav48k(sourceFile, basename, tempDir);
                if (wav == null)
                    continue;
                var (wavFile, tempWav) = wav.Value;

                // For full-song loops, get exact sample count from the converted WAV
                if (isFullSongLoop && !TryGetFullSongLoopEnd(wavFile, basename, out loopEnd))
                {
                    if (tempWav && File.Exists(wavFile)) File.Delete(wavFile);
                    continue;
                }

                if (ConvertWavToNus3(wavFile, basename, loopStart, loopEnd, tempDir, nus3AudioExe, outputNus3))
                {
                    _logger.LogInformation("  → {OutputPath}", outputNus3);
                    converted++;

                    // Always emit in both modes so users can review the whole batch after auto-convert.
                    if (!isFullSongLoop)
                    {
                        var loopsDir = Path.Combine(validateDir, "loops");
                        Directory.CreateDirectory(loopsDir);
                        var previewPath = Path.Combine(loopsDir, basename + "_loop.wav");
                        CreateLoopPreview(sourceFile, loopStart, loopEnd, previewPath, previewLength / 2);
                    }
                }

                if (tempWav && File.Exists(wavFile))
                    File.Delete(wavFile);
            }

            try { Directory.Delete(tempDir, recursive: false); } catch { }

            _logger.LogInformation("--------------------");
            _logger.LogInformation("Nus3 conversion complete: {Converted} file(s) converted ({Good} with detected loops, {Full} with full-song loop).",
                converted, goodLoops, fullLoops);
            _logger.LogInformation("Output: {ValidateDir}", validateDir);
            _logger.LogInformation("Listen to the files in foobar2000 (with vgmstream) to verify loop points.");
            _logger.LogInformation("Delete any files you don't like, then run 'Accept Validated Nus3'.");
        }

        public void RunBatch(string jsonPath)
        {
            if (string.IsNullOrWhiteSpace(jsonPath))
            {
                _logger.LogError("Usage: dotnet run nus3-convert-batch <decisions.json>");
                return;
            }

            if (!File.Exists(jsonPath))
            {
                _logger.LogError("JSON file not found: {Path}", jsonPath);
                return;
            }

            var jsonText = File.ReadAllText(jsonPath);
            var input = JsonSerializer.Deserialize<Nus3BatchInput>(jsonText,
                CliUtil.JsonCaseInsensitive);

            if (input == null || input.Decisions == null || input.Decisions.Count == 0)
            {
                _logger.LogError("No decisions found in {Path}.", jsonPath);
                return;
            }

            var seriesDir = input.SeriesPath;
            if (!Directory.Exists(seriesDir))
            {
                _logger.LogError("Series path does not exist: {Path}", seriesDir);
                return;
            }

            var prep = PrepareConversion(seriesDir);
            if (prep == null)
                return;
            var (nus3AudioExe, validateDir, tempDir) = prep.Value;

            int converted = 0;

            foreach (var decision in input.Decisions)
            {
                var sourceFile = Path.Combine(seriesDir, decision.Filename);
                var basename = Path.GetFileNameWithoutExtension(decision.Filename);
                var outputNus3 = Path.Combine(validateDir, basename + ".nus3audio");

                if (File.Exists(outputNus3))
                {
                    _logger.LogInformation("Skipping '{Basename}': already exists in songs-to-validate.", basename);
                    continue;
                }

                if (!File.Exists(sourceFile))
                {
                    _logger.LogError("Source file not found: {Path}", sourceFile);
                    continue;
                }

                _logger.LogInformation("Processing '{Basename}' (mode: {Mode})...", basename, decision.Mode);

                var wav = PrepareWav48k(sourceFile, basename, tempDir);
                if (wav == null)
                    continue;
                var (wavFile, tempWav) = wav.Value;

                long loopStart, loopEnd;
                if (string.Equals(decision.Mode, "loop", StringComparison.OrdinalIgnoreCase))
                {
                    loopStart = decision.LoopStartSamples;
                    loopEnd = decision.LoopEndSamples;
                    _logger.LogInformation("  Loop points: {Start}-{End}", loopStart, loopEnd);
                }
                else
                {
                    loopStart = 0;
                    if (!TryGetFullSongLoopEnd(wavFile, basename, out loopEnd))
                    {
                        if (tempWav && File.Exists(wavFile)) File.Delete(wavFile);
                        continue;
                    }
                }

                if (ConvertWavToNus3(wavFile, basename, loopStart, loopEnd, tempDir, nus3AudioExe, outputNus3))
                {
                    _logger.LogInformation("  -> {OutputPath}", outputNus3);
                    converted++;
                }

                if (tempWav && File.Exists(wavFile))
                    File.Delete(wavFile);
            }

            try { Directory.Delete(tempDir, recursive: false); } catch { }

            _logger.LogInformation("--------------------");
            _logger.LogInformation("Batch nus3 conversion complete: {Converted}/{Total} file(s) converted.",
                converted, input.Decisions.Count);
            _logger.LogInformation("Output: {ValidateDir}", validateDir);
        }

        private int PromptForLoopCandidate(
            string basename,
            List<(long loopStart, long loopEnd, double noteDistance, double loudnessDiff, double score)> loopCandidates,
            int sourceSampleRate,
            string sourceFile,
            double previewLength)
        {
            string FormatTime(long samples) =>
                sourceSampleRate > 0 ? TimeSpan.FromSeconds((double)samples / sourceSampleRate).ToString(@"mm\:ss\.ff") : "??";

            var choices = new List<string>();
            for (int i = 0; i < loopCandidates.Count; i++)
            {
                var c = loopCandidates[i];
                choices.Add($"#{i + 1}  {c.score:P1}  {FormatTime(c.loopStart)} ({c.loopStart})  {FormatTime(c.loopEnd)} ({c.loopEnd})  {c.noteDistance:F4}  {c.loudnessDiff:F4} dB");
                choices.Add($"#{i + 1}  Preview loop");
            }
            choices.Add("Reject all (use full-song loop)");

            while (true)
            {
                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .Title($"Loop candidates for [cyan]{Markup.Escape(basename)}[/]")
                    .AddColumn("#")
                    .AddColumn("Score")
                    .AddColumn("Start")
                    .AddColumn("End")
                    .AddColumn("Note Dist")
                    .AddColumn("Loudness Diff");
                for (int i = 0; i < loopCandidates.Count; i++)
                {
                    var c = loopCandidates[i];
                    table.AddRow(
                        (i + 1).ToString(),
                        $"{c.score:P1}",
                        $"{FormatTime(c.loopStart)} ({c.loopStart})",
                        $"{FormatTime(c.loopEnd)} ({c.loopEnd})",
                        $"{c.noteDistance:F4}",
                        $"{c.loudnessDiff:F4} dB");
                }
                AnsiConsole.Write(table);

                var selection = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .WrapAround()
                        .Title("Select a loop or preview:")
                        .HighlightStyle(new Style(Color.Cyan1))
                        .AddChoices(choices));

                if (selection.Contains("Preview loop"))
                {
                    var previewIdx = int.Parse(selection.Substring(1, selection.IndexOf(' ') - 1)) - 1;
                    var pc = loopCandidates[previewIdx];
                    PlayLoopPreview(sourceFile, pc.loopStart, pc.loopEnd, previewLength / 2);
                }
                else if (selection.StartsWith("#"))
                {
                    return int.Parse(selection.Substring(1, selection.IndexOf(' ') - 1)) - 1;
                }
                else
                {
                    return -1;
                }
            }
        }

        private List<(long loopStart, long loopEnd, double noteDistance, double loudnessDiff, double score)> RunPymusiclooper(string filePath)
        {
            var results = new List<(long loopStart, long loopEnd, double noteDistance, double loudnessDiff, double score)>();
            try
            {
                var output = RunProcess(ResolvePymusiclooper(),
                    $"export-points --path \"{filePath}\" --alt-export-top 10 --fmt samples --export-to stdout",
                    captureStdout: true);

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

                // pymusiclooper usually emits sorted output; re-sort defensively.
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
                var output = RunProcess(ResolveFfTool("ffprobe"),
                    $"-v error -select_streams a:0 -show_entries stream=sample_rate:stream=duration -of csv=p=0 \"{filePath}\"",
                    captureStdout: true).Trim();

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
                var output = RunProcess(ResolveFfTool("ffprobe"),
                    $"-v error -select_streams a:0 -show_entries stream=sample_rate -of csv=p=0 \"{filePath}\"",
                    captureStdout: true).Trim();

                if (int.TryParse(output, out var rate))
                    return rate;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "ffprobe failed for {File}.", filePath);
            }
            return -1;
        }

        private void CreateLoopPreview(string sourceFile, long loopStart, long loopEnd, string outputPath, double previewHalfLength = 5)
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

                // Preview: N seconds before loop end → N seconds after loop start (simulates the loop transition)
                double seg1Start = Math.Max(0, endSec - previewHalfLength);
                double seg1End = endSec;
                double seg2Start = startSec;
                double seg2End = startSec + previewHalfLength;

                var s1s = seg1Start.ToString("F4", CultureInfo.InvariantCulture);
                var s1e = seg1End.ToString("F4", CultureInfo.InvariantCulture);
                var s2s = seg2Start.ToString("F4", CultureInfo.InvariantCulture);
                var s2e = seg2End.ToString("F4", CultureInfo.InvariantCulture);

                var filter = $"[0:a]atrim=start={s1s}:end={s1e},asetpts=PTS-STARTPTS[a];" +
                             $"[0:a]atrim=start={s2s}:end={s2e},asetpts=PTS-STARTPTS[b];" +
                             $"[a][b]concat=n=2:v=0:a=1";

                RunProcess(ResolveFfTool("ffmpeg"), $"-i \"{sourceFile}\" -filter_complex \"{filter}\" \"{outputPath}\" -y");

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

        private void PlayLoopPreview(string sourceFile, long loopStart, long loopEnd, double previewHalfLength = 5)
        {
            string tempPreview = null;
            try
            {
                var sampleRate = GetSourceSampleRate(sourceFile);
                if (sampleRate <= 0)
                {
                    _logger.LogWarning("  Could not determine sample rate for preview playback.");
                    return;
                }

                // Create a temporary preview WAV
                tempPreview = Path.Combine(Path.GetTempPath(), $"loop_preview_{Guid.NewGuid():N}.wav");
                CreateLoopPreview(sourceFile, loopStart, loopEnd, tempPreview, previewHalfLength);

                if (!File.Exists(tempPreview) || new FileInfo(tempPreview).Length == 0)
                {
                    _logger.LogWarning("  Could not generate preview audio.");
                    return;
                }

                AnsiConsole.MarkupLine("[yellow]Playing loop preview... press Q to stop.[/]");
                RunProcess(ResolveFfTool("ffplay"), $"-nodisp -autoexit \"{tempPreview}\"");
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "  Failed to play loop preview. Make sure ffplay is installed (comes with ffmpeg).");
            }
            finally
            {
                if (tempPreview != null && File.Exists(tempPreview))
                {
                    try { File.Delete(tempPreview); } catch { }
                }
            }
        }

        private bool RunFfmpeg(string inputFile, string outputWav)
        {
            try
            {
                RunProcess(ResolveFfTool("ffmpeg"), $"-i \"{inputFile}\" -ar 48000 -ac 2 \"{outputWav}\" -y");
                return File.Exists(outputWav) && new FileInfo(outputWav).Length > 0;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "ffmpeg failed converting {File}.", inputFile);
                return false;
            }
        }
    }
}
