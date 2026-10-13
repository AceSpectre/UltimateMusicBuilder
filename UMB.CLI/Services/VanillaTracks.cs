using Sma5h.Mods.Music.Helpers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UMB.CLI.Desktop;

namespace UMB.CLI.Services
{
    /// <summary>
    /// Vanilla songs of an existing series are edited through tracks.csv rows whose filename is
    /// the song's ui_bgm id. These helpers keep those rows in sync with the game data.
    /// </summary>
    public static class VanillaTracks
    {
        /// <summary>The sound test songs of a vanilla series (id without the ui_series_ prefix).</summary>
        public static List<VanillaCatalogService.Song> Songs(VanillaCatalogService.Catalog catalog, string seriesId) =>
            catalog.Songs.Where(s => s.TestDispOrder >= 0 && s.SeriesId == MusicConstants.InternalIds.SERIES_ID_PREFIX + seriesId).ToList();

        /// <summary>The vanilla games of a series that have songs, by id.</summary>
        public static List<SeriesGame> Games(VanillaCatalogService.Catalog catalog, string seriesId)
        {
            var used = catalog.Songs.Select(s => s.GameId).ToHashSet();
            return catalog.GameTitles.Where(g => g.SeriesId == MusicConstants.InternalIds.SERIES_ID_PREFIX + seriesId && used.Contains(g.Id))
                .OrderBy(g => g.Id, System.StringComparer.OrdinalIgnoreCase)
                .Select(g => new SeriesGame(g.Id, g.Name))
                .ToList();
        }

        /// <summary>
        /// Appends a row for every vanilla song of an existing series that has none, in sound test
        /// order, and declares the games those rows use. No-op for custom series or without game files.
        /// </summary>
        public static void Backfill(string seriesDir, VanillaCatalogService.Catalog catalog)
        {
            var tomlPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
            var csvPath = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
            if (catalog == null || !File.Exists(tomlPath) || !File.Exists(csvPath)) return;
            var header = SeriesToml.ReadHeader(File.ReadAllText(tomlPath));
            if (!header.ExistingSeries || header.Id == null) return;

            var (rows, headers) = TracksCsv.Read(csvPath);
            var listed = rows.Select(r => r.Get("filename")).ToHashSet();
            var missing = Songs(catalog, header.Id).Where(s => !listed.Contains(s.BgmId)).ToList();
            if (missing.Count == 0) return;

            rows.AddRange(missing.Select(song => new CsvRow
            {
                ["filename"] = song.BgmId, ["game"] = song.GameId, ["title"] = song.Name, ["author"] = song.Author,
                ["copyright"] = song.Copyright, ["record_type"] = song.RecordType, ["special_category"] = song.SpecialCategory,
                [TracksCsv.VolumeDbColumn] = CliUtil.FormatDb(0), ["info1"] = song.Info1, ["in_soundtest"] = "True"
            }));
            TracksCsv.Write(csvPath, rows, headers.Union(TracksCsv.Columns).ToList());
            EnsureSeriesGames(tomlPath, rows, Games(catalog, header.Id));
        }

        /// <summary>Appends [[games]] blocks for known game ids used by rows but not yet declared.</summary>
        public static void EnsureSeriesGames(string seriesTomlPath, List<CsvRow> rows, List<SeriesGame> knownGames)
        {
            if (!File.Exists(seriesTomlPath)) return;

            var text = File.ReadAllText(seriesTomlPath);
            var declared = SeriesToml.Games(text).Select(g => g.Id).ToHashSet();
            var nameById = knownGames.GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First().Name);
            var toAdd = rows.Select(r => r.Get("game").Trim()).Where(id => id.Length > 0).Distinct()
                .Where(id => !declared.Contains(id) && nameById.ContainsKey(id))
                .ToList();
            if (toAdd.Count == 0) return;

            var blocks = string.Concat(toAdd.Select(id =>
                $"\n[[games]]\nid = \"{SeriesToml.Escape(id)}\"\nname = \"{SeriesToml.Escape(nameById[id])}\"\n"));
            File.WriteAllText(seriesTomlPath, $"{text.TrimEnd()}\n{blocks}");
        }
    }
}
