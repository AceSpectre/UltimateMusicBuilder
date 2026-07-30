using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;

using Sma5h;

namespace UMB.CLI
{
    public class WorkspaceManager : IWorkspaceManager
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<Sma5hOptions> _config;

        public WorkspaceManager(IOptionsMonitor<Sma5hOptions> config, ILogger<IWorkspaceManager> logger)
        {
            _logger = logger;
            _config = config;
        }

        public bool Init()
        {
            _logger.LogDebug("Initialize workspace");

            try
            {
                var outputPath = _config.CurrentValue.OutputPath;

                if (!Directory.Exists(outputPath))
                {
                    _logger.LogDebug("Creating Working folder...");
                    Directory.CreateDirectory(outputPath);
                    return true;
                }

                var existingFiles = Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories);
                var existingDirs = Directory.GetDirectories(outputPath, "*", SearchOption.AllDirectories);
                if (existingFiles.Length == 0 && existingDirs.Length == 0)
                    return true;

                // Non-interactive callers (e.g. the desktop app) have stdin redirected and
                // cannot answer a prompt; auto-confirm the cleanup for them.
                if (!_config.CurrentValue.SkipOutputPathCleanupConfirmation && !Console.IsInputRedirected)
                {
                    _logger.LogWarning("Files found in the workspace folder, delete? Y/N (Default: N)");
                    var response = Console.ReadKey();
                    if (response.KeyChar != 'y' && response.KeyChar != 'Y')
                        return false;
                }

                CleaningWorkspaceFolder(outputPath);
                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Workspace error");
                return false;
            }
        }

        /// <summary>
        /// Fully clears the workspace: deletes every file (regardless of extension) and every
        /// subdirectory under <paramref name="outputPath"/>, leaving the root folder itself in place.
        /// </summary>
        private void CleaningWorkspaceFolder(string outputPath)
        {
            _logger.LogInformation("Cleaning up workspace folder");

            int filesDeleted = 0;
            foreach (var file in Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    // Strip the read-only attribute in case anything copied in is flagged.
                    var attrs = File.GetAttributes(file);
                    if ((attrs & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
                    File.Delete(file);
                    filesDeleted++;
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Failed to delete file {File}", file);
                }
            }

            int dirsDeleted = 0;
            foreach (var dir in Directory.GetDirectories(outputPath, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                    dirsDeleted++;
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Failed to delete directory {Dir}", dir);
                }
            }

            _logger.LogInformation("Cleared {Files} file(s) and {Dirs} top-level subfolder(s) from {OutputPath}",
                filesDeleted, dirsDeleted, outputPath);
        }
    }
}
