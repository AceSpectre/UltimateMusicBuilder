using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.MusicMods.MusicModModels;
using Sma5h.Mods.Music.MusicOverride.MusicOverrideConfigModels;
using Xunit;

namespace Tests.Unit
{
    /// <summary>
    /// Maps the raw "Unk*" hash fields (as they appear in the PRC) onto their
    /// named counterparts. Each assignment is guarded by a null check, so a set
    /// value flows through and an unset (null) one leaves the target unchanged.
    /// </summary>
    public class CrackedHashValueConverterTests
    {
        [Fact]
        public void BgmDbRoot_SetValuesFlowToNamedFields()
        {
            var cfg = new BgmDbRootConfig
            {
                Unk1 = true,
                Unk2 = true,
                Unk3 = "ui_chara_x",
                Unk4 = "hat_motif",
                Unk5 = "body_motif",
            };

            CrackedHashValueConverter.UpdateBgmDbRootConfig(cfg);

            Assert.True(cfg.IsSelectableMovieEdit);
            Assert.True(cfg.IsSelectableOriginal);
            Assert.Equal("ui_chara_x", cfg.DlcUiCharaId);
            Assert.Equal("hat_motif", cfg.DlcMiiHatMotifId);
            Assert.Equal("body_motif", cfg.DlcMiiBodyMotifId);
        }

        [Fact]
        public void BgmDbRoot_NullUnksLeaveTargetsUnchanged()
        {
            var cfg = new BgmDbRootConfig
            {
                IsSelectableMovieEdit = false,
                DlcUiCharaId = "keep-me",
                // Unk* left null
            };

            CrackedHashValueConverter.UpdateBgmDbRootConfig(cfg);

            Assert.False(cfg.IsSelectableMovieEdit);
            Assert.Equal("keep-me", cfg.DlcUiCharaId);
        }

        [Fact]
        public void Stage_SetValuesFlowToNamedFields()
        {
            var cfg = new StageConfig
            {
                Unk2 = true,
                Unk3 = true,
                Unk4 = true,
            };

            CrackedHashValueConverter.UpdateStageConfig(cfg);

            Assert.True(cfg.IsUsableFlag);
            Assert.True(cfg.IsUsableAmiibo);
            Assert.True(cfg.BgmSelector);
        }

        [Fact]
        public void Stage_NullUnksLeaveTargetsUnchanged()
        {
            var cfg = new StageConfig { BgmSelector = false };

            CrackedHashValueConverter.UpdateStageConfig(cfg);

            Assert.False(cfg.BgmSelector);
        }
    }
}
