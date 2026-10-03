using Microsoft.Extensions.Logging;
using Sma5h.Helpers;
using Sma5h.Interfaces;
using System;
using System.Diagnostics;

namespace Sma5h
{
    public class ProcessService : IProcessService
    {
        private readonly ILogger<IProcessService> _logger;

        public ProcessService(ILogger<IProcessService> logger)
        {
            _logger = logger;
        }

        public void RunProcess(string executablePath, string arguments, Action<object, DataReceivedEventArgs> standardRedirect = null, Action<object, DataReceivedEventArgs> errorRedirect = null)
        {
            _logger.LogDebug($"Launching {executablePath} {arguments}");

            void onError(object sender, DataReceivedEventArgs data)
            {
                // When the caller passes errorRedirect they're taking responsibility
                // for stderr — don't auto-log every line as an error. Many tools
                // (ffmpeg, etc.) use stderr for normal informational output, and
                // unconditional LogError makes successful runs look like failures.
                if (errorRedirect != null)
                    errorRedirect.Invoke(sender, data);
                else
                    _logger.LogError("Error while running {Executable} with arguments {Arguments} - {Error}", executablePath, arguments, data.Data);
            }
            void onInfo(object sender, DataReceivedEventArgs data)
            {
                _logger.LogDebug("{Executable}: {Data}", executablePath, data.Data);
                standardRedirect?.Invoke(sender, data);
            }

            ProcessRunner.Run(executablePath, arguments, onStdout: onInfo, onStderr: onError);
        }
    }
}
