using Xunit;

namespace Tests.Helpers
{
    /// <summary>
    /// Marker for tests that mutate process-wide globals (cwd, static
    /// AnsiConsole, etc.) and must not run in parallel with anything else.
    /// </summary>
    [CollectionDefinition("CwdSensitive", DisableParallelization = true)]
    public class CwdSensitiveCollection { }
}
