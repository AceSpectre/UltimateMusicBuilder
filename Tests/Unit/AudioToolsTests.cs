using Sma5h.Helpers;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Unit
{
    public class AudioToolsTests
    {
        [Theory]
        [InlineData(0, 120)]
        [InlineData(60, 120)]
        [InlineData(890, 890)]
        public void PymusiclooperTimeout_ScalesWithTrackLength(double durationSeconds, double expectedSeconds)
        {
            Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), AudioTools.PymusiclooperTimeout(durationSeconds));
        }

        [Theory]
        [InlineData(0, "4841381 39782858 0.006 0.011 0.99\n", null)]
        [InlineData(0, "\nERROR    No loop points found for \"song.flac\" with current parameters.   \n", null)]
        [InlineData(0, "\nERROR    float division by zero      \n", "float division by zero")]
        [InlineData(2, "", "exited with code 2")]
        public void FailureMessage_ReportsErrorsButNotMissingLoops(int exitCode, string stdout, string expected)
        {
            Assert.Equal(expected, AudioTools.FailureMessage(new ProcessResult(exitCode, stdout, "")));
        }
    }
}
