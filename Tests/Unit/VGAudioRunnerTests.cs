using Sma5h.Mods.Music.Helpers;
using Xunit;

namespace Tests.Unit
{
    /// <summary>
    /// <see cref="VGAudioRunner"/> swaps the process-wide Console.Out, so it must always put the
    /// original writer back, even when calls overlap. Serial with other Console-touching tests.
    /// </summary>
    [Collection("CwdSensitive")]
    public class VGAudioRunnerTests
    {
        [Fact]
        public void RestoresConsoleOut_AndReturnsVGAudioOutput()
        {
            var original = Console.Out;

            var output = VGAudioRunner.Run("-i", Path.Combine(Path.GetTempPath(), "umb-missing.wav"), "-o", "unused.lopus");

            Assert.Same(original, Console.Out);
            Assert.False(string.IsNullOrWhiteSpace(output));
        }

        [Fact]
        public void RestoresConsoleOut_WhenCalledConcurrently()
        {
            var original = Console.Out;
            var missing = Path.Combine(Path.GetTempPath(), "umb-missing.wav");

            Parallel.For(0, 8, _ => VGAudioRunner.Run("-i", missing, "-o", "unused.lopus"));

            Assert.Same(original, Console.Out);
        }
    }
}
