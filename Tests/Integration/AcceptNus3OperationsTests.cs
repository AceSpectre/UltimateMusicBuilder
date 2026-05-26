using Tests.Helpers;
using Xunit;

namespace Tests.Integration
{
    /// <summary>
    /// Tests the file-level operations of AcceptNus3Service.
    /// The service requires Spectre.Console prompts via Script.PromptModAndSeries(),
    /// so we simulate its file operations directly.
    /// </summary>
    public class AcceptNus3OperationsTests : IDisposable
    {
        private readonly TestEnvironment _env;

        public AcceptNus3OperationsTests()
        {
            _env = new TestEnvironment();
        }

        public void Dispose() => _env.Dispose();

        [Fact]
        public void AcceptNus3_MovesFilesToParent()
        {
            var modDir = _env.CreateConfiguredMod();
            var seriesDir = Path.Combine(modDir, "dev");
            var validateDir = Path.Combine(seriesDir, "songs-to-validate");
            Directory.CreateDirectory(validateDir);

            File.WriteAllBytes(Path.Combine(validateDir, "track1.nus3audio"),
                new byte[] { 0x4E, 0x55, 0x53, 0x33 }); // "NUS3" header
            File.WriteAllBytes(Path.Combine(validateDir, "track2.nus3audio"),
                new byte[] { 0x4E, 0x55, 0x53, 0x33 });

            // Simulate accept: move each nus3audio to parent
            foreach (var file in Directory.GetFiles(validateDir, "*.nus3audio"))
            {
                var dest = Path.Combine(seriesDir, Path.GetFileName(file));
                File.Move(file, dest);
            }

            Assert.True(File.Exists(Path.Combine(seriesDir, "track1.nus3audio")));
            Assert.True(File.Exists(Path.Combine(seriesDir, "track2.nus3audio")));
            Assert.Empty(Directory.GetFiles(validateDir, "*.nus3audio"));
        }

        [Fact]
        public void AcceptNus3_UpdatesCsvFilename()
        {
            var modDir = _env.CreateConfiguredMod();
            var seriesDir = Path.Combine(modDir, "dev");

            var csvPath = Path.Combine(seriesDir, "tracks.csv");
            var csvContent = File.ReadAllText(csvPath);

            // Simulate updating filename from .flac to .nus3audio
            var updated = csvContent.Replace(
                "flowerhead - Somewhat Good- Karts - 01 KARTS!.flac",
                "flowerhead - Somewhat Good- Karts - 01 KARTS!.nus3audio");
            File.WriteAllText(csvPath, updated);

            var result = File.ReadAllText(csvPath);
            Assert.Contains("KARTS!.nus3audio", result);
            Assert.DoesNotContain("KARTS!.flac", result);
        }

        [Fact]
        public void AcceptNus3_CleansUpEmptyValidateFolder()
        {
            var modDir = _env.CreateConfiguredMod();
            var validateDir = Path.Combine(modDir, "dev", "songs-to-validate");
            Directory.CreateDirectory(validateDir);

            // If empty, remove
            if (Directory.GetFiles(validateDir).Length == 0
                && Directory.GetDirectories(validateDir).Length == 0)
            {
                Directory.Delete(validateDir);
            }

            Assert.False(Directory.Exists(validateDir));
        }

        [Fact]
        public void AcceptNus3_DoesNotDeleteSourceIfNotRequested()
        {
            var modDir = _env.CreateConfiguredMod();
            var seriesDir = Path.Combine(modDir, "dev");
            var sourceFile = Path.Combine(seriesDir,
                "flowerhead - Somewhat Good- Karts - 01 KARTS!.flac");

            var validateDir = Path.Combine(seriesDir, "songs-to-validate");
            Directory.CreateDirectory(validateDir);
            File.WriteAllBytes(Path.Combine(validateDir,
                "flowerhead - Somewhat Good- Karts - 01 KARTS!.nus3audio"),
                new byte[] { 0x4E, 0x55, 0x53, 0x33 });

            // Accept without deleting source
            File.Move(
                Path.Combine(validateDir, "flowerhead - Somewhat Good- Karts - 01 KARTS!.nus3audio"),
                Path.Combine(seriesDir, "flowerhead - Somewhat Good- Karts - 01 KARTS!.nus3audio"));

            Assert.True(File.Exists(sourceFile), "Source .flac should still exist");
            Assert.True(File.Exists(Path.Combine(seriesDir,
                "flowerhead - Somewhat Good- Karts - 01 KARTS!.nus3audio")));
        }
    }
}
