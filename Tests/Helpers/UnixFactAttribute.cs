using System;
using Xunit;

namespace Tests.Helpers
{
    /// <summary>A fact that is reported as skipped on Windows, where creating symlinks needs elevation.</summary>
    public sealed class UnixFactAttribute : FactAttribute
    {
        public UnixFactAttribute()
        {
            if (OperatingSystem.IsWindows()) Skip = "Requires Unix symlinks.";
        }
    }
}
