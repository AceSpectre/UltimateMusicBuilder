using Microsoft.Extensions.DependencyInjection;
using Sma5h.Interfaces;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;

namespace Tests.Helpers
{
    /// <summary>
    /// Drives the real build pipeline against a TestEnvironment-prepared mod
    /// folder and writes the result into a baseline directory under the source
    /// tree (Tests/TestData/baselines/&lt;scenario&gt;/). Used only by the regen
    /// harness — normal tests never invoke this.
    /// </summary>
    public static class BaselineGenerator
    {
        public static string BaselineRoot(string repoRoot)
            => Path.Combine(repoRoot, "Tests", "TestData", "baselines");

        public static void SetupDefaultBuild(TestEnvironment env)
        {
            env.CreateConfiguredMod();
        }

        public static void SetupSeriesOrdered(TestEnvironment env)
        {
            var modDir = env.CreateConfiguredMod();
            AddSecondCustomSeries(modDir, "gamma");
            File.WriteAllText(Path.Combine(modDir, "series-order.toml"),
                "order = [\n    \"gamma\",\n    \"dev\",\n]\n");
        }

        public static void SetupTrackOrdered(TestEnvironment env)
        {
            var modDir = env.CreateConfiguredMod();
            ReverseDevOrderColumn(modDir);
            WriteMarioSongOrderToml(modDir);
        }

        public static void GenerateDefaultBuild(TestEnvironment env, string baselineDir)
        {
            SetupDefaultBuild(env);
            RunBuildAndCapture(env, baselineDir);
        }

        public static void GenerateSeriesOrdered(TestEnvironment env, string baselineDir)
        {
            SetupSeriesOrdered(env);
            RunBuildAndCapture(env, baselineDir);
        }

        public static void GenerateTrackOrdered(TestEnvironment env, string baselineDir)
        {
            SetupTrackOrdered(env);
            RunBuildAndCapture(env, baselineDir);
        }

        /// <summary>
        /// Runs the default build pipeline, then copies the generated
        /// series-icon BNTX out of ArcOutput into the baseline directory.
        /// Used as the input for ExtractIconsServiceTests.
        /// </summary>
        public static void GenerateExtractIconsSource(TestEnvironment env, string baselineDir)
        {
            SetupDefaultBuild(env);
            RunBuild(env);

            var sourceSeries0 = Path.Combine(env.TempDir, "ArcOutput", "ui", "replace", "series", "series_0");
            if (!Directory.Exists(sourceSeries0))
                throw new InvalidOperationException(
                    $"Expected build to produce {sourceSeries0} — extract-icons source baseline cannot be made.");

            var destSeries0 = Path.Combine(baselineDir, "ui", "replace", "series", "series_0");
            if (Directory.Exists(baselineDir))
                Directory.Delete(baselineDir, recursive: true);
            Directory.CreateDirectory(destSeries0);
            foreach (var bntx in Directory.GetFiles(sourceSeries0, "*.bntx"))
                File.Copy(bntx, Path.Combine(destSeries0, Path.GetFileName(bntx)), overwrite: true);
        }

        public static void GenerateNus3ConvertManifest(string producedDir, string baselineDir)
        {
            if (Directory.Exists(baselineDir))
                Directory.Delete(baselineDir, recursive: true);
            Directory.CreateDirectory(baselineDir);

            var manifest = BaselineComparer.BuildManifest(producedDir);
            var manifestPath = Path.Combine(baselineDir, BaselineComparer.ManifestFileName);
            File.WriteAllText(manifestPath,
                Newtonsoft.Json.JsonConvert.SerializeObject(manifest,
                    Newtonsoft.Json.Formatting.Indented));
        }

        /// <summary>
        /// Runs the build pipeline without writing a baseline. Tests use this
        /// to produce ArcOutput in the temp dir, then compare against a
        /// pre-committed baseline via <see cref="BaselineComparer"/>.
        /// </summary>
        public static void RunBuild(TestEnvironment env)
        {
            FolderMusicMod.SeriesFilterByMod = null;
            Sma5hMusic.ExplicitSeriesOrder = null;

            using var sp = env.CreateFullServiceProvider();
            var sma5hMod = sp.GetRequiredService<ISma5hMod>();
            var stateManager = sp.GetRequiredService<IStateManager>();

            ApplyExplicitSeriesOrder(env);

            stateManager.Init();
            if (!sma5hMod.Init())
                throw new InvalidOperationException("Sma5hMod.Init() returned false.");
            if (!sma5hMod.Build(useCache: false))
                throw new InvalidOperationException("Sma5hMod.Build() returned false.");
            if (!stateManager.WriteChanges())
                throw new InvalidOperationException("StateManager.WriteChanges() returned false.");
        }

        private static void RunBuildAndCapture(TestEnvironment env, string baselineDir)
        {
            RunBuild(env);

            var outputDir = Path.Combine(env.TempDir, "ArcOutput");
            if (Directory.Exists(baselineDir))
                Directory.Delete(baselineDir, recursive: true);
            BaselineComparer.WriteHashedAndManifest(outputDir, baselineDir);
        }

        public static void ApplyExplicitSeriesOrder(TestEnvironment env)
        {
            // BuildService normally reads series-order.toml; in the test
            // we apply the same logic directly.
            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var modDir in Directory.GetDirectories(env.ModPath))
            {
                var orderToml = Path.Combine(modDir, "series-order.toml");
                if (!File.Exists(orderToml)) continue;
                var model = Tomlyn.Toml.ToModel(File.ReadAllText(orderToml));
                if (!model.TryGetValue("order", out var arr) || arr is not Tomlyn.Model.TomlArray ta)
                    continue;
                int idx = 0;
                foreach (var entry in ta)
                {
                    if (entry is string id && !dict.ContainsKey(id))
                        dict[id] = idx++;
                }
            }
            Sma5hMusic.ExplicitSeriesOrder = dict.Count > 0 ? dict : null;
        }

        private static void AddSecondCustomSeries(string modDir, string seriesId)
        {
            var seriesDir = Path.Combine(modDir, seriesId);
            Directory.CreateDirectory(seriesDir);

            File.WriteAllText(Path.Combine(seriesDir, "series.toml"),
                $"[series]\n" +
                $"id = \"{seriesId}\"\n" +
                $"name = \"Gamma Test Series\"\n" +
                $"playlist-incidence = 100\n" +
                $"series-playlist = \"bgm_{seriesId}\"\n" +
                $"\n[[games]]\n" +
                $"id = \"{seriesId}_game\"\n" +
                $"name = \"Gamma Game\"\n" +
                $"\n[default-track-data]\n" +
                $"game = \"{seriesId}_game\"\n" +
                $"author = \"Test\"\n" +
                $"copyright = \"\"\n" +
                $"record-type = \"original\"\n" +
                $"volume = 1.0\n");

            File.WriteAllText(Path.Combine(seriesDir, "tracks.csv"),
                "filename,game,title,author,copyright,record_type,special_category,volume,info1,in_soundtest\n" +
                $"gamma_track_01.flac,{seriesId}_game,Gamma Track 1,Test,,original,,1.0,,True\n");

            File.WriteAllBytes(Path.Combine(seriesDir, "gamma_track_01.flac"), Array.Empty<byte>());
        }

        private static void ReverseDevOrderColumn(string modDir)
        {
            // FolderMusicMod doesn't read the CSV's `order` column — that's
            // only consumed by TrackOrderService's UI round-trip. What actually
            // drives the build's per-series TestDispOrder is the row order in
            // the CSV (stable sort over default short.MaxValue means CSV order
            // is preserved). So to make track-ordered baseline differ from
            // default-build, we have to physically reverse the data rows.
            var csvPath = Path.Combine(modDir, "dev", "tracks.csv");
            var lines = File.ReadAllLines(csvPath).ToList();
            var header = lines[0];
            var data = lines.Skip(1).ToList();
            data.Reverse();
            var rewritten = new List<string> { header };
            rewritten.AddRange(data);
            File.WriteAllLines(csvPath, rewritten);
        }

        private static void WriteMarioSongOrderToml(string modDir)
        {
            // Custom order interleaved with one vanilla Persona track (ps01).
            var path = Path.Combine(modDir, "mario", "song_order.toml");
            var ids = new[]
            {
                "ui_bgm_flowerhead___somewhat_good__lofi___03_brain_empty",
                "ui_bgm_ps01",
                "ui_bgm_flowerhead___somewhat_good__lofi___01_summer",
                "ui_bgm_flowerhead___somewhat_good__lofi___02_rainy_day",
                "ui_bgm_flowerhead___somewhat_good__lofi___04_bird_s_song",
                "ui_bgm_flowerhead___somewhat_good__lofi___05_stars___chill",
                "ui_bgm_flowerhead___somewhat_good__lofi___06_7pm",
            };
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("song_order = [");
            foreach (var id in ids)
                sb.AppendLine($"    \"{id}\",");
            sb.AppendLine("]");
            File.WriteAllText(path, sb.ToString());
        }
    }
}
