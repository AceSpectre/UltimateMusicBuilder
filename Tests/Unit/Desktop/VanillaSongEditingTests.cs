using System.Text.Json;
using Moq;
using Sma5h.Interfaces;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Services;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Models;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using Tests.Helpers;
using UMB.CLI.Desktop;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Unit.Desktop
{
    public class VanillaSongEditingTests : IDisposable
    {
        private readonly DesktopWorkspace _ws = new();
        private readonly VanillaCatalogService _catalog;
        private readonly TrackOrderService _tracks;
        private readonly string _series;
        private readonly string _audio;

        public VanillaSongEditingTests()
        {
            _series = _ws.WriteSeries("mod", "mario", "filename,title,game\ncustom.flac,Custom,mario\n",
                "[series]\nid = \"mario\"\nexisting-series = true\n");
            _audio = DesktopWorkspace.WriteFile(_ws.Root, "game-song.nus3audio", "synthetic audio");
            var catalog = new Mock<VanillaCatalogService>(_ws.CoreOptions(), Mock.Of<IServiceProvider>());
            catalog.Setup(c => c.Get()).Returns(new VanillaCatalogService.Catalog
            {
                BgmTitles = new() { ["ui_bgm_one"] = "One", ["ui_bgm_two"] = "Two" },
                GameTitles = new(),
                Songs = new()
                {
                    new("ui_bgm_one", "info_one", "One", "ui_series_mario", "Composer", "Publisher", AudioPath: _audio, BaseVolume: 4),
                    new("ui_bgm_two", "info_two", "Two", "ui_series_mario"),
                    new("ui_bgm_other", "info_other", "Other", "ui_series_zelda")
                }
            });
            _catalog = catalog.Object;
            _tracks = new(_ws.MusicOptions(), _catalog, TestEnvironment.CreateLogger<TrackOrderService>());
        }

        public void Dispose() => _ws.Dispose();

        [Fact]
        public void Load_IncludesAllSeriesSongsWithoutOrderFile_AndStableEditableIds()
        {
            var items = _tracks.Load(_series).Items;
            Assert.Equal(new[] { "ui_bgm_custom", "ui_bgm_one", "ui_bgm_two" }, items.Select(i => i.BgmId));
            var vanilla = items[1];
            Assert.Equal("vanilla:ui_bgm_one", vanilla.Id);
            Assert.False(vanilla.IsLocked);
            Assert.Null(vanilla.OriginalIndex);
            Assert.Equal(("Composer", "Publisher", 1f, "info_one"),
                (vanilla.Fields.Author, vanilla.Fields.Copyright, vanilla.Fields.Volume, vanilla.InfoId));
        }

        [Fact]
        public void Save_RoundTripsVanillaOrderMetadataVolumeAndPinch_WithoutAddingAudioRows()
        {
            var data = _tracks.Load(_series);
            var one = data.Items.Single(i => i.BgmId == "ui_bgm_one");
            one.Fields.Title = "Renamed, One";
            one.Fields.Author = "";
            one.Fields.Copyright = "New publisher";
            one.Fields.Volume = .75f;
            one.Fields.Info1 = "info_two";
            one.Fields.SpecialCategory = "sf_situationlink";
            var saved = _tracks.Save(_series, data.Items.AsEnumerable().Reverse().Select(i => new SaveTrackItem(i.Id, i.Fields)).ToList());
            Assert.Equal(new[] { "ui_bgm_two", "ui_bgm_one", "ui_bgm_custom" }, saved.Items.Select(i => i.BgmId));
            var fields = saved.Items[1].Fields;
            Assert.Equal(("Renamed, One", "", "New publisher", .75f, "info_two", "sf_situationlink"),
                (fields.Title, fields.Author, fields.Copyright, fields.Volume, fields.Info1, fields.SpecialCategory));
            Assert.True(saved.Items[0].IsPinchTarget);
            Assert.Single(TracksCsv.Read(Path.Combine(_series, "tracks.csv")).rows);
            Assert.Contains("# vanilla", File.ReadAllText(Path.Combine(_series, "song_order.toml")));
        }

        [Fact]
        public void Save_UnchangedVanillaFieldsCreateNoOverrides_AndPreserveUnspecifiedModVolume()
        {
            var data = _tracks.Load(_series);
            _tracks.Save(_series, data.Items.Select(i => new SaveTrackItem(i.Id, i.Fields)).ToList());
            Assert.Empty(VanillaSongOverride.Read(_series));
            Assert.Equal("", Assert.Single(TracksCsv.Read(Path.Combine(_series, "tracks.csv")).rows).Get("volume"));
        }

        [Fact]
        public void FolderMod_LoadsVanillaOnlyOverridesAndResolvesModPinchTargets()
        {
            File.WriteAllText(Path.Combine(_series, "tracks.csv"), "filename,title,game\ncustom.flac,Custom,mario\n");
            File.AppendAllText(Path.Combine(_series, "series.toml"), "\n[[games]]\nid = \"mario\"\nname = \"Mario\"\n");
            File.WriteAllText(Path.Combine(_series, "custom.flac"), "synthetic audio");
            VanillaSongOverride.Write(_series, new[] { new VanillaSongOverride
            {
                BgmId = "ui_bgm_one", Title = "Changed", Volume = .5f,
                Info1 = "custom.flac", SpecialCategory = "sf_situationlink"
            }});
            var mod = new FolderMusicMod(TestEnvironment.CreateLogger<IMusicMod>(),
                TestEnvironment.CreateMockAudioMetadata().Object, Path.GetDirectoryName(_series));
            var entries = mod.GetMusicModEntries();
            Assert.Equal("info_custom", Assert.Single(entries.VanillaSongOverrides).Info1);
            File.WriteAllText(Path.Combine(_series, "tracks.csv"), "filename,title,game\n");
            VanillaSongOverride.Write(_series, new[] { new VanillaSongOverride { BgmId = "ui_bgm_one", Volume = .5f }});
            Assert.Single(mod.GetMusicModEntries().VanillaSongOverrides);
        }

        [Fact]
        public void Override_ChangesOnlyRequestedFieldsAndClearsLabelsAcrossLocales()
        {
            var root = new BgmDbRootEntry("ui_bgm_one") { NameId = "native_name", UiGameTitleId = "native_game", SaveNo = 12 };
            root.Title["us_en"] = "One";
            root.Author["us_en"] = "Composer";
            root.Author["jp_ja"] = "Japanese composer";
            var set = new BgmStreamSetEntry("set_one") { Info0 = "info_one", Info2 = "info_untouched" };
            new VanillaSongOverride { Author = "", Info1 = "info_two", SpecialCategory = "sf_situationlink" }.Apply(root, set);
            Assert.Equal("One", root.Title["us_en"]);
            Assert.All(root.Author.Values, text => Assert.Equal("", text));
            Assert.Equal(("native_name", "native_game", (short)12), (root.NameId, root.UiGameTitleId, root.SaveNo));
            Assert.Equal(("info_one", "info_two", "info_untouched", "sf_situationlink"), (set.Info0, set.Info1, set.Info2, set.SpecialCategory));
        }

        [Fact]
        public void VolumeTool_AnalyzesVanillaAudioAndSavesOverridesWithoutLosingMetadata()
        {
            var lufs = new Mock<ILufsAnalysisService>();
            lufs.Setup(l => l.Measure(_audio)).Returns(new LufsMeasurement { IsValid = true, IntegratedLufs = -20 });
            lufs.Setup(l => l.CalculateGain(It.IsAny<LufsMeasurement>(), It.IsAny<float>(), It.IsAny<float>())).Returns(new GainResult(2, false));
            var decoder = new Mock<IAudioDecodeService>();
            decoder.Setup(d => d.DecodeToWav(_audio, It.IsAny<string>())).Returns(true);
            var volume = new VolumeConfigService(_ws.MusicOptions(), lufs.Object, decoder.Object,
                TestEnvironment.CreateLogger<VolumeConfigService>(), _catalog);
            var output = Path.Combine(_ws.Root, "volume.json");
            volume.RunAnalyzeBatch(Input(new { seriesPath = _series, outputPath = output, analyze = true }));
            var rows = JsonSerializer.Deserialize<VolumeAnalyzeResultDto>(File.ReadAllText(output), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }).Items;
            var one = rows.Single(r => r.BgmId == "ui_bgm_one");
            Assert.True(one.HasMeasurement);
            Assert.Equal(2, one.AutoGain);
            Assert.Equal(-1, one.OriginalIndex);
            VanillaSongOverride.Write(_series, new[] { new VanillaSongOverride { BgmId = one.BgmId, Author = "New author" } });
            volume.RunSaveBatch(Input(new { seriesPath = _series, overrides = new[] { new { bgmId = one.BgmId, originalIndex = one.OriginalIndex, volume = .5f } } }));
            var saved = Assert.Single(VanillaSongOverride.Read(_series));
            Assert.Equal(("New author", .5f), (saved.Author, saved.Volume));
            Assert.Equal(.5f, _tracks.Load(_series).Items.Single(i => i.BgmId == one.BgmId).Fields.Volume);
            volume.RunPreviewBatch(Input(new { seriesPath = _series, filename = _audio, outputPath = Path.Combine(_ws.Root, "preview.wav") }));
            decoder.Verify(d => d.DecodeToWav(_audio, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public void VolumeTool_UsesNativeBankVolumeWithoutMeasurement_AndRejectsForeignSongs()
        {
            var volume = new VolumeConfigService(_ws.MusicOptions(), Mock.Of<ILufsAnalysisService>(l => l.MeasureCached(_audio) == new LufsMeasurement { IsValid = false }), Mock.Of<IAudioDecodeService>(),
                TestEnvironment.CreateLogger<VolumeConfigService>(), _catalog);
            var output = Path.Combine(_ws.Root, "volume.json");
            volume.RunAnalyzeBatch(Input(new { seriesPath = _series, outputPath = output }));
            var rows = JsonSerializer.Deserialize<VolumeAnalyzeResultDto>(File.ReadAllText(output), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }).Items;
            Assert.Equal(4, rows.Single(r => r.BgmId == "ui_bgm_one").AutoGain);
            Assert.Throws<InvalidDataException>(() => volume.RunSaveBatch(Input(new { seriesPath = _series,
                overrides = new[] { new { bgmId = "ui_bgm_other", volume = 1 } } })));
        }

        [Theory]
        [InlineData(false, 3f)]
        [InlineData(true, 1.5f)]
        public void Build_GeneratesVanillaBankAtConfiguredVolumeWithoutConvertingGameAudio(bool normalize, float expected)
        {
            _ws.Options.OutputPath = Path.Combine(_ws.Root, "output");
            _ws.Options.Sma5hMusic.PlaylistMapping = new() { GenerationMode = Sma5hMusicOptions.PlaylistGeneration.Manual };
            _ws.Options.Sma5hMusic.LufsNormalization.Enabled = normalize;
            var root = new BgmDbRootEntry("ui_bgm_one") { StreamSetId = "set_one" };
            var state = new Mock<IAudioStateService>();
            state.Setup(s => s.GetBgmDbRootEntries()).Returns(new[] { root });
            state.Setup(s => s.GetBgmStreamSetEntries()).Returns(new[] { new BgmStreamSetEntry("set_one") { Info0 = "info_one" } });
            state.Setup(s => s.GetBgmAssignedInfoEntries()).Returns(new[] { new BgmAssignedInfoEntry("info_one") { StreamId = "stream_one" } });
            state.Setup(s => s.GetBgmStreamPropertyEntries()).Returns(new[] { new BgmStreamPropertyEntry("stream_one") { DataName0 = "native_tone" } });
            state.Setup(s => s.GetBgmPropertyEntries()).Returns(new[] { new BgmPropertyEntry("native_tone", _audio) { AudioVolume = 4 } });
            var entries = new MusicModEntries();
            entries.VanillaSongOverrides.Add(new() { BgmId = root.UiBgmId, Volume = .5f });
            var mod = new Mock<IMusicMod>();
            mod.Setup(m => m.GetMusicModEntries()).Returns(entries);
            var manager = new Mock<IMusicModManagerService>();
            manager.Setup(m => m.RefreshMusicMods()).Returns(new[] { mod.Object });
            var lufs = new Mock<ILufsAnalysisService>();
            lufs.Setup(l => l.IsAvailable).Returns(true);
            lufs.Setup(l => l.Measure(_audio)).Returns(new LufsMeasurement { IsValid = true, IntegratedLufs = -20 });
            lufs.Setup(l => l.CalculateGain(It.IsAny<LufsMeasurement>(), It.IsAny<float>(), It.IsAny<float>())).Returns(new GainResult(2, false));
            var nus3 = new Mock<INus3AudioService>();
            var music = new Sma5hMusic(_ws.MusicOptions(), manager.Object, state.Object, nus3.Object,
                Mock.Of<IProcessService>(), lufs.Object, Mock.Of<IStateManager>(), TestEnvironment.CreateLogger<Sma5hMusic>());
            Assert.True(music.Init());
            Assert.True(music.Build(false));
            state.Verify(s => s.ApplyVanillaSongOverride(It.IsAny<VanillaSongOverride>()), Times.Once);
            nus3.Verify(n => n.GenerateNus3Bank("native_tone", expected, It.IsAny<string>()), Times.Once);
            nus3.Verify(n => n.GenerateNus3Audio(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void TrackSave_PreservesVolumeToolNormalizationOptInAtOne()
        {
            VanillaSongOverride.Write(_series, new[] { new VanillaSongOverride { BgmId = "ui_bgm_one", Volume = 1 } });
            var data = _tracks.Load(_series);
            _tracks.Save(_series, data.Items.Select(i => new SaveTrackItem(i.Id, i.Fields)).ToList());
            Assert.Equal(1, Assert.Single(VanillaSongOverride.Read(_series)).Volume);
        }

        [Fact]
        public void GeneratedVanillaBank_PreservesNativeBankId()
        {
            Directory.CreateDirectory(_ws.Options.ResourcesPath);
            Directory.CreateDirectory(Path.Combine(_ws.Options.ToolsPath, "Nus3Audio"));
            File.WriteAllText(Path.Combine(_ws.Options.ToolsPath, MusicConstants.Resources.NUS3AUDIO_EXE_FILE), "synthetic executable");
            File.WriteAllText(Path.Combine(_ws.Options.ResourcesPath, MusicConstants.Resources.NUS3BANK_IDS_FILE), "ID,NUS3Bank Name,Volume\n0x0005,bgm_native,4\n");
            var template = new byte[256];
            template[0x98] = 2;
            foreach (var offset in new[] { 0x20, 0x30, 0x40 }) { template[offset] = 0xE8; template[offset + 1] = 0x22; }
            File.WriteAllBytes(Path.Combine(_ws.Options.ResourcesPath, MusicConstants.Resources.NUS3BANK_TEMPLATE_FILE), template);
            var nus3 = new Nus3AudioService(_ws.MusicOptions(), Mock.Of<IAudioMetadataService>(), Mock.Of<IProcessService>(), TestEnvironment.CreateLogger<INus3AudioService>());
            var bank = Path.Combine(_ws.Root, "native.nus3bank");
            Assert.True(nus3.GenerateNus3Bank("native", .75f, bank));
            var bytes = File.ReadAllBytes(bank);
            Assert.Equal((ushort)5, BitConverter.ToUInt16(bytes, 0x9A));
            Assert.Equal(.75f, BitConverter.ToSingle(bytes, 0x34));
        }

        private string Input(object input) => DesktopWorkspace.WriteFile(_ws.Root, Guid.NewGuid() + ".json", JsonSerializer.Serialize(input));
    }
}
