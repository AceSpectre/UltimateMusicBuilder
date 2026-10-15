using Microsoft.Extensions.Options;
using Sma5h;
using Sma5h.Helpers;
using Sma5h.ResourceProviders.Constants;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace UMB.CLI.Services
{
    /// <summary>Checks the external tools and game files the app needs before a build or conversion.</summary>
    public class SetupCheckService
    {
        /// <summary>Version is the tool's first output line, or null when it can't be run.</summary>
        public record ToolStatus(bool Ok, string Version);
        public record ResourcesStatus(bool Ok, string Path, List<string> Missing);
        public record SetupStatus(Dictionary<string, ToolStatus> Tools, ResourcesStatus Resources);

        private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(30);

        private static readonly (string Name, string VersionArgument)[] Tools =
            { ("uv", "--version"), ("pymusiclooper", "--version"), ("ffmpeg", "-version") };

        // Game files the build reads from Resources/Game.
        private static readonly string[] GameFiles =
        {
            PrcExtConstants.PRC_UI_BGM_DB_PATH,
            PrcExtConstants.PRC_UI_GAMETITLE_DB_PATH,
            PrcExtConstants.PRC_UI_STAGE_DB_PATH,
            string.Format(MsbtExtConstants.MSBT_BGM, "us_en"),
            BgmPropertyFileConstants.BGM_PROPERTY_PATH
        };

        private readonly IOptionsMonitor<Sma5hOptions> _config;

        public SetupCheckService(IOptionsMonitor<Sma5hOptions> config) => _config = config;

        public SetupStatus Check() => new(
            Tools.ToDictionary(t => t.Name, t => CheckTool(t.Name, t.VersionArgument)),
            CheckResources(_config.CurrentValue.GameResourcesPath));

        public static ToolStatus CheckTool(string name, string versionArgument)
        {
            try
            {
                var result = ProcessRunner.Run(AudioTools.Tool(name), versionArgument, ToolTimeout);
                // ffmpeg's first line also carries its copyright notice.
                var version = result.StandardOutput.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l != "")?.Split(" Copyright")[0];
                return result.ExitCode == 0 ? new ToolStatus(true, version) : new ToolStatus(false, null);
            }
            catch (Exception ex) when (ex is Win32Exception or TimeoutException)
            {
                return new ToolStatus(false, null);
            }
        }

        public static ResourcesStatus CheckResources(string gameResourcesPath)
        {
            var root = Path.GetFullPath(gameResourcesPath);
            var missing = GameFiles.Where(f => !File.Exists(Path.Combine(root, f))).ToList();
            return new ResourcesStatus(missing.Count == 0, root, missing);
        }
    }
}
