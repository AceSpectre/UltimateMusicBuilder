using System.Collections.Generic;
using System.Linq;
using UMB.CLI.Desktop;

namespace UMB.CLI.Services
{
    /// <summary>
    /// Song presets in series.toml: the series default ([default-track-data]), per-game defaults
    /// ([[game-defaults]]) and named [[presets]]. Unset keys inherit, so they are not written.
    /// </summary>
    public static class SongPresetsToml
    {
        private const string GameDefaultsTable = "game-defaults";
        private const string PresetsTable = "presets";

        public static SongPresets Read(string text) => new()
        {
            SeriesDefault = ReadPreset(SeriesToml.TableSection(text, SeriesToml.DefaultTrackDataTable) ?? ""),
            GameDefaults = SeriesToml.TableArrayBlocks(text, GameDefaultsTable).Select(ReadPreset).ToList(),
            Presets = SeriesToml.TableArrayBlocks(text, PresetsTable).Select(ReadPreset).ToList()
        };

        /// <summary>
        /// Writes every preset, keeping volume-db and other keys in [default-track-data]. Game
        /// defaults that set nothing are dropped, so those games use the series default.
        /// </summary>
        public static List<string> Write(IReadOnlyList<string> lines, SongPresets presets)
        {
            var result = SeriesToml.UpsertTable(lines, SeriesToml.DefaultTrackDataTable,
                Values(presets.SeriesDefault).Where(v => v.Key != "name")
                    .Select(v => new SeriesToml.Entry(v.Key, v.Value == "" ? null : Line(v))).ToList(),
                createIfMissing: true);
            result = SeriesToml.RewriteArrayTables(result, GameDefaultsTable,
                presets.GameDefaults.Where(p => Values(p).Any(v => v.Key is not ("name" or "game") && v.Value != ""))
                    .Select(p => Block(GameDefaultsTable, p)).ToList());
            return SeriesToml.RewriteArrayTables(result, PresetsTable, presets.Presets.Select(p => Block(PresetsTable, p)).ToList());
        }

        private static SongPreset ReadPreset(string section) => new()
        {
            Name = SeriesToml.String(section, "name"),
            Game = SeriesToml.String(section, "game"),
            Author = SeriesToml.String(section, "author"),
            Copyright = SeriesToml.String(section, "copyright"),
            RecordType = SeriesToml.String(section, "record-type"),
            InSoundtest = SeriesToml.String(section, "in-soundtest")
        };

        private static List<string> Block(string table, SongPreset preset) =>
            Values(preset).Where(v => v.Value != "").Select(Line).Prepend($"[[{table}]]").ToList();

        private static (string Key, string Value)[] Values(SongPreset p) => new[]
        {
            ("name", p.Name), ("game", p.Game), ("author", p.Author), ("copyright", p.Copyright),
            ("record-type", p.RecordType), ("in-soundtest", p.InSoundtest)
        };

        private static string Line((string Key, string Value) v) => $"{v.Key} = \"{SeriesToml.Escape(v.Value)}\"";
    }
}
