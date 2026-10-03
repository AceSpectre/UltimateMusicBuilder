using Riok.Mapperly.Abstractions;
using Sma5h.Data.Ui.Param.Database.PrcUiBgmDatabaseModels;
using Sma5h.Data.Ui.Param.Database.PrcUiGameTitleDatabaseModels;
using Sma5h.Data.Ui.Param.Database.PrcUiSeriesDatabaseModels;
using Sma5h.Data.Ui.Param.Database.PrcUiStageDatabaseModels;
using Sma5h.Mods.Music.Models.PlaylistEntryModels;
using Sma5h.Mods.Music.MusicMods.MusicModModels;
using Sma5h.Mods.Music.MusicOverride.MusicOverrideConfigModels;
using System.Collections.Generic;
using BinBgmPropertyEntry = Sma5h.Mods.Data.Sound.Config.BgmPropertyStructs.BgmPropertyEntry;

namespace Sma5h.Mods.Music.Models
{
    // Compile-time generated mappings between game data (prc/bin), state entries and json configs.
    [Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
    public static partial class MusicMapper
    {
        // Game data -> entries
        [MapperIgnoreTarget(nameof(SeriesEntry.MSBTTitle))]
        [MapperIgnoreTarget(nameof(SeriesEntry.IconPath))]
        [MapperIgnoreTarget(nameof(SeriesEntry.MusicMod))]
        public static partial void Map(PrcSeriesDbRootEntry source, [MappingTarget] SeriesEntry target);
        [MapperIgnoreTarget(nameof(GameTitleEntry.MSBTTitle))]
        [MapperIgnoreTarget(nameof(GameTitleEntry.MusicMod))]
        public static partial void Map(PrcGameTitleDbRootEntry source, [MappingTarget] GameTitleEntry target);
        [MapperIgnoreTarget(nameof(BgmDbRootEntry.Title))]
        [MapperIgnoreTarget(nameof(BgmDbRootEntry.Author))]
        [MapperIgnoreTarget(nameof(BgmDbRootEntry.Copyright))]
        [MapperIgnoreTarget(nameof(BgmDbRootEntry.MusicMod))]
        public static partial void Map(PrcBgmDbRootEntry source, [MappingTarget] BgmDbRootEntry target);
        [MapperIgnoreTarget(nameof(BgmStreamSetEntry.MusicMod))]
        public static partial void Map(PrcBgmStreamSetEntry source, [MappingTarget] BgmStreamSetEntry target);
        [MapperIgnoreTarget(nameof(BgmAssignedInfoEntry.MusicMod))]
        public static partial void Map(PrcBgmAssignedInfoEntry source, [MappingTarget] BgmAssignedInfoEntry target);
        [MapperIgnoreTarget(nameof(BgmStreamPropertyEntry.MusicMod))]
        public static partial void Map(PrcBgmStreamPropertyEntry source, [MappingTarget] BgmStreamPropertyEntry target);
        [MapperIgnoreTarget(nameof(BgmPropertyEntry.AudioVolume))]
        [MapperIgnoreTarget(nameof(BgmPropertyEntry.MusicMod))]
        public static partial void Map(BinBgmPropertyEntry source, [MappingTarget] BgmPropertyEntry target);
        public static partial PlaylistValueEntry ToEntry(PrcBgmPlaylistEntry source);
        public static partial StageEntry ToEntry(StageDbRootEntry source);

        // Entries -> game data
        public static partial PrcSeriesDbRootEntry ToPrc(SeriesEntry source);
        public static partial PrcGameTitleDbRootEntry ToPrc(GameTitleEntry source);
        public static partial PrcBgmDbRootEntry ToPrc(BgmDbRootEntry source);
        public static partial PrcBgmStreamSetEntry ToPrc(BgmStreamSetEntry source);
        public static partial PrcBgmAssignedInfoEntry ToPrc(BgmAssignedInfoEntry source);
        public static partial PrcBgmStreamPropertyEntry ToPrc(BgmStreamPropertyEntry source);
        public static partial PrcBgmPlaylistEntry ToPrc(PlaylistValueEntry source);
        public static partial StageDbRootEntry ToPrc(StageEntry source);
        public static partial BinBgmPropertyEntry ToBin(BgmPropertyEntry source);

        // Configs -> entries
        [MapProperty(nameof(SeriesConfig.Title), nameof(SeriesEntry.MSBTTitle))]
        [MapperIgnoreTarget(nameof(SeriesEntry.IconPath))]
        [MapperIgnoreTarget(nameof(SeriesEntry.MusicMod))]
        public static partial void Map(SeriesConfig source, [MappingTarget] SeriesEntry target);
        [MapProperty(nameof(GameConfig.Title), nameof(GameTitleEntry.MSBTTitle))]
        [MapperIgnoreTarget(nameof(GameTitleEntry.MusicMod))]
        public static partial void Map(GameConfig source, [MappingTarget] GameTitleEntry target);
        [MapperIgnoreTarget(nameof(BgmDbRootEntry.NameId))]
        [MapperIgnoreTarget(nameof(BgmDbRootEntry.SaveNo))]
        [MapperIgnoreTarget(nameof(BgmDbRootEntry.TestDispOrder))]
        [MapperIgnoreTarget(nameof(BgmDbRootEntry.MenuValue))]
        [MapperIgnoreTarget(nameof(BgmDbRootEntry.MusicMod))]
        public static partial void Map(BgmDbRootConfig source, [MappingTarget] BgmDbRootEntry target);
        [MapperIgnoreTarget(nameof(BgmStreamSetEntry.MusicMod))]
        public static partial void Map(BgmStreamSetConfig source, [MappingTarget] BgmStreamSetEntry target);
        [MapperIgnoreTarget(nameof(BgmAssignedInfoEntry.MusicMod))]
        public static partial void Map(BgmAssignedInfoConfig source, [MappingTarget] BgmAssignedInfoEntry target);
        [MapperIgnoreTarget(nameof(BgmStreamPropertyEntry.MusicMod))]
        public static partial void Map(BgmStreamPropertyConfig source, [MappingTarget] BgmStreamPropertyEntry target);
        [MapperIgnoreTarget(nameof(BgmPropertyEntry.AudioVolume))]
        [MapperIgnoreTarget(nameof(BgmPropertyEntry.MusicMod))]
        public static partial void Map(BgmPropertyEntryConfig source, [MappingTarget] BgmPropertyEntry target);
        public static partial void Map(StageConfig source, [MappingTarget] StageEntry target);
        public static partial PlaylistValueEntry ToEntry(PlaylistValueConfig source);

        // Entries -> configs
        [MapProperty(nameof(SeriesEntry.MSBTTitle), nameof(SeriesConfig.Title))]
        [MapperIgnoreTarget(nameof(SeriesConfig.Games))]
        public static partial SeriesConfig ToConfig(SeriesEntry source);
        [MapProperty(nameof(SeriesEntry.MSBTTitle), nameof(SeriesConfig.Title))]
        [MapperIgnoreTarget(nameof(SeriesConfig.Games))]
        public static partial void Map(SeriesEntry source, [MappingTarget] SeriesConfig target);
        [MapProperty(nameof(GameTitleEntry.MSBTTitle), nameof(GameConfig.Title))]
        [MapperIgnoreTarget(nameof(GameConfig.Bgms))]
        public static partial GameConfig ToConfig(GameTitleEntry source);
        [MapProperty(nameof(GameTitleEntry.MSBTTitle), nameof(GameConfig.Title))]
        [MapperIgnoreTarget(nameof(GameConfig.Bgms))]
        public static partial void Map(GameTitleEntry source, [MappingTarget] GameConfig target);
        [MapperIgnoreTarget(nameof(BgmDbRootConfig.NameId))]
        [MapperIgnoreTarget(nameof(BgmDbRootConfig.SaveNo))]
        [MapperIgnoreTarget(nameof(BgmDbRootConfig.TestDispOrder))]
        [MapperIgnoreTarget(nameof(BgmDbRootConfig.MenuValue))]
        [MapperIgnoreTarget(nameof(BgmDbRootConfig.Unk1))]
        [MapperIgnoreTarget(nameof(BgmDbRootConfig.Unk2))]
        [MapperIgnoreTarget(nameof(BgmDbRootConfig.Unk3))]
        [MapperIgnoreTarget(nameof(BgmDbRootConfig.Unk4))]
        [MapperIgnoreTarget(nameof(BgmDbRootConfig.Unk5))]
        [MapperIgnoreTarget(nameof(BgmDbRootConfig.OldTitle))]
        [MapperIgnoreTarget(nameof(BgmDbRootConfig.OldAuthor))]
        [MapperIgnoreTarget(nameof(BgmDbRootConfig.OldCopyright))]
        public static partial BgmDbRootConfig ToConfig(BgmDbRootEntry source);
        public static partial BgmStreamSetConfig ToConfig(BgmStreamSetEntry source);
        [MapperIgnoreTarget(nameof(BgmAssignedInfoConfig.Unk1))]
        public static partial BgmAssignedInfoConfig ToConfig(BgmAssignedInfoEntry source);
        public static partial BgmStreamPropertyConfig ToConfig(BgmStreamPropertyEntry source);
        public static partial BgmPropertyEntryConfig ToConfig(BgmPropertyEntry source);
        [MapperIgnoreTarget(nameof(StageConfig.Unk2))]
        [MapperIgnoreTarget(nameof(StageConfig.Unk3))]
        [MapperIgnoreTarget(nameof(StageConfig.Unk4))]
        public static partial StageConfig ToConfig(StageEntry source);
        public static partial PlaylistConfig ToConfig(PlaylistEntry source);

        // Collections are copied, never shared, and null becomes empty.
        private static Dictionary<string, string> CopyLabels(Dictionary<string, string> labels) => labels == null ? new() : new(labels);
    }
}
