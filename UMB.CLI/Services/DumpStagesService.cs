using Microsoft.Extensions.Logging;
using Sma5h.Mods.Music.Interfaces;
using System;
using System.IO;
using System.Linq;

namespace UMB.CLI.Services
{
    /// <summary>
    /// Diagnostic action: prints every stage's UiStageId / UiSeriesId / BgmSetId so we can
    /// see exactly which playlist each stage rolls from.
    /// </summary>
    public class DumpStagesService
    {
        private readonly ILogger _logger;
        private readonly IAudioStateService _audioStateService;

        public DumpStagesService(IAudioStateService audioStateService, ILogger<DumpStagesService> logger)
        {
            _audioStateService = audioStateService;
            _logger = logger;
        }

        public void Run()
        {
            Script.PrintBanner(_logger);

            try
            {
                _audioStateService.InitBgmEntriesFromStateManager();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load vanilla game data.");
                return;
            }

            var stages = _audioStateService.GetStagesEntries()
                .OrderBy(s => s.UiSeriesId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.UiStageId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogInformation("--------------------");
            _logger.LogInformation("Stage â†’ BgmSetId map ({Count} stages)", stages.Count);
            _logger.LogInformation("--------------------");

            foreach (var s in stages)
            {
                _logger.LogInformation("  {Stage,-50} series={Series,-30} bgm={Bgm}",
                    s.UiStageId ?? "<null>",
                    s.UiSeriesId ?? "<null>",
                    string.IsNullOrEmpty(s.BgmSetId) ? "<none>" : s.BgmSetId);
            }

            _logger.LogInformation("--------------------");
            _logger.LogInformation("Grouped by BgmSetId:");
            _logger.LogInformation("--------------------");

            var byPlaylist = stages
                .Where(s => !string.IsNullOrEmpty(s.BgmSetId))
                .GroupBy(s => s.BgmSetId, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in byPlaylist)
            {
                _logger.LogInformation("  [{Playlist}] ({Count} stage(s)):", group.Key, group.Count());
                foreach (var s in group.OrderBy(s => s.UiStageId, StringComparer.OrdinalIgnoreCase))
                    _logger.LogInformation("      {Stage}", s.UiStageId);
            }

            // Write a flat CSV next to the log so the user can grep / open in a spreadsheet.
            try
            {
                var path = Path.Combine(Directory.GetCurrentDirectory(), "stages_dump.csv");
                using var w = new StreamWriter(path);
                w.WriteLine("ui_stage_id,ui_series_id,bgm_set_id");
                foreach (var s in stages)
                    w.WriteLine($"{s.UiStageId},{s.UiSeriesId},{s.BgmSetId}");
                _logger.LogInformation("Wrote {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write stages_dump.csv");
            }
        }
    }
}
