using AutoMapper;
using Moq;
using Sma5h.Interfaces;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Models;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using Sma5h.Mods.Music.Services;
using Tests.Helpers;
using Xunit;

namespace Tests.Unit
{
    public class VanillaSeriesBuildTests : IDisposable
    {
        private readonly DesktopWorkspace _ws = new();
        public void Dispose() => _ws.Dispose();

        [Fact]
        public void ExistingSeries_IconOnlyOverrideReachesCoreEntryWithoutReplacingMetadata()
        {
            var dir = _ws.WriteSeries("mod", "splatoon", "filename,game,title\n",
                "[series]\nid = \"splatoon\"\nexisting-series = true\n");
            var icon = Path.Combine(dir, "icon.png");
            File.WriteAllBytes(icon, new byte[] { 1, 2, 3 });
            var entries = LoadMod();
            var replacement = Assert.Single(entries.SeriesEntries);
            Assert.Equal(icon, replacement.IconPath);
            Assert.Empty(entries.GameTitleEntries);

            var state = new AudioStateService(_ws.MusicOptions(), Mock.Of<IMapper>(), Mock.Of<IStateManager>(),
                TestEnvironment.CreateLogger<IAudioStateService>());
            var core = new SeriesEntry("ui_series_splatoon") { NameId = "splatoon", DispOrderSound = 30, SaveNo = 12 };
            core.MSBTTitle["en_us"] = "Splatoon";
            Assert.True(state.AddSeriesEntry(core));
            Assert.True(state.AddSeriesEntry(replacement));
            Assert.Same(core, Assert.Single(state.GetSeriesEntries()));
            Assert.Equal(icon, core.IconPath);
            Assert.Equal(("splatoon", (sbyte)30, (sbyte)12, EntrySource.Core), (core.NameId, core.DispOrderSound, core.SaveNo, core.Source));
            Assert.Equal("Splatoon", core.MSBTTitle["en_us"]);
        }

        [Fact]
        public void ExistingSeries_NewGameLoadsWithoutRecreatingSeries()
        {
            _ws.WriteSeries("mod", "splatoon", "filename,game,title\n",
                "[series]\nid = \"splatoon\"\nexisting-series = true\n\n[[games]]\nid = \"splatoon_3\"\nname = \"Splatoon 3\"\n");
            var entries = LoadMod();
            Assert.Empty(entries.SeriesEntries);
            var game = Assert.Single(entries.GameTitleEntries);
            Assert.Equal("ui_gametitle_splatoon_3", game.UiGameTitleId);
            Assert.Equal("ui_series_splatoon", game.UiSeriesId);
            Assert.Equal("Splatoon 3", game.MSBTTitle["en_us"]);
        }

        private MusicModEntries LoadMod() => new FolderMusicMod(
            TestEnvironment.CreateLogger<IMusicMod>(), Mock.Of<IAudioMetadataService>(), _ws.ModDir("mod")).GetMusicModEntries();
    }
}
