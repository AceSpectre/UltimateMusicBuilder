using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sma5h;
using Sma5h.Attributes;
using Sma5h.Interfaces;
using Xunit;

namespace Tests.Unit
{
    /// <summary>
    /// <see cref="StateManager.WriteChanges"/> output naming for localised MSBTs.
    /// A single loaded locale is written locale-less (msg_bgm.msbt); several loaded
    /// locales each keep their +locale suffix so they don't overwrite one another.
    /// Uses a recording fake provider, so no game resources are needed.
    /// </summary>
    public class StateManagerWriteChangesTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _gamePath;
        private readonly string _outputPath;
        private readonly RecordingMsbtProvider _msbtProvider = new();
        private readonly RecordingPrcProvider _prcProvider = new();

        public StateManagerWriteChangesTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "umb-statemgr-" + Guid.NewGuid().ToString("N")[..8]);
            _gamePath = Path.Combine(_tempDir, "Game");
            _outputPath = Path.Combine(_tempDir, "ArcOutput");
            Directory.CreateDirectory(_gamePath);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        private StateManager CreateStateManager(params string[] resourceKeys)
        {
            foreach (var key in resourceKeys)
            {
                var file = Path.Combine(_gamePath, key);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllBytes(file, Array.Empty<byte>());
            }

            var services = new ServiceCollection();
            services.AddSingleton<IResourceProvider>(_msbtProvider);
            services.AddSingleton<IResourceProvider>(_prcProvider);

            var opts = new Sma5hOptions { GameResourcesPath = _gamePath, OutputPath = _outputPath };
            var config = new Mock<IOptionsMonitor<Sma5hOptions>>();
            config.Setup(m => m.CurrentValue).Returns(opts);

            var state = new StateManager(services.BuildServiceProvider(), config.Object,
                NullLogger<IStateManager>.Instance);
            foreach (var key in resourceKeys)
                state.LoadResource<FakeDb>(key, optional: false);
            return state;
        }

        private List<string> WrittenFiles(RecordingProvider provider) =>
            provider.Writes
                .Select(p => Path.GetRelativePath(_outputPath, p.OutputFile).Replace('\\', '/'))
                .OrderBy(p => p)
                .ToList();

        [Fact]
        public void SingleLocale_WritesLocaleLessFile()
        {
            var state = CreateStateManager(
                "ui/message/msg_bgm+us_en.msbt",
                "ui/message/msg_title+us_en.msbt");

            Assert.True(state.WriteChanges());

            Assert.Equal(
                new[] { "ui/message/msg_bgm.msbt", "ui/message/msg_title.msbt" },
                WrittenFiles(_msbtProvider));
        }

        [Fact]
        public void SeveralLocales_KeepLocaleSuffixPerFile()
        {
            var state = CreateStateManager(
                "ui/message/msg_bgm+eu_fr.msbt",
                "ui/message/msg_bgm+us_en.msbt",
                "ui/message/msg_title+eu_fr.msbt",
                "ui/message/msg_title+us_en.msbt");

            Assert.True(state.WriteChanges());

            Assert.Equal(
                new[]
                {
                    "ui/message/msg_bgm+eu_fr.msbt",
                    "ui/message/msg_bgm+us_en.msbt",
                    "ui/message/msg_title+eu_fr.msbt",
                    "ui/message/msg_title+us_en.msbt",
                },
                WrittenFiles(_msbtProvider));
        }

        [Fact]
        public void LocaleCountIsPerFile_NotGlobal()
        {
            // msg_bgm has two locales, msg_title only one: each decides independently.
            var state = CreateStateManager(
                "ui/message/msg_bgm+eu_fr.msbt",
                "ui/message/msg_bgm+us_en.msbt",
                "ui/message/msg_title+us_en.msbt");

            Assert.True(state.WriteChanges());

            Assert.Equal(
                new[]
                {
                    "ui/message/msg_bgm+eu_fr.msbt",
                    "ui/message/msg_bgm+us_en.msbt",
                    "ui/message/msg_title.msbt",
                },
                WrittenFiles(_msbtProvider));
        }

        [Fact]
        public void NonLocalisedResources_KeepTheirPath()
        {
            var state = CreateStateManager(
                "ui/param/database/ui_bgm_db.prc",
                "ui/message/msg_bgm+eu_fr.msbt",
                "ui/message/msg_bgm+us_en.msbt");

            Assert.True(state.WriteChanges());

            Assert.Equal(new[] { "ui/param/database/ui_bgm_db.prc" }, WrittenFiles(_prcProvider));
        }

        [Fact]
        public void SeveralLocales_ReadFromMatchingInputFile()
        {
            var state = CreateStateManager(
                "ui/message/msg_bgm+eu_fr.msbt",
                "ui/message/msg_bgm+us_en.msbt");

            Assert.True(state.WriteChanges());

            Assert.All(_msbtProvider.Writes, w =>
                Assert.Equal(Path.GetFileName(w.InputFile), Path.GetFileName(w.OutputFile)));
        }

        public class FakeDb : IStateManagerDb { }

        public abstract class RecordingProvider : IResourceProvider
        {
            public List<(string InputFile, string OutputFile)> Writes { get; } = new();

            public T ReadFile<T>(string inputFile) where T : IStateManagerDb, new() => new T();

            public bool WriteFile<T>(string inputFile, string outputFile, T inputObj) where T : IStateManagerDb
            {
                Writes.Add((inputFile, outputFile));
                return true;
            }
        }

        [ResourceProviderMatch(".msbt")]
        public class RecordingMsbtProvider : RecordingProvider { }

        [ResourceProviderMatch(".prc")]
        public class RecordingPrcProvider : RecordingProvider { }
    }
}
