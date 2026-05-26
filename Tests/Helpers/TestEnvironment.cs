using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sma5h.Interfaces;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tests.Helpers
{
    public class TestEnvironment : IDisposable
    {
        public string TempDir { get; }
        public string ModPath { get; }
        public string RepoRoot { get; }

        private static readonly string TestDataDir = Path.Combine(
            AppContext.BaseDirectory, "TestData");

        public static readonly string[] DevTrackFiles = new[]
        {
            "flowerhead - Somewhat Good- Karts - 01 KARTS!.flac",
            "flowerhead - Somewhat Good- Karts - 02 Choose Your Racer.flac",
            "flowerhead - Somewhat Good- Karts - 03 Flowey Speedway.flac",
            "flowerhead - Somewhat Good- Karts - 04 Palmtree Square.flac",
            "flowerhead - Somewhat Good- Karts - 05 Bouncing Pyramids.flac",
            "flowerhead - Somewhat Good- Karts - 06 Retro Roundabout.flac",
            "flowerhead - Somewhat Good- Karts - 07 Stadium 64.flac",
            "flowerhead - Somewhat Good- Karts - 08 Highway Hustle.flac",
            "flowerhead - Somewhat Good- Karts - 09 Rainbow Way.flac",
            "flowerhead - Somewhat Good- Karts - 10 You Won!.flac",
            "flowerhead - Somewhat Good- Karts - 11 You Lost....flac",
            "flowerhead - Somewhat Good- Karts - 12 Race Results.flac",
            "flowerhead - Somewhat Good- Karts - 13 Time Trials.flac"
        };

        public static readonly string[] MarioTrackFiles = new[]
        {
            "flowerhead - Somewhat Good- Lofi - 01 summer.flac",
            "flowerhead - Somewhat Good- Lofi - 02 rainy day.flac",
            "flowerhead - Somewhat Good- Lofi - 03 brain empty.flac",
            "flowerhead - Somewhat Good- Lofi - 04 bird's song.flac",
            "flowerhead - Somewhat Good- Lofi - 05 stars & chill.flac",
            "flowerhead - Somewhat Good- Lofi - 06 7pm.flac"
        };

        public TestEnvironment()
        {
            SuiteEnvironmentCheck.ThrowIfInitFailed();
            SuiteEnvironmentCheck.Verify();

            TempDir = Path.Combine(Path.GetTempPath(), "umb-tests-" + Guid.NewGuid().ToString("N")[..8]);
            ModPath = Path.Combine(TempDir, "MusicMods");
            Directory.CreateDirectory(ModPath);
            RepoRoot = SuiteEnvironmentCheck.RepoRoot ?? FindRepoRoot();
        }

        /// <summary>
        /// Creates a fully configured mod. Copies real audio files from TestData if present,
        /// otherwise creates empty stubs so unit/integration tests still pass.
        /// </summary>
        public string CreateConfiguredMod(string modName = "test-mod")
        {
            var modDir = Path.Combine(ModPath, modName);
            var sourceDir = Path.Combine(TestDataDir, "configured-mod");

            CopyDirectory(sourceDir, modDir);

            // Only create stubs for files not already copied from TestData
            foreach (var file in DevTrackFiles)
            {
                var path = Path.Combine(modDir, "dev", file);
                if (!File.Exists(path))
                    File.WriteAllBytes(path, Array.Empty<byte>());
            }

            var iconPath = Path.Combine(modDir, "dev", "icon.png");
            if (!File.Exists(iconPath))
                File.WriteAllBytes(iconPath, CreateMinimalPng());

            foreach (var file in MarioTrackFiles)
            {
                var path = Path.Combine(modDir, "mario", file);
                if (!File.Exists(path))
                    File.WriteAllBytes(path, Array.Empty<byte>());
            }

            return modDir;
        }

        /// <summary>
        /// Creates an unconfigured mod laid out as it would be AFTER nus3-convert:
        /// each track exists as a .nus3audio stub (scaffold only recognizes audio
        /// extensions listed in MusicConstants.VALID_MUSIC_EXTENSIONS, which does
        /// not include .flac).
        /// </summary>
        public string CreateUnconfiguredMod(string modName = "scaffold-mod")
        {
            var modDir = Path.Combine(ModPath, modName);

            var devDir = Path.Combine(modDir, "dev");
            Directory.CreateDirectory(devDir);
            foreach (var flac in DevTrackFiles)
            {
                var stub = Path.Combine(devDir, Path.ChangeExtension(flac, ".nus3audio"));
                File.WriteAllBytes(stub, new byte[] { 0x4E, 0x55, 0x53, 0x33 });
            }

            var iconSrc = Path.Combine(TestDataDir, "configured-mod", "dev", "icon.png");
            var iconDst = Path.Combine(devDir, "icon.png");
            if (File.Exists(iconSrc))
                File.Copy(iconSrc, iconDst);
            else
                File.WriteAllBytes(iconDst, CreateMinimalPng());

            var marioDir = Path.Combine(modDir, "mario");
            Directory.CreateDirectory(marioDir);
            foreach (var flac in MarioTrackFiles)
            {
                var stub = Path.Combine(marioDir, Path.ChangeExtension(flac, ".nus3audio"));
                File.WriteAllBytes(stub, new byte[] { 0x4E, 0x55, 0x53, 0x33 });
            }

            return modDir;
        }

        public bool HasRealAudioFiles()
        {
            var testFile = Path.Combine(TestDataDir, "configured-mod", "dev", DevTrackFiles[0]);
            if (!File.Exists(testFile)) return false;
            return new FileInfo(testFile).Length > 0;
        }

        public bool HasGameResources()
        {
            return RepoRoot != null
                && File.Exists(Path.Combine(RepoRoot, "Resources", "Game",
                    "ui", "param", "database", "ui_bgm_db.prc"));
        }

        public IOptionsMonitor<Sma5hMusicOptions> CreateMusicOptions(
            string gameResourcesPath = null, string outputPath = null, string toolsPath = null)
        {
            var options = new Sma5hMusicOptions
            {
                GameResourcesPath = gameResourcesPath
                    ?? (RepoRoot != null ? Path.Combine(RepoRoot, "Resources", "Game") : Path.Combine(TempDir, "Resources", "Game")),
                OutputPath = outputPath ?? Path.Combine(TempDir, "ArcOutput"),
                ToolsPath = toolsPath
                    ?? (RepoRoot != null ? Path.Combine(RepoRoot, "Tools") : Path.Combine(TempDir, "Tools")),
                TempPath = Path.Combine(TempDir, "Temp"),
                ResourcesPath = RepoRoot != null ? Path.Combine(RepoRoot, "Resources") : Path.Combine(TempDir, "Resources"),
                LogPath = Path.Combine(TempDir, "Log"),
                Sma5hMusic = new Sma5hMusicOptions.Sma5hMusicOptionsSection
                {
                    ModPath = ModPath,
                    CachePath = Path.Combine(TempDir, "Cache"),
                    AudioConversionFormat = "idsp",
                    EnableAudioCaching = false,
                    DefaultLocale = "en_us",
                    GlobalVolumeMultiplier = 1.5f,
                    LufsNormalization = new Sma5hMusicOptions.LufsNormalizationOptions
                    {
                        Enabled = false
                    },
                    PlaylistMapping = new Sma5hMusicOptions.Sma5hMusicOptionsAutoPlaylistsSection
                    {
                        GenerationMode = Sma5hMusicOptions.PlaylistGeneration.Manual
                    }
                }
            };

            var mock = new Mock<IOptionsMonitor<Sma5hMusicOptions>>();
            mock.Setup(m => m.CurrentValue).Returns(options);
            return mock.Object;
        }

        /// <summary>
        /// Builds an IConfiguration suitable for the full DI container,
        /// pointing at real game resources and the test mod.
        /// </summary>
        public IConfiguration CreateConfiguration()
        {
            var settings = new Dictionary<string, string>
            {
                ["GameResourcesPath"] = RepoRoot != null
                    ? Path.Combine(RepoRoot, "Resources", "Game") : "",
                ["OutputPath"] = Path.Combine(TempDir, "ArcOutput"),
                ["ToolsPath"] = RepoRoot != null
                    ? Path.Combine(RepoRoot, "Tools") : "",
                ["TempPath"] = Path.Combine(TempDir, "Temp"),
                ["ResourcesPath"] = RepoRoot != null
                    ? Path.Combine(RepoRoot, "Resources") : "",
                ["LogPath"] = Path.Combine(TempDir, "Log"),
                ["Sma5hMusic:ModPath"] = ModPath,
                ["Sma5hMusic:CachePath"] = Path.Combine(TempDir, "Cache"),
                ["Sma5hMusic:AudioConversionFormat"] = "idsp",
                ["Sma5hMusic:EnableAudioCaching"] = "false",
                ["Sma5hMusic:DefaultLocale"] = "en_us",
                ["Sma5hMusic:GlobalVolumeMultiplier"] = "1.5",
                ["Sma5hMusic:LufsNormalization:Enabled"] = "false",
                ["Sma5hMusic:PlaylistMapping:GenerationMode"] = "Manual"
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();
        }

        /// <summary>
        /// Builds a full DI service provider with real state management
        /// but mocked audio processing services.
        /// </summary>
        public ServiceProvider CreateFullServiceProvider()
        {
            var config = CreateConfiguration();
            var services = new ServiceCollection();

            services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
            services.AddOptions();
            services.AddSingleton<IConfiguration>(config);

            services.AddSma5hCore(config);
            services.AddSma5hMusic(config);

            // Mock audio-generation services (nus3audio + LUFS) so tests don't
            // need to actually re-encode audio. IProcessService stays real so
            // bgm-property.exe / paracobNET-driven steps can run against the
            // tools folder verified by SuiteEnvironmentCheck.
            ReplaceService<INus3AudioService>(services, CreateMockNus3AudioService().Object);
            ReplaceService<ILufsAnalysisService>(services, new Mock<ILufsAnalysisService>().Object);

            return services.BuildServiceProvider();
        }

        public static Mock<IAudioMetadataService> CreateMockAudioMetadata()
        {
            var mock = new Mock<IAudioMetadataService>();
            mock.Setup(m => m.GetCuePoints(It.IsAny<string>()))
                .ReturnsAsync(new AudioCuePoints
                {
                    TotalSamples = 4800000,
                    TotalTimeMs = 100000,
                    LoopStartSample = 0,
                    LoopStartMs = 0,
                    LoopEndSample = 4800000,
                    LoopEndMs = 100000
                });
            return mock;
        }

        // Deterministic 64-byte stub written by the mock for every nus3 file so
        // the build pipeline emits real on-disk artefacts (the baseline manifest
        // records filename + size — those stay deterministic across runs).
        private static readonly byte[] _nus3Stub = Enumerable.Range(0, 64)
            .Select(i => (byte)(i & 0xFF)).ToArray();

        public static Mock<INus3AudioService> CreateMockNus3AudioService()
        {
            var mock = new Mock<INus3AudioService>();
            mock.Setup(m => m.GenerateNus3Bank(
                It.IsAny<string>(), It.IsAny<float>(), It.IsAny<string>()))
                .Returns((string _, float _, string outputMediaFile) =>
                {
                    WriteStub(outputMediaFile);
                    return true;
                });
            mock.Setup(m => m.GenerateNus3Audio(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string _, string _, string outputMediaFile) =>
                {
                    WriteStub(outputMediaFile);
                    return true;
                });
            return mock;
        }

        private static void WriteStub(string outputMediaFile)
        {
            if (string.IsNullOrEmpty(outputMediaFile)) return;
            var dir = Path.GetDirectoryName(outputMediaFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(outputMediaFile, _nus3Stub);
        }

        public static Mock<IAudioStateService> CreateMockAudioStateService()
        {
            var mock = new Mock<IAudioStateService>();
            mock.Setup(m => m.GetSeriesEntries()).Returns(Enumerable.Empty<SeriesEntry>());
            mock.Setup(m => m.GetGameTitleEntries()).Returns(Enumerable.Empty<GameTitleEntry>());
            mock.Setup(m => m.GetStagesEntries()).Returns(Enumerable.Empty<StageEntry>());
            return mock;
        }

        public static ILogger<T> CreateLogger<T>()
        {
            return new Mock<ILogger<T>>().Object;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(TempDir))
                    Directory.Delete(TempDir, true);
            }
            catch { }
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Sma5h.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
            foreach (var dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }

        private static void CopyRealOrStub(string sourceDir, string destDir, string[] fileNames)
        {
            foreach (var file in fileNames)
            {
                var src = Path.Combine(sourceDir, file);
                var dst = Path.Combine(destDir, file);
                if (File.Exists(src))
                    File.Copy(src, dst);
                else
                    File.WriteAllBytes(dst, Array.Empty<byte>());
            }
        }

        private static void ReplaceService<T>(IServiceCollection services, T replacement) where T : class
        {
            var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(T));
            if (descriptor != null)
                services.Remove(descriptor);
            services.AddSingleton(replacement);
        }

        private static byte[] CreateMinimalPng()
        {
            return Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVQI12NgAAIABQAB" +
                "Nl7BcQAAAABJRU5ErkJggg==");
        }
    }
}
