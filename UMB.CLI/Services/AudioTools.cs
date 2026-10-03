using Sma5h.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using UMB.CLI.Desktop;

namespace UMB.CLI.Services
{
    /// <summary>
    /// ffprobe/ffmpeg/pymusiclooper calls shared by nus3 conversion and loop analysis. These tools
    /// are expected on the system PATH (install via choco/brew/apt/pipx — see scripts/fetch-tools).
    /// </summary>
    public static class AudioTools
    {
        public record RawLoop(long Start, long End, double NoteDistance, double LoudnessDiff, double Score);

        private static readonly TimeSpan PymusiclooperTimeout = TimeSpan.FromMinutes(2);

        public static string Tool(string name) => ToolPathResolver.Resolve(null, null, name) ?? name;

        /// <summary>Sample rate and duration (seconds) of the first audio stream; each 0 when unknown or ffprobe fails.</summary>
        public static (int SampleRate, double Duration) Probe(string filePath)
        {
            var output = TryRun(Tool("ffprobe"),
                $"-v error -select_streams a:0 -show_entries stream=sample_rate:stream=duration -of csv=p=0 \"{filePath}\"");
            // Output format: "sample_rate,duration" e.g. "48000,185.365979"
            var parts = output.Trim().Split(',');
            var rate = int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : 0;
            var duration = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
            return (rate, duration);
        }

        /// <summary>pymusiclooper's top loop candidates, best score first; empty when it finds none or fails.</summary>
        public static List<RawLoop> FindLoops(string filePath, LoopAnalysisOptions options = null)
        {
            var args = $"export-points --path \"{filePath}\" --alt-export-top 10 --fmt samples --export-to stdout";
            if (options?.MinLoopDuration > 0)
                args += $" --min-loop-duration {options.MinLoopDuration.Value.ToString(CultureInfo.InvariantCulture)}";
            else if (options?.MinDurationMultiplier != null)
                args += $" --min-duration-multiplier {options.MinDurationMultiplier.Value.ToString(CultureInfo.InvariantCulture)}";
            if (options?.DisablePruning == true)
                args += " --disable-pruning";

            var loops = new List<RawLoop>();
            // Format: loop_start loop_end note_distance loudness_difference score
            foreach (var line in TryRun(Tool("pymusiclooper"), args, PymusiclooperTimeout).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5
                    && long.TryParse(parts[0], out var start)
                    && long.TryParse(parts[1], out var end)
                    && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var noteDistance)
                    && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var loudness)
                    && double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var score))
                {
                    loops.Add(new RawLoop(start, end, noteDistance, loudness, score));
                }
            }
            // pymusiclooper usually emits sorted output; re-sort defensively.
            loops.Sort((a, b) => b.Score.CompareTo(a.Score));
            return loops;
        }

        /// <summary>A tool's stdout, or "" when it can't be started or times out.</summary>
        private static string TryRun(string tool, string arguments, TimeSpan? timeout = null)
        {
            try { return ProcessRunner.Run(tool, arguments, timeout).StandardOutput; }
            catch (Exception ex) when (ex is TimeoutException or Win32Exception) { return ""; }
        }

        /// <summary>
        /// Renders the loop seam: <paramref name="halfLength"/> seconds before the loop end
        /// followed by the same after the loop start. True when the WAV was written.
        /// </summary>
        public static bool RenderLoopPreview(string sourceFile, double loopStartSec, double loopEndSec, double halfLength, string outputWav)
        {
            static string Sec(double value) => value.ToString("F4", CultureInfo.InvariantCulture);
            var filter = $"[0:a]atrim=start={Sec(Math.Max(0, loopEndSec - halfLength))}:end={Sec(loopEndSec)},asetpts=PTS-STARTPTS[a];" +
                         $"[0:a]atrim=start={Sec(loopStartSec)}:end={Sec(loopStartSec + halfLength)},asetpts=PTS-STARTPTS[b];" +
                         "[a][b]concat=n=2:v=0:a=1";
            ProcessRunner.Run(Tool("ffmpeg"), $"-i \"{sourceFile}\" -filter_complex \"{filter}\" \"{outputWav}\" -y");
            return File.Exists(outputWav) && new FileInfo(outputWav).Length > 0;
        }
    }
}
