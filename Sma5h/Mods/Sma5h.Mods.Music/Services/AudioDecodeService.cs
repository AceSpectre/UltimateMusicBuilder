using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h.Interfaces;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using System;
using System.IO;
using System.Linq;
using System.Text;
using VGAudio.Cli;

namespace Sma5h.Mods.Music.Services
{
    public class AudioDecodeService : IAudioDecodeService
    {
        private readonly ILogger<IAudioDecodeService> _logger;
        private readonly IProcessService _processService;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _config;
        private readonly ILufsAnalysisService _lufsService; // for resolved FFmpeg path

        public AudioDecodeService(
            IOptionsMonitor<Sma5hMusicOptions> config,
            IProcessService processService,
            ILufsAnalysisService lufsService,
            ILogger<IAudioDecodeService> logger)
        {
            _config = config;
            _processService = processService;
            _lufsService = lufsService;
            _logger = logger;
        }

        public bool DecodeToWav(string inputFile, string outputWav)
        {
            if (!File.Exists(inputFile))
            {
                _logger.LogWarning("DecodeToWav: input file does not exist: {Path}", inputFile);
                return false;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputWav)));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create output directory for {Path}.", outputWav);
                return false;
            }

            var ext = Path.GetExtension(inputFile).ToLowerInvariant();

            // .wav: copy through unchanged so callers can uniformly delete the temp.
            if (ext == ".wav")
            {
                try { File.Copy(inputFile, outputWav, overwrite: true); return true; }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to copy WAV {In} → {Out}.", inputFile, outputWav); return false; }
            }

            // .nus3audio: extract the inner stream first, then recurse.
            if (ext == ".nus3audio")
                return DecodeNus3Audio(inputFile, outputWav);

            // VGAudio handles all the Nintendo formats: idsp, lopus, brstm, dsp, etc.
            if (ext == ".idsp" || ext == ".lopus" || ext == ".brstm" || ext == ".dsp" || ext == ".bcstm" || ext == ".bfstm" || ext == ".hps" || ext == ".hca" || ext == ".adx")
                return DecodeWithVGAudio(inputFile, outputWav);

            // Fallback: FFmpeg (handles wav/mp3/ogg/flac/etc.). If FFmpeg is unavailable, fail cleanly.
            return DecodeWithFfmpeg(inputFile, outputWav);
        }

        private bool DecodeNus3Audio(string inputFile, string outputWav)
        {
            var nus3AudioExe = Path.Combine(_config.CurrentValue.ToolsPath, MusicConstants.Resources.NUS3AUDIO_EXE_FILE);
            if (!File.Exists(nus3AudioExe))
            {
                _logger.LogWarning("nus3audio.exe not found at {Path}; cannot extract {File}.", nus3AudioExe, inputFile);
                return false;
            }

            var tempBase = _config.CurrentValue.TempPath ?? "Temp";
            // nus3audio.exe -e panics if the target directory already exists, so we ensure
            // only the parent Temp dir exists and let nus3audio create the (unique) extract dir.
            Directory.CreateDirectory(tempBase);
            var extractDir = Path.Combine(tempBase, $"nus3_extract_{Guid.NewGuid():N}");
            try
            {
                // Extract by filename — produces a single .lopus / .idsp / etc. inside extractDir.
                // The `--` separator is required because `-e` is documented as `<extract-name>...`
                // (multi-value) and would otherwise greedily consume the input file as a second
                // extraction folder, causing a "directory already exists" panic.
                _processService.RunProcess(nus3AudioExe, $"-e \"{extractDir}\" -- \"{inputFile}\"");

                var extracted = Directory.GetFiles(extractDir).FirstOrDefault();
                if (extracted == null)
                {
                    _logger.LogWarning("nus3audio.exe produced no extracted file for {File}.", inputFile);
                    return false;
                }

                // Recurse — inner format is typically .lopus or .idsp, both of which VGAudio handles.
                return DecodeToWav(extracted, outputWav);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract nus3audio {File}.", inputFile);
                return false;
            }
            finally
            {
                try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true); }
                catch { /* best effort */ }
            }
        }

        // VGAudio.Cli.Converter mutates Console.Out globally and runs a progress-bar Timer
        // whose callbacks fire AFTER RunConverterCli returns. Two failure modes:
        //   1. Parallel callers corrupt Console.Out (one thread's restore picks up the other
        //      thread's writer as the "previous" value).
        //   2. A StringWriter captured for output collection gets disposed before the timer's
        //      next tick, throwing ObjectDisposedException which kills the whole process.
        // Fix: serialize VGAudio calls under a single lock, and route output to TextWriter.Null
        // (which is a non-disposable shared no-op sink — even lingering timer writes are safe).
        private static readonly object _vgaudioConsoleLock = new();

        private bool DecodeWithVGAudio(string inputFile, string outputWav)
        {
            lock (_vgaudioConsoleLock)
            {
                var oldOut = Console.Out;
                try
                {
                    Console.SetOut(TextWriter.Null);
                    Converter.RunConverterCli(new[] { "-i", inputFile, "-o", outputWav });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "VGAudio conversion failed for {File}.", inputFile);
                    return false;
                }
                finally
                {
                    Console.SetOut(oldOut);
                }
            }

            if (!File.Exists(outputWav) || new FileInfo(outputWav).Length == 0)
            {
                _logger.LogWarning("VGAudio produced no output decoding {File}.", inputFile);
                return false;
            }
            return true;
        }

        private bool DecodeWithFfmpeg(string inputFile, string outputWav)
        {
            var ffmpegPath = _lufsService.ResolvedFfmpegPath;
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                _logger.LogWarning("FFmpeg unavailable; cannot decode {File} to WAV.", inputFile);
                return false;
            }
            // Capture stderr so we can surface it on failure. FFmpeg uses stderr for all
            // informational output (codec banner, progress) — passing errorRedirect tells
            // ProcessService not to log each line as an error.
            var stderr = new StringBuilder();
            try
            {
                _processService.RunProcess(ffmpegPath,
                    $"-hide_banner -nostats -y -i \"{inputFile}\" -ac 2 -c:a pcm_s16le \"{outputWav}\"",
                    standardRedirect: null,
                    errorRedirect: (_, data) => { if (data?.Data != null) stderr.AppendLine(data.Data); });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FFmpeg decode failed for {File}. FFmpeg said: {Stderr}", inputFile, stderr.ToString().Trim());
                return false;
            }
            if (!File.Exists(outputWav) || new FileInfo(outputWav).Length == 0)
            {
                _logger.LogWarning("FFmpeg produced no output decoding {File}. FFmpeg said: {Stderr}", inputFile, stderr.ToString().Trim());
                return false;
            }
            return true;
        }
    }
}
