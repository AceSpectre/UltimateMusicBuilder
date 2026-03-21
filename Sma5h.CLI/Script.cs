using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h.Mods.Music;
using Sma5h.CLI.Services;
using Spectre.Console;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Sma5h.CLI
{
    public class Script
    {
        private const double CLIVersion = 1.41;
        private const string VALIDATE_FOLDER = "songs-to-validate";

        private readonly BuildService _build;
        private readonly ScaffoldService _scaffold;
        private readonly ConvertService _convert;
        private readonly ExtractIconsService _extractIcons;
        private readonly Nus3ConvertService _nus3Convert;
        private readonly AcceptNus3Service _acceptNus3;
        private readonly CleanupService _cleanup;

        public Script(BuildService build, ScaffoldService scaffold, ConvertService convert,
            ExtractIconsService extractIcons, Nus3ConvertService nus3Convert,
            AcceptNus3Service acceptNus3, CleanupService cleanup)
        {
            _build = build;
            _scaffold = scaffold;
            _convert = convert;
            _extractIcons = extractIcons;
            _nus3Convert = nus3Convert;
            _acceptNus3 = acceptNus3;
            _cleanup = cleanup;
        }

        public async Task RunBuild() => await _build.Run();
        public void RunScaffold() => _scaffold.Run();
        public void RunConvert() => _convert.Run();
        public void RunExtractIcons() => _extractIcons.Run();
        public void RunNus3Convert() => _nus3Convert.Run();
        public void RunAcceptValidatedNus3() => _acceptNus3.Run();
        public void RunCleanup() => _cleanup.Run();

        // ── Shared helpers used by services ──

        public static void PrintBanner(ILogger logger)
        {
            logger.LogInformation($"Sma5h.CLI v.{CLIVersion}");
            logger.LogInformation("--------------------");
            logger.LogInformation("research: soneek");
            logger.LogInformation("prcEditor: https://github.com/BenHall-7/paracobNET");
            logger.LogInformation("msbtEditor: https://github.com/IcySon55/3DLandMSBTeditor");
            logger.LogInformation("nus3audio:  https://github.com/jam1garner/nus3audio-rs");
            logger.LogInformation("bgm-property:  https://github.com/jam1garner/smash-bgm-property");
            logger.LogInformation("VGAudio:  https://github.com/Thealexbarney/VGAudio");
            logger.LogInformation("--------------------");
        }

        public static (string modDir, string seriesDir) PromptModAndSeries(
            IOptionsMonitor<Sma5hMusicOptions> musicConfig, ILogger logger)
        {
            var modPath = musicConfig.CurrentValue.Sma5hMusic.ModPath;
            Directory.CreateDirectory(modPath);

            var modDirs = Directory.GetDirectories(modPath, "*", SearchOption.TopDirectoryOnly)
                .Where(d => !Path.GetFileName(d).StartsWith("."))
                .ToList();

            if (modDirs.Count == 0)
            {
                logger.LogWarning("No mod folders found in {ModPath}.", modPath);
                return (null, null);
            }

            // Select mod
            string selectedModDir;
            if (modDirs.Count == 1)
            {
                selectedModDir = modDirs[0];
                logger.LogInformation("Using mod: {ModName}", Path.GetFileName(selectedModDir));
            }
            else
            {
                var modChoice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select a mod:")
                        .HighlightStyle(new Style(Color.Cyan1))
                        .AddChoices(modDirs.Select(d => Path.GetFileName(d))));
                selectedModDir = modDirs.First(d => Path.GetFileName(d) == modChoice);
            }

            // Select series
            var seriesDirs = Directory.GetDirectories(selectedModDir)
                .Where(d => !Path.GetFileName(d).StartsWith(".") && Path.GetFileName(d) != VALIDATE_FOLDER)
                .ToList();

            if (seriesDirs.Count == 0)
            {
                logger.LogWarning("No series folders found in {ModDir}.", selectedModDir);
                return (null, null);
            }

            string selectedSeriesDir;
            if (seriesDirs.Count == 1)
            {
                selectedSeriesDir = seriesDirs[0];
                logger.LogInformation("Using series: {SeriesName}", Path.GetFileName(selectedSeriesDir));
            }
            else
            {
                var seriesChoice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select a series:")
                        .HighlightStyle(new Style(Color.Cyan1))
                        .AddChoices(seriesDirs.Select(d => Path.GetFileName(d))));
                selectedSeriesDir = seriesDirs.First(d => Path.GetFileName(d) == seriesChoice);
            }

            return (selectedModDir, selectedSeriesDir);
        }
    }
}
