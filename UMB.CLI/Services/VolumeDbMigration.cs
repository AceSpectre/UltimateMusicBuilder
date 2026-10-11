using Sma5h.Mods.Music.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace UMB.CLI.Services
{
    /// <summary>
    /// One-time conversion of the legacy linear volume multipliers (1 = unchanged) to dB boosts
    /// (0 = unchanged) in appsettings.json, series.toml and tracks.csv. Files already using the
    /// dB keys are left alone.
    /// </summary>
    public static class VolumeDbMigration
    {
        private const string LegacyCsvColumn = "volume";

        public static void MigrateSettings(string appSettingsPath)
        {
            if (!File.Exists(appSettingsPath)) return;
            var root = JsonNode.Parse(File.ReadAllText(appSettingsPath)) as JsonObject;
            if (root?["Sma5hMusic"] is not JsonObject music) return;

            var changed = RenameToDb(music, "GlobalVolumeMultiplier", "GlobalVolumeDb");
            if (music["LufsNormalization"] is JsonObject lufs)
                changed |= RenameToDb(lufs, "MaxGainMultiplier", "MaxGainDb");
            if (changed)
                File.WriteAllText(appSettingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        public static void MigrateMods(string modsRoot)
        {
            foreach (var seriesDir in ModPaths.VisibleDirs(modsRoot).SelectMany(ModPaths.VisibleDirs))
            {
                MigrateSeriesToml(seriesDir);
                MigrateTracksCsv(seriesDir);
            }
        }

        private static bool RenameToDb(JsonObject obj, string legacyKey, string dbKey)
        {
            if (!obj.TryGetPropertyValue(legacyKey, out var legacy)) return false;
            obj.Remove(legacyKey);
            if (!obj.ContainsKey(dbKey) && legacy != null)
                obj[dbKey] = ToDb(legacy.GetValue<double>());
            return true;
        }

        private static void MigrateSeriesToml(string seriesDir)
        {
            var path = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_SERIES_TOML_FILE);
            if (!File.Exists(path)) return;
            var section = SeriesToml.TableSection(File.ReadAllText(path), "default-track-data") ?? "";
            var legacy = Regex.Match(section, @"^\s*volume\s*=\s*([0-9.]+)", RegexOptions.Multiline);
            if (!legacy.Success) return;

            var entries = new List<SeriesToml.Entry> { new("volume", null) };
            if (!Regex.IsMatch(section, @"^\s*volume-db\s*=", RegexOptions.Multiline))
            {
                var db = ToDb(double.Parse(legacy.Groups[1].Value, CultureInfo.InvariantCulture));
                entries.Add(new("volume-db", $"volume-db = {SeriesToml.FormatDb(db)}"));
            }
            SeriesToml.WriteLines(path, SeriesToml.UpsertTable(SeriesToml.ReadLines(path), "default-track-data", entries, createIfMissing: false));
        }

        private static void MigrateTracksCsv(string seriesDir)
        {
            var path = Path.Combine(seriesDir, MusicConstants.MusicModFiles.FOLDER_MOD_TRACKS_CSV_FILE);
            if (!File.Exists(path)) return;
            var (rows, headers) = TracksCsv.Read(path);
            if (!headers.Contains(LegacyCsvColumn) || headers.Contains(VolumeConfigService.VolumeColumn)) return;

            foreach (var row in rows)
            {
                var legacy = row.Get(LegacyCsvColumn).Trim();
                row[VolumeConfigService.VolumeColumn] = double.TryParse(legacy, NumberStyles.Float, CultureInfo.InvariantCulture, out var m)
                    ? ToDb(m).ToString(CultureInfo.InvariantCulture)
                    : "";
            }
            TracksCsv.Write(path, rows, headers.Select(h => h == LegacyCsvColumn ? VolumeConfigService.VolumeColumn : h).ToList());
        }

        private static double ToDb(double multiplier) => Math.Round(VolumeHelper.MultiplierToDb((float)multiplier), 2);
    }
}
