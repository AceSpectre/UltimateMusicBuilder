using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sma5h.Interfaces;
using Sma5h.Mods.Music;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Sma5h.CLI.Services
{
    public class BuildService
    {
        private readonly ILogger _logger;
        private readonly IStateManager _state;
        private readonly IServiceProvider _serviceProvider;
        private readonly IWorkspaceManager _workspace;
        private readonly IOptionsMonitor<Sma5hMusicOptions> _musicConfig;

        public BuildService(IServiceProvider serviceProvider, IWorkspaceManager workspace, IStateManager state,
            IOptionsMonitor<Sma5hMusicOptions> musicConfig, ILogger<BuildService> logger)
        {
            _serviceProvider = serviceProvider;
            _workspace = workspace;
            _state = state;
            _musicConfig = musicConfig;
            _logger = logger;
        }

        public async Task Run()
        {
            Script.PrintBanner(_logger);

            var modPath = _musicConfig.CurrentValue.Sma5hMusic.ModPath;
            Directory.CreateDirectory(modPath);

            var modDirs = Directory.GetDirectories(modPath, "*", SearchOption.TopDirectoryOnly)
                .Where(d => !Path.GetFileName(d).StartsWith("."))
                .ToList();

            if (modDirs.Count == 0)
            {
                _logger.LogWarning("No mod folders found in {ModPath}.", modPath);
                return;
            }

            // Let user pick which mod to build
            string selectedMod = null;
            if (modDirs.Count == 1)
            {
                selectedMod = Path.GetFileName(modDirs[0]);
                _logger.LogInformation("Building mod: {ModName}", selectedMod);
            }
            else
            {
                var choices = modDirs.Select(d => Path.GetFileName(d)).ToList();
                choices.Insert(0, "All");

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select a mod to build:")
                        .HighlightStyle(new Style(Color.Cyan1))
                        .AddChoices(choices));

                if (choice != "All")
                    selectedMod = choice;
            }

            // Temporarily disable unselected mods by prefixing with '.'
            var disabledDirs = new List<(string original, string disabled)>();
            if (selectedMod != null)
            {
                foreach (var dir in modDirs)
                {
                    var name = Path.GetFileName(dir);
                    if (!name.Equals(selectedMod, StringComparison.OrdinalIgnoreCase))
                    {
                        var disabledPath = Path.Combine(Path.GetDirectoryName(dir), "." + name);
                        Directory.Move(dir, disabledPath);
                        disabledDirs.Add((dir, disabledPath));
                    }
                }
            }

            try
            {
                await Task.Delay(1000);

                //Init State Manager
                _state.Init();

                //Init workspace
                if (!_workspace.Init())
                    return;

                //Load Mods
                var mods = _serviceProvider.GetServices<ISma5hMod>();

                //Step that initialize a mod
                _logger.LogInformation("--------------------");
                var initMods = new List<ISma5hMod>();
                foreach (var mod in mods)
                {
                    _logger.LogInformation("{ModeName}: Initialize mod", mod.ModName);
                    if (mod.Init())
                        initMods.Add(mod);
                }

                //Step to activate an eventual build step for a mod.
                _logger.LogInformation("--------------------");
                foreach (var mod in initMods)
                {
                    _logger.LogInformation("{ModeName}; Build mod changes", mod.ModName);
                    mod.Build();
                }

                //Generate Output mod
                _logger.LogInformation("--------------------");
                _logger.LogInformation("Starting State Manager Mod Generation");
                _state.WriteChanges();
                _logger.LogInformation("COMPLETE - Please check the logs for any error.");
                _logger.LogInformation("--------------------");
            }
            finally
            {
                // Re-enable disabled mods
                foreach (var (original, disabled) in disabledDirs)
                {
                    if (Directory.Exists(disabled))
                        Directory.Move(disabled, original);
                }
            }
        }
    }
}
