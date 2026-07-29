using Microsoft.Extensions.Options;
using Moq;
using Sma5h;
using Tests.Helpers;
using UMB.CLI;
using Xunit;

namespace Tests.Integration
{
    /// <summary>
    /// Exercises <see cref="WorkspaceManager.Init"/> — the ArcOutput reset step run
    /// at the start of every build. Covers create-when-missing, no-op-when-empty, and
    /// the non-interactive cleanup path (SkipOutputPathCleanupConfirmation), including
    /// read-only file handling.
    /// </summary>
    public class WorkspaceManagerTests : IDisposable
    {
        private readonly TestEnvironment _env;

        public WorkspaceManagerTests()
        {
            _env = new TestEnvironment();
        }

        public void Dispose() => _env.Dispose();

        private static IOptionsMonitor<Sma5hOptions> Options(string outputPath, bool skipConfirm = true)
        {
            var opts = new Sma5hOptions
            {
                OutputPath = outputPath,
                SkipOutputPathCleanupConfirmation = skipConfirm
            };
            var mock = new Mock<IOptionsMonitor<Sma5hOptions>>();
            mock.Setup(m => m.CurrentValue).Returns(opts);
            return mock.Object;
        }

        private WorkspaceManager Create(string outputPath, bool skipConfirm = true) =>
            new WorkspaceManager(Options(outputPath, skipConfirm),
                TestEnvironment.CreateLogger<IWorkspaceManager>());

        [Fact]
        public void Init_CreatesOutputDirWhenMissing()
        {
            var outputPath = Path.Combine(_env.TempDir, "ArcOutput");
            Assert.False(Directory.Exists(outputPath));

            var result = Create(outputPath).Init();

            Assert.True(result);
            Assert.True(Directory.Exists(outputPath));
        }

        [Fact]
        public void Init_ReturnsTrueWhenAlreadyEmpty()
        {
            var outputPath = Path.Combine(_env.TempDir, "ArcOutput");
            Directory.CreateDirectory(outputPath);

            var result = Create(outputPath).Init();

            Assert.True(result);
            Assert.True(Directory.Exists(outputPath));
        }

        [Fact]
        public void Init_ClearsExistingFilesAndSubdirs()
        {
            var outputPath = Path.Combine(_env.TempDir, "ArcOutput");
            Directory.CreateDirectory(outputPath);
            File.WriteAllText(Path.Combine(outputPath, "leftover.arc"), "stale");
            var sub = Path.Combine(outputPath, "sub");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "nested.bin"), "stale");

            var result = Create(outputPath, skipConfirm: true).Init();

            Assert.True(result);
            Assert.True(Directory.Exists(outputPath), "Root output folder should remain");
            Assert.Empty(Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetDirectories(outputPath));
        }

        [Fact]
        public void Init_StripsReadOnlyAttributeAndDeletes()
        {
            var outputPath = Path.Combine(_env.TempDir, "ArcOutput");
            Directory.CreateDirectory(outputPath);
            var ro = Path.Combine(outputPath, "readonly.bin");
            File.WriteAllText(ro, "locked");
            File.SetAttributes(ro, FileAttributes.ReadOnly);

            var result = Create(outputPath, skipConfirm: true).Init();

            Assert.True(result);
            Assert.False(File.Exists(ro), "Read-only leftover should be stripped and deleted");
        }
    }
}
