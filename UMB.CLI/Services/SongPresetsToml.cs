using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UMB.CLI.Desktop;

namespace UMB.CLI.Services
{
    /// <summary>
    /// Song presets in series.toml: the series default ([default-track-data]), per-game defaults
    /// ([[game-defaults]]) and named [[presets]]. Unset keys inherit, so they are not written.
    /// </summary>
    public static class SongPresetsToml
    {
        public const string SeriesDefaultTable = "default-track-data";
        public const string GameDefaultsTable = "game-defaults";
        public const string PresetsTable = "presets";

        public static SongPresets Read(string text)
        {
            var seriesDefault = ReadPreset(SeriesToml.TableSection(text, SeriesDefaultTable) ?? "");
            seriesDefault.RecordType = CliUtil.FirstNonEmpty(seriesDefault.RecordType, "original");
            seriesDefault.InSoundtest = CliUtil.FirstNonEmpty(seriesDefault.InSoundtest, "True");
            return new SongPresets
            {
                SeriesDefault = seriesDefault,
                GameDefaults = SeriesToml.TableArrayBlocks(text, GameDefaultsTable).Select(ReadPreset).ToList(),
                Presets = SeriesToml.TableArrayBlocks(text, PresetsTable).Select(ReadPreset).ToList()
            };
        }

        /// <summary>
        /// Writes every preset, keeping volume-db and other keys in [default-track-data]. Game
        /// defaults that set nothing are dropped, so those games use the series default.
        /// </summary>
        public static List<string> Write(IReadOnlyList<string> lines, SongPresets presets)
        {
            var series = presets.SeriesDefault ?? new SongPreset();
            var recordType = CliUtil.FirstNonEmpty(series.RecordType, "original");
            var hidden = series.InSoundtest == "False";
            // [default-track-data] is only created when it would hold a non-default value.
            var hasContent = !string.IsNullOrEmpty(series.Game) || !string.IsNullOrEmpty(series.Author)
                || !string.IsNullOrEmpty(series.Copyright) || !string.IsNullOrEmpty(series.PinchSong) || recordType != "original" || hidden;
            var result = SeriesToml.UpsertTable(lines, SeriesDefaultTable, new[]
            {
                new SeriesToml.Entry("game", Line("game", series.Game ?? "")),
                new SeriesToml.Entry("author", Line("author", series.Author ?? "")),
                new SeriesToml.Entry("copyright", Line("copyright", series.Copyright ?? "")),
                new SeriesToml.Entry("record-type", Line("record-type", recordType)),
                new SeriesToml.Entry("in-soundtest", hidden ? Line("in-soundtest", "False") : null),
                new SeriesToml.Entry("pinch-song", string.IsNullOrEmpty(series.PinchSong) ? null : Line("pinch-song", series.PinchSong))
            }, createIfMissing: hasContent);

            result = SeriesToml.RewriteArrayTables(result, GameDefaultsTable,
                (presets.GameDefaults ?? new()).Where(p => Values(p).Any(v => v.Key is not ("name" or "game") && v.Value != ""))
                    .Select(p => Block(GameDefaultsTable, p)).ToList());
            return SeriesToml.RewriteArrayTables(result, PresetsTable,
                (presets.Presets ?? new()).Select(p => Block(PresetsTable, p)).ToList());
        }

        private static SongPreset ReadPreset(string section)
        {
            var inSoundtest = Regex.Match(section, @"^\s*in-soundtest\s*=\s*(true|false)", RegexOptions.Multiline);
            return new SongPreset
            {
                Name = SeriesToml.String(section, "name"),
                Game = SeriesToml.String(section, "game"),
                Author = SeriesToml.String(section, "author"),
                Copyright = SeriesToml.String(section, "copyright"),
                RecordType = SeriesToml.String(section, "record-type"),
                InSoundtest = inSoundtest.Success ? (inSoundtest.Groups[1].Value == "true" ? "True" : "False") : "",
                PinchSong = SeriesToml.String(section, "pinch-song")
            };
        }

        private static List<string> Block(string table, SongPreset preset) =>
            Values(preset).Where(v => v.Value != "").Select(v => Line(v.Key, v.Value)).Prepend($"[[{table}]]").ToList();

        private static KeyValuePair<string, string>[] Values(SongPreset p) => new[]
        {
            KeyValuePair.Create("name", p.Name ?? ""), KeyValuePair.Create("game", p.Game ?? ""),
            KeyValuePair.Create("author", p.Author ?? ""), KeyValuePair.Create("copyright", p.Copyright ?? ""),
            KeyValuePair.Create("record-type", p.RecordType ?? ""), KeyValuePair.Create("in-soundtest", p.InSoundtest ?? ""),
            KeyValuePair.Create("pinch-song", p.PinchSong ?? "")
        };

        private static string Line(string key, string value) =>
            key == "in-soundtest" ? $"{key} = {(value == "False" ? "false" : "true")}" : $"{key} = \"{SeriesToml.Escape(value)}\"";
    }
}
