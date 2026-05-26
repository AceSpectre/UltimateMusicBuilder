using Xunit;

namespace Tests.Helpers
{
    /// <summary>
    /// Marker for tests that mutate process-wide globals — cwd, static
    /// <c>AnsiConsole.Console</c>, <c>Sma5hMusic.ExplicitSeriesOrder</c>,
    /// <c>FolderMusicMod.SeriesFilterByMod</c>, etc. Anything in this
    /// collection runs serially with every other test in the collection.
    /// </summary>
    [CollectionDefinition("CwdSensitive", DisableParallelization = true)]
    public class CwdSensitiveCollection { }
}
