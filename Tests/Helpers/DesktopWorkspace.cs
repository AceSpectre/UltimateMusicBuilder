using Microsoft.Extensions.Options;
using Moq;
using Sma5h;
using Sma5h.Mods.Music;

namespace Tests.Helpers
{
    /// <summary>
    /// A throwaway workspace with an empty Mods/MusicMods folder and options pointing at it, for
    /// the desktop-facing services. Needs no game resources (unlike <see cref="TestEnvironment"/>).
    /// </summary>
    public sealed class DesktopWorkspace : IDisposable
    {
        public string Root { get; }
        public string ModsRoot => Path.Combine(Root, "Mods", "MusicMods");
        public Sma5hMusicOptions Options { get; }

        public DesktopWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "umb-desktop-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(ModsRoot);
            Options = new Sma5hMusicOptions
            {
                GameResourcesPath = Path.Combine(Root, "Resources", "Game"),
                ResourcesPath = Path.Combine(Root, "Resources"),
                ToolsPath = Path.Combine(Root, "Tools"),
                TempPath = Path.Combine(Root, "Temp"),
                Sma5hMusic = new Sma5hMusicOptions.Sma5hMusicOptionsSection { ModPath = ModsRoot }
            };
        }

        public IOptionsMonitor<Sma5hMusicOptions> MusicOptions() => Monitor(Options);

        public IOptionsMonitor<Sma5hOptions> CoreOptions() => Monitor<Sma5hOptions>(Options);

        /// <summary>Creates (and returns) a folder under Mods/MusicMods.</summary>
        public string ModDir(params string[] segments)
        {
            var dir = Path.Combine(new[] { ModsRoot }.Concat(segments).ToArray());
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>Lays down Mods/MusicMods/&lt;mod&gt;/&lt;series&gt;/ with tracks.csv and optional TOML files.</summary>
        public string WriteSeries(string mod, string series, string tracksCsv, string seriesToml = null, string songOrderToml = null)
        {
            var dir = ModDir(mod, series);
            File.WriteAllText(Path.Combine(dir, "tracks.csv"), tracksCsv);
            if (seriesToml != null) File.WriteAllText(Path.Combine(dir, "series.toml"), seriesToml);
            if (songOrderToml != null) File.WriteAllText(Path.Combine(dir, "song_order.toml"), songOrderToml);
            return dir;
        }

        public static string WriteFile(string dir, string name, string content)
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
        }

        private static IOptionsMonitor<T> Monitor<T>(T value)
        {
            var mock = new Mock<IOptionsMonitor<T>>();
            mock.Setup(m => m.CurrentValue).Returns(value);
            return mock.Object;
        }
    }
}
