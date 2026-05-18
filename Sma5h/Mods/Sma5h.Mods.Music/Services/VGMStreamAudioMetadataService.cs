using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Sma5h.Helpers;
using Sma5h.Interfaces;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Models;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using VGAudio.Cli;

namespace Sma5h.Mods.Music.Services
{
    public class VGMStreamAudioMetadataService : IAudioMetadataService
    {
        // Cross-platform: shells out to the official `vgmstream-cli` binary
        // (https://github.com/vgmstream/vgmstream/releases) and parses its JSON
        // metadata output (-I flag). Previously this called into libvgmstream
        // via P/Invoke through VGMMusicPlayer, which required a chain of native
        // Windows DLLs and had no equivalent prebuilt on Linux/macOS.

        private const string VgmStreamCliRelative = "vgmstream-cli/vgmstream-cli";

        private readonly ILogger _logger;
        private readonly IProcessService _processService;
        private readonly IOptionsMonitor<Sma5hOptions> _config;

        public VGMStreamAudioMetadataService(IOptionsMonitor<Sma5hOptions> config, IProcessService processService, ILogger<IAudioMetadataService> logger)
        {
            _config = config;
            _processService = processService;
            _logger = logger;
        }

        public Task<AudioCuePoints> GetCuePoints(string inputFile)
        {
            _logger.LogDebug("Retrieving audio metadata for {FilePath}...", inputFile);

            var cuePoints = ReadCuePointsViaCli(inputFile);

            _logger.LogDebug("VGMStream metadata for {FilePath}: TotalSamples: {TotalSamples}, LoopStartSample: {LoopStartSample}, LoopEndSample: {LoopEndSample}, LoopStartMs: {LoopStartMs}, LoopEndMs: {LoopEndMs}",
                inputFile, cuePoints.TotalSamples, cuePoints.LoopStartSample, cuePoints.LoopEndSample, cuePoints.LoopStartMs, cuePoints.LoopEndMs);

            if (cuePoints.TotalSamples == 0 || cuePoints.LoopEndSample == 0)
            {
                _logger.LogWarning("VGMStream metadata for {FilePath}: total samples and/or loop end sample was 0. Use song_cue_points_override in the payload to override these values.", inputFile);
            }

            return Task.FromResult(cuePoints);
        }

        private AudioCuePoints ReadCuePointsViaCli(string inputFile)
        {
            var empty = new AudioCuePoints();

            var vgmstreamCli = ToolPathResolver.Resolve(_config.CurrentValue.ToolsPath, VgmStreamCliRelative, "vgmstream-cli");
            if (vgmstreamCli == null)
            {
                _logger.LogWarning("vgmstream-cli not found under Tools/{Rel} or on PATH. Cue points will be zero for {File}; use song_cue_points_override to set them manually, or run scripts/fetch-tools to install vgmstream-cli.",
                    VgmStreamCliRelative, inputFile);
                return empty;
            }

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            try
            {
                _processService.RunProcess(
                    vgmstreamCli,
                    $"-m -I \"{inputFile}\"",
                    standardRedirect: (_, data) => { if (data?.Data != null) stdout.AppendLine(data.Data); },
                    errorRedirect: (_, data) => { if (data?.Data != null) stderr.AppendLine(data.Data); });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "vgmstream-cli invocation failed for {File}.", inputFile);
                return empty;
            }

            var raw = stdout.ToString();
            var jsonStart = raw.IndexOf('{');
            var jsonEnd = raw.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd <= jsonStart)
            {
                _logger.LogWarning("vgmstream-cli produced no JSON for {File}. stderr: {Stderr}", inputFile, stderr.ToString().Trim());
                return empty;
            }

            JObject root;
            try
            {
                root = JObject.Parse(raw.Substring(jsonStart, jsonEnd - jsonStart + 1));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse vgmstream-cli JSON for {File}. Raw output:\n{Raw}", inputFile, raw);
                return empty;
            }

            var sampleRate = (int?)root["sampleRate"] ?? 0;
            var totalSamples = (long?)root["numberOfSamples"] ?? 0;
            long loopStart = 0;
            long loopEnd = 0;

            var loopingInfo = root["loopingInfo"];
            if (loopingInfo != null && loopingInfo.Type == JTokenType.Object)
            {
                loopStart = (long?)loopingInfo["start"] ?? 0;
                loopEnd = (long?)loopingInfo["end"] ?? 0;
            }

            if (sampleRate <= 0 || totalSamples <= 0)
            {
                _logger.LogWarning("vgmstream-cli returned invalid sampleRate ({Rate}) or numberOfSamples ({Samples}) for {File}.",
                    sampleRate, totalSamples, inputFile);
                return empty;
            }

            return new AudioCuePoints
            {
                TotalSamples = (uint)totalSamples,
                LoopStartSample = (uint)loopStart,
                LoopEndSample = (uint)loopEnd,
                TotalTimeMs = (uint)(totalSamples * 1000 / sampleRate),
                LoopStartMs = (uint)(loopStart * 1000 / sampleRate),
                LoopEndMs = (uint)(loopEnd * 1000 / sampleRate)
            };
        }

        public bool ConvertAudio(string inputMediaFile, string outputMediaFile)
        {
            _logger.LogDebug("Convert from {AudioMediaFile} to {AudioOutputFile}", inputMediaFile, outputMediaFile);

            if (!File.Exists(inputMediaFile))
            {
                _logger.LogError("File {mediaPath} does not exist....", inputMediaFile);
                return false;
            }

            if (File.Exists(outputMediaFile))
            {
                _logger.LogDebug("The conversion from {InputMediaFile} to {OutputMediaFile} was skipped. The file already exists.", inputMediaFile, outputMediaFile);
                return true;
            }

            var builder = new StringBuilder();

            var oldValue = Console.Out;
            using (var writer = new StringWriter(builder))
            {
                Console.SetOut(writer);
                if (outputMediaFile.EndsWith("lopus"))
                {
                    //Special tags for opus
                    Converter.RunConverterCli(new string[] { "-i", inputMediaFile, "-o", outputMediaFile, "--opusheader", "Namco", "--cbr" });
                }
                else
                {
                    Converter.RunConverterCli(new string[] { "-i", inputMediaFile, "-o", outputMediaFile });
                }
            }
            Console.SetOut(oldValue);

            var output = builder.ToString();

            _logger.LogDebug("VGAudio Convert for {OutputMediaFile}: {Data}", outputMediaFile, output.Trim('\r', '\n'));

            if (!File.Exists(outputMediaFile) || new FileInfo(outputMediaFile).Length == 0)
            {
                _logger.LogError("VGAudio Error - The conversion from {InputMediaFile} to {OutputMediaFile} failed. Reason {Reason}", inputMediaFile, outputMediaFile, output.Trim('\r', '\n'));
                return false;
            }
            return true;
        }
    }
}
