using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UMB.CLI.Services;

namespace UMB.CLI.Desktop
{
    /// <summary>A failure whose message is meant for the user (bad input, missing series, …).</summary>
    public class DesktopApiException : Exception
    {
        public DesktopApiException(string message) : base(message) { }
    }

    /// <summary>
    /// JSON entry points used by the desktop app: <c>&lt;action&gt; &lt;input.json&gt; &lt;output.json&gt;</c>.
    /// The output file holds <c>{"result": …}</c> on success or <c>{"error": "…"}</c> on failure.
    /// </summary>
    public class DesktopApi
    {
        public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly Dictionary<string, Func<JsonElement, object>> _handlers;
        private readonly ILogger _logger;

        // Services are resolved per call so an action constructs only what it uses.
        public DesktopApi(IServiceProvider services, ILogger<DesktopApi> logger)
        {
            _logger = logger;
            T Get<T>() => services.GetRequiredService<T>();
            _handlers = new Dictionary<string, Func<JsonElement, object>>(StringComparer.OrdinalIgnoreCase)
            {
                ["mods-list"] = _ => Get<ModsService>().ListMods(),
                ["mod-series-list"] = In<ModPathInput>(i => Get<ModsService>().ListModSeries(i.ModPath)),
                ["mod-stats"] = In<ModPathInput>(i => Get<ModsService>().GetModStats(i.ModPath)),

                ["series-order-load"] = In<ModPathInput>(i => Get<SeriesOrderService>().Load(i.ModPath)),
                ["series-order-save"] = In<SaveSeriesOrderInput>(i => Get<SeriesOrderService>().Save(i.ModPath, i.Items)),
                ["series-create"] = In<CreateSeriesRequest>(i => Get<SeriesOrderService>().Create(i.ModPath, i.Input)),
                ["series-set-icon"] = In<SetSeriesIconInput>(i => Get<SeriesOrderService>().SetIcon(i.ModPath, i.SeriesId, i.IconDataUrl)),

                ["track-order-load"] = In<SeriesPathInput>(i => Get<TrackOrderService>().Load(i.SeriesPath)),
                ["song-presets-save"] = In<SaveSongPresetsInput>(i => Get<TrackOrderService>().SavePresets(i.SeriesPath, i.SeriesDefaults, i.Presets)),
                ["song-preset-apply"] = In<ApplySongPresetInput>(i => Get<TrackOrderService>().ApplyPreset(i.SeriesPath, i.Fields, i.Game, i.UseSeriesDefaults)),
                ["track-order-save"] = In<SaveTrackOrderInput>(i => Get<TrackOrderService>().Save(i.SeriesPath, i.Items)),

                ["merge-analyze"] = In<MergeInput>(i => Get<MergeService>().Analyze(i.ModPaths)),
                ["merge-validate-name"] = In<MergeInput>(i => Get<MergeService>().ValidateOutputName(i.OutputName)),
                ["merge-execute"] = In<MergeInput>(i => Get<MergeService>().Execute(i.ModPaths, i.OutputName, i.PriorityModPath)),

                ["extract-icons-analyze"] = In<ExtractIconsInput>(i => Get<ExtractIconsService>().Analyze(i.CompiledModPath, i.ModPath)),
                ["extract-icons-run"] = In<ExtractIconsInput>(i => Get<ExtractIconsService>().Extract(i.CompiledModPath, i.ModPath, i.Mode == "missing-only")),

                ["nus3-list-sources"] = In<SeriesPathInput>(i => Get<Nus3WorkflowService>().ListSources(i.SeriesPath)),
                ["nus3-load-conversions"] = In<SeriesPathInput>(i => Get<Nus3WorkflowService>().LoadConversions(i.SeriesPath)),
                ["nus3-analyze-loop"] = In<Nus3FileInput>(i => Get<Nus3WorkflowService>().AnalyzeLoopPoints(i.FilePath, i.Options ?? new LoopAnalysisOptions())),
                ["nus3-duration"] = In<Nus3FileInput>(i => Get<Nus3WorkflowService>().GetTrackDuration(i.FilePath)),
                ["nus3-waveform"] = In<Nus3FileInput>(i => Get<Nus3WorkflowService>().ExtractWaveformPeaks(i.FilePath, i.Bars ?? 140)),
                ["nus3-loop-preview"] = In<Nus3PreviewInput>(i => Get<Nus3WorkflowService>().GenerateLoopPreview(i.FilePath, i.LoopStart, i.LoopEnd, i.PreviewLength)),
                ["nus3-convert-track"] = In<Nus3ConvertTrackInput>(i => Get<Nus3WorkflowService>().ConvertTrack(i.SeriesPath, i.TrackId, i.Mode, i.Candidate)),
                ["nus3-reject"] = In<Nus3TrackInput>(i => { Get<Nus3WorkflowService>().RejectTrack(i.SeriesPath, i.TrackId); return true; }),
                ["nus3-accept"] = In<Nus3AcceptInput>(i => { Get<AcceptNus3Service>().Accept(i.SeriesPath, i.DeleteSources); return true; }),

                ["playlist-info"] = _ => Get<VanillaCatalogService>().GetPlaylistInfo(),
                ["playlists-load"] = In<ModPathInput>(i => Get<PlaylistAssignmentService>().Load(i.ModPath)),
                ["playlists-save"] = In<SavePlaylistsInput>(i => Get<PlaylistAssignmentService>().Save(i.ModPath, i.Assignments)),
            };
        }

        public bool Handles(string action) => _handlers.ContainsKey(action);

        public void Run(string action, string[] args)
        {
            if (args == null || args.Length < 2)
            {
                _logger.LogError("Usage: {Action} <input.json> <output.json>", action);
                return;
            }

            object response;
            try
            {
                using var input = JsonDocument.Parse(File.ReadAllText(args[0]));
                response = new { result = _handlers[action](input.RootElement) };
            }
            catch (DesktopApiException ex)
            {
                // The app shows this message to the user itself, so it is not logged as an error.
                _logger.LogDebug("{Action}: {Message}", action, ex.Message);
                response = new { error = ex.Message };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Action} failed.", action);
                response = new { error = ex.Message };
            }

            File.WriteAllText(args[1], JsonSerializer.Serialize(response, Json));
        }

        private static Func<JsonElement, object> In<T>(Func<T, object> handler) =>
            json => handler(json.Deserialize<T>(Json) ?? throw new DesktopApiException("Missing request input."));

        private record ModPathInput(string ModPath);
        private record SeriesPathInput(string SeriesPath);
        private record SaveSeriesOrderInput(string ModPath, List<SaveSeriesItem> Items);
        private record CreateSeriesRequest(string ModPath, CreateSeriesInput Input);
        private record SetSeriesIconInput(string ModPath, string SeriesId, string IconDataUrl);
        private record SaveSongPresetsInput(string SeriesPath, DefaultTrackData SeriesDefaults, List<DefaultTrackData> Presets);
        private record ApplySongPresetInput(string SeriesPath, TrackFields Fields, string Game, bool UseSeriesDefaults);
        private record SaveTrackOrderInput(string SeriesPath, List<SaveTrackItem> Items);
        private record MergeInput(List<string> ModPaths, string OutputName, string PriorityModPath);
        private record ExtractIconsInput(string CompiledModPath, string ModPath, string Mode);
        private record Nus3FileInput(string FilePath, LoopAnalysisOptions Options, int? Bars);
        private record Nus3PreviewInput(string FilePath, double LoopStart, double LoopEnd, double PreviewLength);
        private record Nus3TrackInput(string SeriesPath, string TrackId);
        private record Nus3AcceptInput(string SeriesPath, bool DeleteSources);
        private record Nus3ConvertTrackInput(string SeriesPath, string TrackId, string Mode, LoopCandidate Candidate);
        private record SavePlaylistsInput(string ModPath, List<PlaylistAssignmentInput> Assignments);
    }
}
